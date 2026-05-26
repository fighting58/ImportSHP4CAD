using System.Collections.Generic;
using GeoAPI.Geometries;

namespace ShpCadImporter.Geometry
{
    /// <summary>
    /// MultiPolygon을 개별 Polygon으로 분해하고,
    /// 각 Polygon의 ExteriorRing/InteriorRing을 추출한다.
    /// </summary>
    public static class GeometryProcessor
    {
        /// <summary>
        /// IGeometry(MultiPolygon 또는 Polygon)를 개별 PolygonRings 리스트로 분해한다.
        /// </summary>
        public static List<PolygonRings> Explode(IGeometry geometry)
        {
            var result = new List<PolygonRings>();

            if (geometry == null || geometry.IsEmpty)
            {
                return result;
            }

            if (geometry is IPolygon)
            {
                result.Add(ExtractRings((IPolygon)geometry));
            }
            else if (geometry is IMultiPolygon)
            {
                IMultiPolygon multi = (IMultiPolygon)geometry;
                for (int i = 0; i < multi.NumGeometries; i++)
                {
                    IPolygon polygon = (IPolygon)multi.GetGeometryN(i);
                    if (!polygon.IsEmpty)
                    {
                        result.Add(ExtractRings(polygon));
                    }
                }
            }
            else if (geometry is IGeometryCollection)
            {
                // GeometryCollection 내 Polygon 탐색
                IGeometryCollection collection = (IGeometryCollection)geometry;
                for (int i = 0; i < collection.NumGeometries; i++)
                {
                    IGeometry child = collection.GetGeometryN(i);
                    result.AddRange(Explode(child));
                }
            }

            return result;
        }

        /// <summary>
        /// 단일 Polygon에서 ExteriorRing 좌표와 InteriorRing 좌표를 추출한다.
        /// </summary>
        private static PolygonRings ExtractRings(IPolygon polygon)
        {
            var rings = new PolygonRings();
            rings.SourcePolygon = polygon;

            // ExteriorRing
            rings.ExteriorRing = CoordinatesToArray(polygon.ExteriorRing.Coordinates);

            // InteriorRings (Holes)
            int holeCount = polygon.NumInteriorRings;
            rings.InteriorRings = new double[holeCount][][];
            for (int i = 0; i < holeCount; i++)
            {
                rings.InteriorRings[i] = CoordinatesToArray(
                    polygon.GetInteriorRingN(i).Coordinates);
            }

            return rings;
        }

        /// <summary>
        /// NTS Coordinate 배열을 double[][] 배열로 변환한다.
        /// </summary>
        private static double[][] CoordinatesToArray(Coordinate[] coordinates)
        {
            double[][] result = new double[coordinates.Length][];
            for (int i = 0; i < coordinates.Length; i++)
            {
                result[i] = new double[] { coordinates[i].X, coordinates[i].Y };
            }
            return result;
        }
    }
}
