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

            var features = new List<ShpFeature>();

            // ShapefileDataReader로 SHP + DBF 동시 읽기
            using (var reader = new ShapefileDataReader(shpPath, 
                NetTopologySuite.Geometries.GeometryFactory.Default))
            {
                // 원본 shp파일의 인코딩을 euc-kr로 강제 설정
                reader.DbaseHeader.Encoding = encoding;

                // DBF 헤더로부터 사용 가능한 모든 필드 이름 추출
                DbaseFileHeader header = reader.DbaseHeader;
                List<string> fieldNames = new List<string>();
                for (int i = 0; i < header.NumFields; i++)
                {
                    fieldNames.Add(header.Fields[i].Name);
                }

                while (reader.Read())
                {
                    IGeometry geom = reader.Geometry;

                    // Empty Geometry는 Skip (예외 처리 정책)
                    if (geom == null || geom.IsEmpty)
                    {
                        continue;
                    }

                    var feature = new ShpFeature();
                    feature.Geometry = geom;

                    // 전체 속성을 동적으로 딕셔너리에 로드 (0-indexed 순차 바인딩)
                    for (int i = 0; i < fieldNames.Count; i++)
                    {
                        string fieldName = fieldNames[i];
                        string value = ReadStringField(reader, i, encoding);
                        feature.Attributes[fieldName] = value;
                    }

                    features.Add(feature);
                }
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

        /// <summary>
        /// DbaseFileHeader에서 필드 이름으로 인덱스를 찾는다.
        /// 대소문자 무시. 필드가 없으면 -1 반환.
        /// </summary>
        private static int FindFieldIndex(DbaseFileHeader header, string fieldName)
        {
            for (int i = 0; i < header.NumFields; i++)
            {
                if (string.Equals(header.Fields[i].Name, fieldName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// DBF 레코드에서 문자열 필드를 읽는다.
        /// 필드가 없거나 값이 null이면 빈 문자열을 반환한다.
        /// EUC-KR 인코딩 시 바이트 배열에서 직접 디코딩한다.
        /// </summary>
        private static string ReadStringField(ShapefileDataReader reader, 
            int fieldIndex, Encoding encoding)
        {
            // 필드 인덱스가 유효하지 않으면 빈 문자열
            if (fieldIndex < 0 || fieldIndex >= reader.FieldCount)
            {
                return string.Empty;
            }

            try
            {
                object value = reader.GetValue(fieldIndex);

                if (value == null || value is DBNull)
                {
                    return string.Empty;
                }

                string result = value.ToString().Trim();
                return result;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
