using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NetTopologySuite.IO;
using GeoAPI.Geometries;

namespace ShpCadImporter.SHP
{
    /// <summary>
    /// SHP + DBF 파일을 읽어 ShpFeature 리스트를 반환한다.
    /// NetTopologySuite 1.13.x + GeoTools IO 기반.
    /// DBF 인코딩은 CPG 파일로 감지한다 (EUC-KR 등).
    /// </summary>
    public static class ShpReader
    {
        /// <summary>
        /// SHP 파일 경로를 받아 전체 Feature를 읽는다.
        /// </summary>
        /// <param name="shpPath">SHP 파일 절대 경로</param>
        /// <returns>Feature 리스트</returns>
        public static List<ShpFeature> Read(string shpPath)
        {
            if (!File.Exists(shpPath))
            {
                throw new FileNotFoundException("SHP file not found: " + shpPath);
            }

            // 원본 shp파일의 인코딩을 euc-kr로 고정하여 읽기
            Encoding encoding = DetectEncoding(shpPath);

            // 1. 직접 DBF 파일을 EUC-KR로 디코딩하여 속성 데이터 리스트 획득
            string dbfPath = Path.ChangeExtension(shpPath, ".dbf");
            var dbfRecords = DbfRawReader.ReadAllRecords(dbfPath, encoding);

            var features = new List<ShpFeature>();

            // 2. ShapefileReader로 지오메트리만 안전하게 순차 읽기
            var factory = NetTopologySuite.Geometries.GeometryFactory.Default;
            var reader = new ShapefileReader(shpPath, factory);
            var enumerator = reader.GetEnumerator();
            int index = 0;
            while (enumerator.MoveNext())
            {
                var geom = (GeoAPI.Geometries.IGeometry)enumerator.Current;
                
                int dbfIndex = index;
                index++; // 다음 레코드를 위해 인덱스는 무조건 증가

                if (geom == null || geom.IsEmpty)
                {
                    continue; // 빈 기하는 무시하지만 인덱스는 밀리지 않음
                }

                var feature = new ShpFeature();
                feature.Geometry = geom;

                if (dbfIndex < dbfRecords.Count)
                {
                    foreach (var kvp in dbfRecords[dbfIndex])
                    {
                        feature.Attributes[kvp.Key] = kvp.Value;
                    }
                }

                features.Add(feature);
            }

            return features;
        }

        private static Encoding DetectEncoding(string shpPath)
        {
            // 사용자의 요청에 따라 원본 shp(DBF) 파일의 인코딩을 모두 EUC-KR(Codepage 949)로 강제 적용합니다.
            return GetSafeEncoding(949);
        }

        /// <summary>
        /// .NET Core/8 환경 대비 안전하게 인코딩을 가져온다 (동적 Provider 등록 포함).
        /// </summary>
        private static Encoding GetSafeEncoding(int codepage)
        {
            try
            {
                return Encoding.GetEncoding(codepage);
            }
            catch (Exception)
            {
                TryRegisterCodePagesProvider();
                try
                {
                    return Encoding.GetEncoding(codepage);
                }
                catch
                {
                    return Encoding.Default;
                }
            }
        }

        private static Encoding GetSafeEncoding(string name)
        {
            try
            {
                return Encoding.GetEncoding(name);
            }
            catch (Exception)
            {
                TryRegisterCodePagesProvider();
                try
                {
                    return Encoding.GetEncoding(name);
                }
                catch
                {
                    return Encoding.Default;
                }
            }
        }

