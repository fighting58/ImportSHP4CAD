using GeoAPI.Geometries;

namespace ShpCadImporter.SHP
{
    /// <summary>
    /// 단일 SHP Feature를 표현하는 DTO.
    /// Geometry + 속성(MNUM, NTFDATE, ALIAS)을 보유한다.
    /// </summary>
    public class ShpFeature
    {
        /// <summary>
        /// Polygon 또는 MultiPolygon Geometry.
        /// NTS 1.13.x의 GeoAPI IGeometry 타입.
        /// </summary>
        public IGeometry Geometry { get; set; }

        /// <summary>동적 속성 저장 딕셔너리</summary>
        public System.Collections.Generic.Dictionary<string, string> Attributes { get; set; }

        /// <summary>관리번호</summary>
        public string MNUM
        {
            get
            {
                string v;
                return Attributes.TryGetValue("MNUM", out v) ? v : string.Empty;
            }
            set { Attributes["MNUM"] = value; }
        }

        /// <summary>날짜</summary>
        public string NTFDATE
        {
            get
            {
                string v;
                return Attributes.TryGetValue("NTFDATE", out v) ? v : string.Empty;
            }
            set { Attributes["NTFDATE"] = value; }
        }

        /// <summary>별칭</summary>
        public string ALIAS
        {
            get
            {
                string v;
                return Attributes.TryGetValue("ALIAS", out v) ? v : string.Empty;
            }
            set { Attributes["ALIAS"] = value; }
        }

        public ShpFeature()
        {
            Geometry = null;
            Attributes = new System.Collections.Generic.Dictionary<string, string>(
                System.StringComparer.OrdinalIgnoreCase);
        }
    }
}
