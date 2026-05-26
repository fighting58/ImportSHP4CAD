using GeoAPI.Geometries;

namespace ShpCadImporter.Geometry
{
    /// <summary>
    /// 단일 Polygon의 Ring 데이터를 보유한다.
    /// ExteriorRing 1개 + InteriorRing(Hole) 0개 이상.
    /// </summary>
    public class PolygonRings
    {
        /// <summary>
        /// ExteriorRing 좌표 배열. [N][2] = {x, y}
        /// </summary>
        public double[][] ExteriorRing { get; set; }

        /// <summary>
        /// InteriorRing(Hole) 좌표 배열 리스트.
        /// 각 Hole은 double[][] 형태.
        /// </summary>
        public double[][][] InteriorRings { get; set; }

        /// <summary>
        /// 원본 NTS Polygon 객체 참조 (Polylabel/PointOnSurface 계산 시 필요).
        /// </summary>
        public IPolygon SourcePolygon { get; set; }

        public PolygonRings()
        {
            InteriorRings = new double[0][][];
        }
    }
}
