using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ShpCadImporter.CAD
{
    /// <summary>
    /// AutoCAD Entity 생성기.
    /// LWPOLYLINE, DBText를 생성하여 BlockTableRecord에 추가한다.
    /// </summary>
    public static class CadEntityBuilder
    {
        /// <summary>
        /// Closed LWPOLYLINE을 생성하여 ModelSpace에 추가한다.
        /// </summary>
        /// <param name="ring">좌표 배열 [N][2] = {x, y}</param>
        /// <param name="layerName">대상 레이어 이름</param>
        /// <param name="btr">BlockTableRecord (ModelSpace)</param>
        /// <param name="tr">현재 Transaction</param>
        public static void CreatePolyline(double[][] ring, string layerName,
            BlockTableRecord btr, Transaction tr)
        {
            if (ring == null || ring.Length < 3)
            {
                return; // 유효하지 않은 Ring은 Skip
            }

            Polyline pline = new Polyline();
            pline.SetDatabaseDefaults();

            // 좌표 추가 (마지막 좌표가 첫 좌표와 같으면 제외 — Closed 속성이 처리)
            int count = ring.Length;
            // SHP Ring은 보통 첫/끝 좌표가 동일하므로 마지막을 제거
            if (count > 1 &&
                ring[0][0] == ring[count - 1][0] &&
                ring[0][1] == ring[count - 1][1])
            {
                count--;
            }

            for (int i = 0; i < count; i++)
            {
                pline.AddVertexAt(i, new Point2d(ring[i][0], ring[i][1]), 0, 0, 0);
            }

            pline.Closed = true;
            pline.Layer = layerName;

            btr.AppendEntity(pline);
            tr.AddNewlyCreatedDBObject(pline, true);
        }

        /// <summary>
        /// DBText를 생성하여 ModelSpace에 추가한다.
        /// MiddleCenter 정렬, Height=0.5 도면단위.
        /// AutoCAD 2013 호환 순서: SetDatabaseDefaults → Position → Height → Mode → AlignmentPoint
        /// </summary>
        /// <param name="value">텍스트 값</param>
        /// <param name="position">삽입점 {x, y}</param>
        /// <param name="layerName">대상 레이어 이름</param>
        /// <param name="btr">BlockTableRecord (ModelSpace)</param>
        /// <param name="tr">현재 Transaction</param>
        public static void CreateText(string value, double[] position, string layerName,
            BlockTableRecord btr, Transaction tr)
        {
            if (string.IsNullOrEmpty(value) || position == null || position.Length < 2)
            {
                return;
            }

            DBText text = new DBText();

            // AutoCAD 2013 호환 순서 (계획서 §8.5)
            // 1. SetDatabaseDefaults
            text.SetDatabaseDefaults();

            // 2. Position (초기 삽입점)
            text.Position = new Point3d(position[0], position[1], 0);

            // 3. Height
            text.Height = 0.5;

            // 4. TextString
            text.TextString = value;

            // 5. HorizontalMode + VerticalMode (MiddleCenter)
            text.HorizontalMode = TextHorizontalMode.TextCenter;
            text.VerticalMode = TextVerticalMode.TextVerticalMid;

            // 6. AlignmentPoint (MiddleCenter 사용 시 필수)
            text.AlignmentPoint = new Point3d(position[0], position[1], 0);

            // 7. Layer
            text.Layer = layerName;

            btr.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
        }
    }
}
