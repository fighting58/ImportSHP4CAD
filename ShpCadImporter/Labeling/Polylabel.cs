using System;
using System.Collections.Generic;

namespace ShpCadImporter.Labeling
{
    /// <summary>
    /// Mapbox Polylabel 알고리즘 C# 포팅.
    /// 폴리곤의 시각적 중심(Pole of Inaccessibility)을 찾는다.
    /// </summary>
    public static class Polylabel
    {
        // 큐 요소
        private class Cell : IComparable<Cell>
        {
            public double X { get; private set; }
            public double Y { get; private set; }
            public double H { get; private set; }
            public double D { get; private set; }
            public double Max { get; private set; }

            public Cell(double x, double y, double h, double polygonSq, double[][] polygon, double[][][] holes)
            {
                X = x;
                Y = y;
                H = h;
                D = PointToPolygonDist(x, y, polygon, holes);
                Max = D + h * Math.Sqrt(2);
            }

            public int CompareTo(Cell other)
            {
                // Max 값이 큰 것이 우선순위가 높도록 (최대 힙 역할)
                return other.Max.CompareTo(Max);
            }
        }

        /// <summary>
        /// 폴리곤 내부에 가장 깊숙한 점을 찾는다.
        /// </summary>
        /// <param name="polygon">Exterior Ring 좌표 배열 [N][2]</param>
        /// <param name="holes">Interior Rings 좌표 배열 리스트</param>
        /// <param name="precision">정밀도 (도면 단위)</param>
        /// <returns>{x, y} 좌표 배열</returns>
        public static double[] GetPolylabel(double[][] polygon, double[][][] holes, double precision = 1.0)
        {
            if (polygon == null || polygon.Length == 0) return null;

            // Bounding Box 계산
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            for (int i = 0; i < polygon.Length; i++)
            {
                double x = polygon[i][0];
                double y = polygon[i][1];
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }

            double width = maxX - minX;
            double height = maxY - minY;
            double cellSize = Math.Min(width, height);
            double h = cellSize / 2;
            
            // 면적이 0이거나 유효하지 않은 바운딩 박스
            if (cellSize == 0)
            {
                return new double[] { minX, minY };
            }

            // 폴리곤 면적 (부호 있음)
            double polygonSq = GetPolygonArea(polygon);

            // 초기 Cell 리스트 생성
            var cellQueue = new List<Cell>();

            // Bounding Box를 그리드로 분할하여 초기 Cell 추가
            for (double x = minX; x < maxX; x += cellSize)
            {
                for (double y = minY; y < maxY; y += cellSize)
                {
                    cellQueue.Add(new Cell(x + h, y + h, h, polygonSq, polygon, holes));
                }
            }

            // 가장 좋은 위치 찾기 (Best Cell)
            Cell bestCell = GetCentroidCell(polygon, holes);
            
            // 초기 Bounding Box 중심도 테스트
            Cell bboxCell = new Cell(minX + width / 2, minY + height / 2, 0, polygonSq, polygon, holes);
            if (bboxCell.D > bestCell.D) bestCell = bboxCell;

            int numProbes = cellQueue.Count;
            // 큐 정렬 (Max 기준 내림차순)
            cellQueue.Sort();

            while (cellQueue.Count > 0)
            {
                // Dequeue
                Cell cell = cellQueue[0];
                cellQueue.RemoveAt(0);

                // 현재까지의 최고 Cell 갱신
                if (cell.D > bestCell.D)
                {
                    bestCell = cell;
                }

                // cell.Max가 bestCell.D + precision 이하면 더 이상 탐색 불필요
                if (cell.Max - bestCell.D <= precision) continue;

                // Cell 분할
                h = cell.H / 2;
                cellQueue.Add(new Cell(cell.X - h, cell.Y - h, h, polygonSq, polygon, holes));
                cellQueue.Add(new Cell(cell.X + h, cell.Y - h, h, polygonSq, polygon, holes));
                cellQueue.Add(new Cell(cell.X - h, cell.Y + h, h, polygonSq, polygon, holes));
                cellQueue.Add(new Cell(cell.X + h, cell.Y + h, h, polygonSq, polygon, holes));
                numProbes += 4;

                // 재정렬
                cellQueue.Sort();
            }

            return new double[] { bestCell.X, bestCell.Y };
        }

        private static Cell GetCentroidCell(double[][] polygon, double[][][] holes)
        {
            double area = 0;
            double x = 0;
            double y = 0;
            
            for (int i = 0, len = polygon.Length, j = len - 1; i < len; j = i++)
            {
                double[] a = polygon[i];
                double[] b = polygon[j];
                double f = a[0] * b[1] - b[0] * a[1];
                x += (a[0] + b[0]) * f;
                y += (a[1] + b[1]) * f;
                area += f * 3;
            }
            
            if (area == 0) return new Cell(polygon[0][0], polygon[0][1], 0, 0, polygon, holes);
            
            return new Cell(x / area, y / area, 0, 0, polygon, holes);
        }

        private static double GetPolygonArea(double[][] polygon)
        {
            double area = 0;
            for (int i = 0, len = polygon.Length, j = len - 1; i < len; j = i++)
            {
                area += polygon[i][0] * polygon[j][1] - polygon[j][0] * polygon[i][1];
            }
            return area / 2;
        }

        private static double PointToPolygonDist(double x, double y, double[][] polygon, double[][][] holes)
        {
            bool inside = false;
            double minDistSq = double.MaxValue;

            // Exterior Ring 검사
            for (int i = 0, len = polygon.Length, j = len - 1; i < len; j = i++)
            {
                double[] a = polygon[i];
                double[] b = polygon[j];

                if ((a[1] > y != b[1] > y) &&
                    (x < (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1]) + a[0]))
                {
                    inside = !inside;
                }

                minDistSq = Math.Min(minDistSq, GetSegDistSq(x, y, a, b));
            }

            // Interior Rings(Holes) 검사
            if (holes != null)
            {
                for (int h = 0; h < holes.Length; h++)
                {
                    double[][] hole = holes[h];
                    for (int i = 0, len = hole.Length, j = len - 1; i < len; j = i++)
                    {
                        double[] a = hole[i];
                        double[] b = hole[j];

                        if ((a[1] > y != b[1] > y) &&
                            (x < (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1]) + a[0]))
                        {
                            inside = !inside;
                        }

                        minDistSq = Math.Min(minDistSq, GetSegDistSq(x, y, a, b));
                    }
                }
            }

            return (inside ? 1 : -1) * Math.Sqrt(minDistSq);
        }

        private static double GetSegDistSq(double px, double py, double[] a, double[] b)
        {
            double x = a[0];
            double y = a[1];
            double dx = b[0] - x;
            double dy = b[1] - y;

            if (dx != 0 || dy != 0)
            {
                double t = ((px - x) * dx + (py - y) * dy) / (dx * dx + dy * dy);

                if (t > 1)
                {
                    x = b[0];
                    y = b[1];
                }
                else if (t > 0)
                {
                    x += dx * t;
                    y += dy * t;
                }
            }

            dx = px - x;
            dy = py - y;

            return dx * dx + dy * dy;
        }
    }
}
