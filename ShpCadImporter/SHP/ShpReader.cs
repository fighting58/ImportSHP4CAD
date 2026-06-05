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
            // 1. CPG 파일이 존재하면 설정 우선 처리
            string cpgPath = Path.ChangeExtension(shpPath, ".cpg");
            if (File.Exists(cpgPath))
            {
                try
                {
                    string cpgContent = File.ReadAllText(cpgPath).Trim();
                    if (!string.IsNullOrEmpty(cpgContent))
                    {
                        string encodingName = cpgContent.ToUpperInvariant()
                            .Replace("-", "")
                            .Replace("_", "");

                        if (encodingName == "EUCKR" || encodingName == "KSCOMPLIANT" || encodingName == "949")
                        {
                            return GetSafeEncoding(949);
                        }
                        if (encodingName == "UTF8" || encodingName == "65001")
                        {
                            return Encoding.UTF8;
                        }
                        return GetSafeEncoding(cpgContent.Trim());
                    }
                }
                catch
                {
                    // CPG 파일 오류 시 무시하고 다음 단계로 진행
                }
            }

            // 2. CPG 파일이 없는 경우, DBF 파일 본문의 바이트 데이터를 Heuristic하게 분석하여 UTF-8 판단
            string dbfPath = Path.ChangeExtension(shpPath, ".dbf");
            if (File.Exists(dbfPath))
            {
                try
                {
                    using (var fs = new FileStream(dbfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (fs.Length > 32)
                        {
                            byte[] header = new byte[32];
                            fs.Read(header, 0, 32);

                            int numRecords = BitConverter.ToInt32(header, 4);
                            short headerLength = BitConverter.ToInt16(header, 8);
                            short recordLength = BitConverter.ToInt16(header, 10);

                            // 최대 상위 5개의 레코드를 읽어 한글 문자가 유효한 UTF-8 형식의 멀티바이트인지 검사
                            int sampleCount = Math.Min(5, numRecords);
                            if (sampleCount > 0 && fs.Length >= headerLength + (sampleCount * recordLength))
                            {
                                byte[] sampleBuffer = new byte[sampleCount * recordLength];
                                fs.Position = headerLength;
                                fs.Read(sampleBuffer, 0, sampleBuffer.Length);

                                if (IsUtf8Sequence(sampleBuffer))
                                {
                                    return Encoding.UTF8;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 분석 실패 시 무시하고 기본 설정 위임
                }
            }

            // 3. 디폴트 값: 한국 사용자 환경에 최적화된 EUC-KR (949) 사용
            return GetSafeEncoding(949);
        }

        /// <summary>
        /// 바이트 시퀀스가 깨지지 않은 UTF-8 형식의 멀티바이트 문자를 포함하고 있는지 판별합니다.
        /// </summary>
        private static bool IsUtf8Sequence(byte[] buffer)
        {
            int i = 0;
            int length = buffer.Length;
            bool hasMultiByte = false;

            while (i < length)
            {
                byte b = buffer[i];

                if (b < 0x80)
                {
                    i++;
                    continue;
                }

                // 2바이트 UTF-8 (110xxxxx 10xxxxxx)
                if ((b & 0xE0) == 0xC0)
                {
                    if (i + 1 >= length || (buffer[i + 1] & 0xC0) != 0x80) return false;
                    hasMultiByte = true;
                    i += 2;
                }
                // 3바이트 UTF-8 (1110xxxx 10xxxxxx 10xxxxxx) - 한글이 3바이트를 주로 차지함
                else if ((b & 0xF0) == 0xE0)
                {
                    if (i + 2 >= length || 
                        (buffer[i + 1] & 0xC0) != 0x80 || 
                        (buffer[i + 2] & 0xC0) != 0x80) return false;
                    hasMultiByte = true;
                    i += 3;
                }
                // 4바이트 UTF-8 (11110xxx 10xxxxxx 10xxxxxx 10xxxxxx)
                else if ((b & 0xF8) == 0xF0)
                {
                    if (i + 3 >= length || 
                        (buffer[i + 1] & 0xC0) != 0x80 || 
                        (buffer[i + 2] & 0xC0) != 0x80 || 
                        (buffer[i + 3] & 0xC0) != 0x80) return false;
                    hasMultiByte = true;
                    i += 4;
                }
                else
                {
                    // 비정상적인 UTF-8 시작 바이트 검출 시 실패
                    return false;
                }
            }

            return hasMultiByte;
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