        public static void TryRegisterCodePagesProvider()
        {
            try
            {
                Type providerType = null;

                // 1. 이미 로드된 모든 어셈블리에서 CodePagesEncodingProvider 검색
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    providerType = assembly.GetType("System.Text.CodePagesEncodingProvider");
                    if (providerType != null)
                    {
                        break;
                    }
                }

                // 2. 찾지 못했다면 어셈블리 동적 로드 시도
                if (providerType == null)
                {
                    try
                    {
                        var asm = System.Reflection.Assembly.Load("System.Text.Encoding.CodePages");
                        if (asm != null)
                        {
                            providerType = asm.GetType("System.Text.CodePagesEncodingProvider");
                        }
                    }
                    catch
                    {
                        // 어셈블리 로드 실패 시 무시
                    }
                }

                // 3. 타입 획득 성공 시 인스턴스를 얻어 공급자로 등록
                if (providerType != null)
                {
                    var instanceProperty = providerType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (instanceProperty != null)
                    {
                        object providerInstance = instanceProperty.GetValue(null, null);
                        if (providerInstance != null)
                        {
                            Type encodingType = typeof(Encoding);
                            var registerMethod = encodingType.GetMethod("RegisterProvider", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            if (registerMethod != null)
                            {
                                registerMethod.Invoke(null, new object[] { providerInstance });
                            }
                        }
                    }
                }
            }
            catch
            {
                // 리플렉션 오류는 무시하고 기본 설정에 위임
            }
        }

    }

    public class DbfRawField
    {
        public string Name { get; set; }
        public char Type { get; set; }
        public int Length { get; set; }
    }

    public static class DbfRawReader
    {
        public static List<string> ReadFieldNames(string dbfPath, Encoding encoding)
        {
            var fieldNames = new List<string>();
            if (!File.Exists(dbfPath)) return fieldNames;

            using (var fs = new FileStream(dbfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                if (fs.Length < 32) return fieldNames;

                br.ReadByte(); // version
                br.ReadBytes(3); // date
                br.ReadInt32(); // numRecords
                short headerLength = br.ReadInt16();
                br.ReadInt16(); // recordLength
                br.ReadBytes(20); // skip remaining 20 bytes of 32-byte header

                while (fs.Position < headerLength - 1)
                {
                    byte nextByte = br.ReadByte();
                    if (nextByte == 0x0D) break;

                    byte[] nameBytes = new byte[11];
                    nameBytes[0] = nextByte;
                    br.Read(nameBytes, 1, 10);

                    string fieldName = encoding.GetString(nameBytes).Trim('\0', ' ');
                    fieldNames.Add(fieldName);

                    br.ReadByte(); // type
                    br.ReadBytes(4); // address
                    br.ReadByte(); // len
                    br.ReadByte(); // decimal
                    br.ReadBytes(14); // reserved
                }
            }
            return fieldNames;
        }

        public static List<Dictionary<string, string>> ReadAllRecords(string dbfPath, Encoding encoding)
        {
            var records = new List<Dictionary<string, string>>();
            if (!File.Exists(dbfPath)) return records;

            using (var fs = new FileStream(dbfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                if (fs.Length < 32) return records;

                br.ReadByte(); // version
                br.ReadBytes(3); // date
                int numRecords = br.ReadInt32();
                short headerLength = br.ReadInt16();
                short recordLength = br.ReadInt16();
                br.ReadBytes(20); // skip remaining 20 bytes of 32-byte header

                var fields = new List<DbfRawField>();
                while (fs.Position < headerLength - 1)
                {
                    byte nextByte = br.ReadByte();
                    if (nextByte == 0x0D) break;

                    byte[] nameBytes = new byte[11];
                    nameBytes[0] = nextByte;
                    br.Read(nameBytes, 1, 10);

                    string fieldName = encoding.GetString(nameBytes).Trim('\0', ' ');

                    char fieldType = (char)br.ReadByte();
                    br.ReadBytes(4); // address
                    byte fieldLen = br.ReadByte();
                    br.ReadByte(); // decimal
                    br.ReadBytes(14); // reserved

                    fields.Add(new DbfRawField
                    {
                        Name = fieldName,
                        Type = fieldType,
                        Length = fieldLen
                    });
                }

                fs.Position = headerLength;

                for (int r = 0; r < numRecords; r++)
                {
                    if (fs.Position + recordLength > fs.Length) break;

                    br.ReadByte(); // delete flag
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var field in fields)
                    {
                        byte[] buffer = br.ReadBytes(field.Length);
                        string value = encoding.GetString(buffer).Trim();
                        value = value.Replace("\0", "").Trim();
                        row[field.Name] = value;
                    }
                    records.Add(row);
                }
            }
            return records;
        }
    }
}
