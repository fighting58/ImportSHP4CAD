using System;
using GeoAPI.Geometries;
using ShpCadImporter.Geometry;

namespace ShpCadImporter.Labeling
{
    /// <summary>
    /// Polygon 중심점에 텍스트를 배치하기 위한 좌표를 계산한다.
    /// 기본적으로 Polylabel을 사용하며, 실패 시 NTS PointOnSurface로 폴백한다.
    /// </summary>
    public static class LabelEngine
    {
        /// <summary>
        /// 텍스트 배치 기준 좌표를 계산한다.
        /// </summary>
        /// <param name="rings">분해된 Polygon Ring 데이터</param>
        /// <returns>{x, y} 좌표 배열</returns>
        public static double[] GetLabelPosition(PolygonRings rings)
        {
            if (rings == null || rings.ExteriorRing == null || rings.ExteriorRing.Length == 0)
            {
                return null;
            }

            try
            {
                // 1. Polylabel 시도 (Precision: 1.0 도면 단위)
                double[] labelPos = Polylabel.GetPolylabel(rings.ExteriorRing, rings.InteriorRings, 1.0);
                
                if (labelPos != null && labelPos.Length == 2)
                {
                    return labelPos;
                }
            }
            catch (Exception)
            {
                // Polylabel 계산 중 예외 발생 시 무시하고 Fallback 진행
            }

            // 2. Fallback: NTS PointOnSurface
            return GetPointOnSurface(rings.SourcePolygon);
        }

        private static double[] GetPointOnSurface(IPolygon polygon)
        {
            if (polygon == null) return null;
            
            try
            {
                IPoint point = polygon.PointOnSurface;
                if (point != null && !point.IsEmpty)
                {
                    return new double[] { point.X, point.Y };
                }
            }
            catch
            {
                // Fallback도 실패하면 null 반환
            }
            
            return null;
        }
    }
}
