using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;

namespace ShpCadImporter.Commands
{
    /// <summary>
    /// 원점(200000, 600000)을 기준으로 지적도곽(Map Sheet Frame) 인덱스 격자를
    /// 대화식으로 자동 계산하여 'dokwag'(빨간색) 레이어에 생성하는 AutoCAD 커맨드 클래스.
    /// 1:500 축척은 WD5(도각크기 200m * 150m), 1:1000 축척은 WD10(도각크기 400m * 300m)을 지원합니다.
    /// </summary>
    public class MapIndexCommand
    {
        private const double OriginX = 200000.0;
        private const double OriginY = 600000.0;

        [CommandMethod("MAPINDEX_500")]
        [CommandMethod("WD5")]
        public void RunMapIndex500()
        {
            CreateMapIndex(500);
        }

        [CommandMethod("MAPINDEX_1000")]
        [CommandMethod("WD10")]
        public void RunMapIndex1000()
        {
            CreateMapIndex(1000);
        }

        /// <summary>
        /// 도각 격자 생성을 수행하는 메인 엔진 메서드.
        /// </summary>
        private void CreateMapIndex(int scale)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                ed.WriteMessage(string.Format("\n[지적도곽 인덱스 격자 생성 작업을 시작합니다 - 축척 1:{0}]\n", scale));

                // 1. 도각 셀 크기 정의
                double cellW, cellH;
                if (scale == 500)
                {
                    cellW = 200.0; // 1:500 (도면상 400mm * 0.5 = 200m)
                    cellH = 150.0; // 1:500 (도면상 300mm * 0.5 = 150m)
                }
                else
                {
                    cellW = 400.0; // 1:1000 (도면상 400mm * 1.0 = 400m)
                    cellH = 300.0; // 1:1000 (도면상 300mm * 1.0 = 300m)
                }

                // 2. 작업 모드 입력 선택 (Window가 기본값)
                PromptKeywordOptions pko = new PromptKeywordOptions("\n도각 격자를 산출할 방식을 선택하세요 [객체영역(Object)/윈도우범위(Window)] <Window>: ");
                pko.Keywords.Add("Object", "O", "객체영역(Object)");
                pko.Keywords.Add("Window", "W", "윈도우범위(Window)");
                pko.AllowNone = true;

                PromptResult pr = ed.GetKeywords(pko);
                if (pr.Status == PromptStatus.Cancel) return;

                string mode = pr.StringResult;
                if (string.IsNullOrEmpty(mode)) mode = "Window";

                double minX = 0, minY = 0, maxX = 0, maxY = 0;
                HashSet<ObjectId> selectedIds = new HashSet<ObjectId>();
                bool ok = true;

                if (mode == "Window")
                {
                    ed.WriteMessage("\n--- 윈도우(Window) 지정 범위를 기준으로 격자를 산출합니다 ---");
                    PromptPointResult ppr1 = ed.GetPoint("\n첫 번째 구석 지점을 클릭하세요: ");
                    if (ppr1.Status != PromptStatus.OK) return;
                    Point3d pt1 = ppr1.Value;

                    PromptCornerOptions pco = new PromptCornerOptions("\n반대편 구석 지점을 클릭하세요: ", pt1);
                    PromptPointResult ppr2 = ed.GetCorner(pco);
                    if (ppr2.Status != PromptStatus.OK) return;
                    Point3d pt2 = ppr2.Value;

                    minX = Math.Min(pt1.X, pt2.X);
                    minY = Math.Min(pt1.Y, pt2.Y);
                    maxX = Math.Max(pt1.X, pt2.X);
                    maxY = Math.Max(pt1.Y, pt2.Y);
                }
                else // Object 모드
                {
                    ed.WriteMessage("\n--- 선택한 객체(Object)의 영역을 기준으로 도각 격자를 산출합니다 ---");
                    PromptSelectionOptions pso = new PromptSelectionOptions();
                    pso.MessageForAdding = "\n기준이 될 도면 객체들을 드래그하여 선택하세요: ";

                    PromptSelectionResult psr = ed.GetSelection(pso);
                    if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
                    {
                        ed.WriteMessage("\n[취소됨] 객체가 선택되지 않았습니다.\n");
                        return;
                    }

                    // 객체들의 전체 Bounding Box(Extents) 연산
                    Extents3d combinedExtent = new Extents3d();
                    bool hasFirst = false;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (SelectedObject so in psr.Value)
                        {
                            if (so == null) continue;
                            selectedIds.Add(so.ObjectId);

                            Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                            if (ent != null)
                            {
                                try
                                {
                                    Extents3d ext = ent.GeometricExtents;
                                    if (!hasFirst)
                                    {
                                        combinedExtent = ext;
                                        hasFirst = true;
                                    }
                                    else
                                    {
                                        combinedExtent.AddExtents(ext);
                                    }
                                }
                                catch
                                {
                                    // 빈 문자열이나 원점 정의 에러 우회
                                }
                            }
                        }
                        tr.Commit();
                    }

