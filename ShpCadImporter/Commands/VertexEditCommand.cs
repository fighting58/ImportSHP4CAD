using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ShpCadImporter.Commands
{
    /// <summary>
    /// 폴리라인(LWPolyline)의 정점을 대화식으로 추가(VAD)하거나 제거(VDE)할 수 있는 AutoCAD 커맨드 클래스.
    /// 기존 LISP의 VERTEX_ADD / VERTEX_DEL 기능을 C# .NET API로 변환하였으며,
    /// 선(Line)을 선택하는 경우 폴리라인으로 자동 전환하여 정점 추가를 수행합니다.
    /// </summary>
    public class VertexEditCommand
    {
        [CommandMethod("VERTEX_ADD")]
        [CommandMethod("VAD")]
        public void AddPolylineVertex()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            object oldOsMode = null;
            ObjectId polyId = ObjectId.Null;
            try
            {
                // OSNAP 백업 및 끝점(1) 지정
                try
                {
                    oldOsMode = Application.GetSystemVariable("OSMODE");
                    Application.SetSystemVariable("OSMODE", 1);
                }
                catch
                {
                    // 예외 발생 시 무시
                }

                ed.WriteMessage("\n[폴리라인 정점 추가 작업을 시작합니다: VA/VAD]\n");

                // 1. 대상 객체 선택 (Line 또는 LWPolyline)
                TypedValue[] tv = new TypedValue[]
                {
                    new TypedValue((int)DxfCode.Operator, "<or"),
                    new TypedValue((int)DxfCode.Start, "LINE"),
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                    new TypedValue((int)DxfCode.Operator, "or>")
                };
                SelectionFilter filter = new SelectionFilter(tv);

                PromptEntityOptions peo = new PromptEntityOptions("\n정점을 추가할 객체(Line/LWPolyline)를 선택하세요: ");
                peo.SetRejectMessage("\n선택한 객체는 Line 또는 폴리라인이어야 합니다.");
                peo.AddAllowedClass(typeof(Line), exactMatch: false);
                peo.AddAllowedClass(typeof(Polyline), exactMatch: false);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 객체가 선택되지 않았습니다.\n");
                    return;
                }

                polyId = ObjectId.Null;

                // 2. Line인 경우 Polyline으로 변환 후 ObjectId 획득
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    Entity ent = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;

                    if (ent is Line)
                    {
                        Line line = (Line)ent;
                        Polyline pline = new Polyline();
                        pline.SetDatabaseDefaults();
                        pline.AddVertexAt(0, new Point2d(line.StartPoint.X, line.StartPoint.Y), 0, 0, 0);
                        pline.AddVertexAt(1, new Point2d(line.EndPoint.X, line.EndPoint.Y), 0, 0, 0);
                        
                        pline.Layer = line.Layer;
                        pline.Color = line.Color;
                        pline.Linetype = line.Linetype;
                        pline.LinetypeScale = line.LinetypeScale;
                        pline.LineWeight = line.LineWeight;

                        btr.AppendEntity(pline);
                        tr.AddNewlyCreatedDBObject(pline, true);

                        line.UpgradeOpen();
                        line.Erase();

                        polyId = pline.ObjectId;
                        ed.WriteMessage("\n선(Line) 객체가 폴리라인으로 자동 전환되었습니다.\n");
                    }
                    else if (ent is Polyline)
                    {
                        polyId = ent.ObjectId;
                    }

                    tr.Commit();
                }

                if (polyId == ObjectId.Null) return;

                // 대화식 편집 대상 객체 하이라이트(Highlight) 적용
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Entity ent = tr.GetObject(polyId, OpenMode.ForRead) as Entity;
                    if (ent != null)
                    {
                        ent.Highlight();
                    }
                    tr.Commit();
                }

                // 3. 대화식 정점 추가 루프
                PromptPointOptions ppo = new PromptPointOptions("\n정점을 추가할 위치를 클릭하세요 (종료하려면 Enter): ");
                ppo.AllowNone = true;

                while (true)
                {
                    PromptPointResult ppr = ed.GetPoint(ppo);
                    if (ppr.Status == PromptStatus.None) // Enter 누름
                    {
                        break;
                    }
                    if (ppr.Status != PromptStatus.OK)
                    {
                        break;
                    }

                    Point3d p = ppr.Value;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Polyline pline = tr.GetObject(polyId, OpenMode.ForWrite) as Polyline;
                        if (pline != null)
                        {
                            // A. 정점 중복 체크 (0.0005m 이내에 기존 정점이 있는지 확인)
                            bool isDuplicate = false;
                            for (int i = 0; i < pline.NumberOfVertices; i++)
                            {
                                Point2d pt = pline.GetPoint2dAt(i);
                                double dist = Math.Sqrt(Math.Pow(p.X - pt.X, 2) + Math.Pow(p.Y - pt.Y, 2));
                                if (dist <= 0.0005)
                                {
                                    isDuplicate = true;
                                    break;
                                }
                            }

                            if (isDuplicate)
                            {
                                ed.WriteMessage("\n이미 정점이 존재하는 위치(0.0005m 이내)이므로 추가하지 않았습니다.");
                            }
                            else
                            {
                                // B. 곡선 상 가장 가까운 점 및 해당 지점의 Parameter 획득
                                Point3d closestPoint = pline.GetClosestPointTo(p, false);
                                double param = 0.0;
                                try
                                {
                                    param = pline.GetParameterAtPoint(closestPoint);
                                }
                                catch
                                {
                                    // 예외 발생 시 인접 구간 계산
                                    param = 0.0;
                                }

                                int idx = (int)Math.Floor(param) + 1;
                                if (idx < 0) idx = 0;
                                if (idx > pline.NumberOfVertices) idx = pline.NumberOfVertices;

                                // C. 정점 신규 추가
                                pline.AddVertexAt(idx, new Point2d(p.X, p.Y), 0, 0, 0);
                                pline.RecordGraphicsModified(true);
                                ed.WriteMessage(string.Format("\n새로운 정점이 {0}번 인덱스에 추가되었습니다.", idx + 1));
                            }
                        }

                        tr.Commit();
                    }

                    doc.TransactionManager.QueueForGraphicsFlush();
                    ed.UpdateScreen();

                    // 정점 추가 및 화면 갱신으로 풀린 하이라이트 상태 재적용
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Entity ent = tr.GetObject(polyId, OpenMode.ForRead) as Entity;
                        if (ent != null)
                        {
                            ent.Highlight();
                        }
                        tr.Commit();
                    }
                }

                ed.WriteMessage("\n[완료] 정점 추가 작업을 마쳤습니다.\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] 정점 추가 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
            finally
            {
                // 편집 대상 객체 하이라이트 해제(Unhighlight)
                if (polyId != ObjectId.Null && !polyId.IsErased)
                {
                    try
                    {
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            Entity ent = tr.GetObject(polyId, OpenMode.ForRead) as Entity;
                            if (ent != null)
                            {
                                ent.Unhighlight();
                            }
                            tr.Commit();
                        }
                    }
                    catch
                    {
                        // 예외 발생 시 무시
                    }
                }

                // OSNAP 원복
                if (oldOsMode != null)
                {
                    try
                    {
                        Application.SetSystemVariable("OSMODE", oldOsMode);
                    }
                    catch
                    {
                        // 예외 발생 시 무시
                    }
                }
            }
        }

        [CommandMethod("VERTEX_DEL")]
        [CommandMethod("VDE")]
        public void DeletePolylineVertex()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            object oldOsMode = null;
            ObjectId polyId = ObjectId.Null;
            try
            {
                // OSNAP 백업 및 끝점(1) 지정
                try
                {
                    oldOsMode = Application.GetSystemVariable("OSMODE");
                    Application.SetSystemVariable("OSMODE", 1);
                }
                catch
                {
                    // 예외 발생 시 무시
                }

                ed.WriteMessage("\n[폴리라인 정점 제거 작업을 시작합니다: VD/VDE]\n");

                // 1. 대상 폴리라인 선택
                PromptEntityOptions peo = new PromptEntityOptions("\n정점을 제거할 LWPolyline을 선택하세요: ");
                peo.SetRejectMessage("\n선택한 객체는 폴리라인이어야 합니다.");
                peo.AddAllowedClass(typeof(Polyline), exactMatch: false);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 객체가 선택되지 않았습니다.\n");
                    return;
                }

                polyId = per.ObjectId;

                // 대화식 편집 대상 객체 하이라이트(Highlight) 적용
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Entity ent = tr.GetObject(polyId, OpenMode.ForRead) as Entity;
                    if (ent != null)
                    {
                        ent.Highlight();
                    }
                    tr.Commit();
                }

                // 2. 대화식 정점 제거 루프
                PromptPointOptions ppo = new PromptPointOptions("\n제거할 정점 부근을 클릭하세요 (종료하려면 Enter): ");
                ppo.AllowNone = true;

                while (true)
                {
                    PromptPointResult ppr = ed.GetPoint(ppo);
                    if (ppr.Status == PromptStatus.None) // Enter 누름
                    {
                        break;
                    }
                    if (ppr.Status != PromptStatus.OK)
                    {
                        break;
                    }

                    Point3d p = ppr.Value;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Polyline pline = tr.GetObject(polyId, OpenMode.ForWrite) as Polyline;
                        if (pline != null)
                        {
                            // A. 클릭 점과 가장 가까운 정점 인덱스 탐색
                            double minDist = double.MaxValue;
                            int minIdx = -1;

                            for (int i = 0; i < pline.NumberOfVertices; i++)
                            {
                                Point2d pt = pline.GetPoint2dAt(i);
                                double dist = Math.Sqrt(Math.Pow(p.X - pt.X, 2) + Math.Pow(p.Y - pt.Y, 2));
                                if (dist < minDist)
                                {
                                    minDist = dist;
                                    minIdx = i;
                                }
                            }

                            // B. 5mm(0.005m) 허용 오차 검증 및 정점 개수 한계 확인
                            if (minDist <= 0.005)
                            {
                                if (pline.NumberOfVertices <= 2)
                                {
                                    ed.WriteMessage("\n에러: 폴리라인의 정점 개수가 2개 이하이므로 더 이상 정점을 제거할 수 없습니다.");
                                }
                                else
                                {
                                    pline.RemoveVertexAt(minIdx);
                                    pline.RecordGraphicsModified(true);
                                    ed.WriteMessage(string.Format("\n{0}번째 정점이 제거되었습니다.", minIdx + 1));
                                }
                            }
                            else
                            {
                                ed.WriteMessage("\n정점이 감지되지 않았습니다. (정점과의 오차 5mm 이내여야 함)");
                            }
                        }

                        tr.Commit();
                    }

                    doc.TransactionManager.QueueForGraphicsFlush();
                    ed.UpdateScreen();

                    // 정점 삭제 및 화면 갱신으로 풀린 하이라이트 상태 재적용
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Entity ent = tr.GetObject(polyId, OpenMode.ForRead) as Entity;
                        if (ent != null)
                        {
                            ent.Highlight();
                        }
                        tr.Commit();
                    }
                }

                ed.WriteMessage("\n[완료] 정점 제거 작업을 마쳤습니다.\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] 정점 제거 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
            finally
            {
                // 편집 대상 객체 하이라이트 해제(Unhighlight)
                if (polyId != ObjectId.Null && !polyId.IsErased)
                {
                    try
                    {
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            Entity ent = tr.GetObject(polyId, OpenMode.ForRead) as Entity;
                            if (ent != null)
                            {
                                ent.Unhighlight();
                            }
                            tr.Commit();
                        }
                    }
                    catch
                    {
                        // 예외 발생 시 무시
                    }
                }

                // OSNAP 원복
                if (oldOsMode != null)
                {
                    try
                    {
                        Application.SetSystemVariable("OSMODE", oldOsMode);
                    }
                    catch
                    {
                        // 예외 발생 시 무시
                    }
                }
            }
        }
    }
}