                    if (!hasFirst)
                    {
                        ed.WriteMessage("\n[오류] 선택한 객체의 기하학적 범위를 연산할 수 없습니다.\n");
                        return;
                    }

                    minX = combinedExtent.MinPoint.X;
                    minY = combinedExtent.MinPoint.Y;
                    maxX = combinedExtent.MaxPoint.X;
                    maxY = combinedExtent.MaxPoint.Y;
                }

                if (!ok) return;

                // 3. 컬럼/로우 인덱스 범위 산출 (원점 대비 셀크기로 나눈 정수 하향 변형)
                int startCol = (int)Math.Floor((minX - OriginX) / cellW);
                int endCol = (int)Math.Floor((maxX - OriginX) / cellW);
                int startRow = (int)Math.Floor((minY - OriginY) / cellH);
                int endRow = (int)Math.Floor((maxY - OriginY) / cellH);

                int createdCount = 0;

                // 4. 트랜잭션 기동 및 도각(dokwag) 레이어 생성
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureDokwagLayer(db, tr);

                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    for (int i = startCol; i <= endCol; i++)
                    {
                        for (int j = startRow; j <= endRow; j++)
                        {
                            double curX = OriginX + i * cellW;
                            double curY = OriginY + j * cellH;

                            Point3d p1 = new Point3d(curX, curY, 0);
                            Point3d p2 = new Point3d(curX + cellW, curY + cellH, 0);

                            bool foundInCell = false;

                            if (mode == "Window")
                            {
                                foundInCell = true;
                            }
                            else
                            {
                                // 객체 방식일 경우, 이 도각 격자 사각형과 만나는 객체들이 있는지 Crossing Selection 수행
                                PromptSelectionResult cellPsr = ed.SelectCrossingWindow(p1, p2);
                                if (cellPsr.Status == PromptStatus.OK && cellPsr.Value != null)
                                {
                                    foreach (SelectedObject cellSo in cellPsr.Value)
                                    {
                                        if (selectedIds.Contains(cellSo.ObjectId))
                                        {
                                            foundInCell = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            if (foundInCell)
                            {
                                // 도곽 폴리라인(Rectangle) 생성
                                Polyline pline = new Polyline();
                                pline.SetDatabaseDefaults();
                                pline.Layer = "dokwag";
                                pline.AddVertexAt(0, new Point2d(p1.X, p1.Y), 0, 0, 0);
                                pline.AddVertexAt(1, new Point2d(p2.X, p1.Y), 0, 0, 0);
                                pline.AddVertexAt(2, new Point2d(p2.X, p2.Y), 0, 0, 0);
                                pline.AddVertexAt(3, new Point2d(p1.X, p2.Y), 0, 0, 0);
                                pline.Closed = true;

                                btr.AppendEntity(pline);
                                tr.AddNewlyCreatedDBObject(pline, true);

                                createdCount++;
                            }
                        }
                    }

                    tr.Commit();
                }

                ed.WriteMessage(string.Format("\n[완료] 총 {0}개의 도각 격자가 'dokwag'(빨간색) 레이어에 성공적으로 생성되었습니다.\n", createdCount));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] 도각 격자 생성 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
        }

        /// <summary>
        /// 도각(dokwag) 빨간색(1) 레이어를 보장 생성합니다.
        /// </summary>
        private static void EnsureDokwagLayer(Database db, Transaction tr)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has("dokwag"))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = "dokwag";
                ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, 1); // 빨간색 (Color 1)

                LinetypeTable ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has("Continuous"))
                {
                    ltr.LinetypeObjectId = ltt["Continuous"];
                }

                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
                lt.DowngradeOpen();
            }
        }
    }
}
