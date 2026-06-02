using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ShpCadImporter.Commands
{
    /// <summary>
    /// 선택한 객체(POINT, LINE, LWPOLYLINE, CIRCLE, ARC, TEXT, MTEXT)의 좌표를
    /// 오사오입(Round Half to Even, Banker's Rounding) 방식을 활용해
    /// 소수점 아래 지정 자릿수(2자리 또는 3자리)로 정밀 조정하는 AutoCAD 커맨드 클래스.
    /// 기존 객체는 유지하고, 초록색(Color Index 3)의 조정된 복제 객체를 생성합니다.
    /// </summary>
    public class CoordRoundCommand
    {
        [CommandMethod("COORDROUND_OSA2")]
        [CommandMethod("OSA2")]
        public void RunCoordRoundOsa2()
        {
            RoundCoordinates(2);
        }

        [CommandMethod("COORDROUND_OSA3")]
        [CommandMethod("OSA3")]
        public void RunCoordRoundOsa3()
        {
            RoundCoordinates(3);
        }

        /// <summary>
        /// 선택 객체들의 좌표를 지정된 소수점 자리수로 오사오입하여 초록색(Color 3) 복제본을 생성하는 공용 엔진 메서드.
        /// </summary>
        private void RoundCoordinates(int prec)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                ed.WriteMessage(string.Format("\n[좌표 소수점 정밀도 조정 - 소수점 {0}자리(오사오입)]\n", prec));

                // 1. 선택 필터 구성 (POINT, LINE, LWPOLYLINE, CIRCLE, ARC, TEXT, MTEXT)
                TypedValue[] tv = new TypedValue[]
                {
                    new TypedValue((int)DxfCode.Operator, "<or"),
                    new TypedValue((int)DxfCode.Start, "POINT"),
                    new TypedValue((int)DxfCode.Start, "LINE"),
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                    new TypedValue((int)DxfCode.Start, "CIRCLE"),
                    new TypedValue((int)DxfCode.Start, "ARC"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Operator, "or>")
                };
                SelectionFilter filter = new SelectionFilter(tv);

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\n좌표를 조정할 객체(POINT, LINE, Polyline, CIRCLE, ARC, TEXT, MTEXT)를 선택하세요: ";

                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 객체가 선택되지 않았습니다.\n");
                    return;
                }

                SelectionSet ss = psr.Value;
                if (ss == null || ss.Count == 0) return;

                int convertedCount = 0;

                // 2. 트랜잭션 기동
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    foreach (SelectedObject so in ss)
                    {
                        if (so == null) continue;

                        Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        // 원래 객체의 비파괴 복제본(Clone) 생성
                        Entity cloneEnt = ent.Clone() as Entity;
                        if (cloneEnt == null) continue;

                        // 복제된 객체 초록색(색상 인덱스 3) 적용
                        cloneEnt.ColorIndex = 3;

                        bool processed = false;

                        // A. Polyline (LWPolyline) 객체
                        Polyline pline = cloneEnt as Polyline;
                        if (pline != null)
                        {
                            for (int j = 0; j < pline.NumberOfVertices; j++)
                            {
                                Point2d p2d = pline.GetPoint2dAt(j);
                                double rx = Math.Round(p2d.X, prec, MidpointRounding.ToEven);
                                double ry = Math.Round(p2d.Y, prec, MidpointRounding.ToEven);
                                pline.SetPointAt(j, new Point2d(rx, ry));
                            }
                            processed = true;
                        }
                        else
                        {
                            // B. Line 객체
                            Line line = cloneEnt as Line;
                            if (line != null)
                            {
                                Point3d p1 = line.StartPoint;
                                Point3d p2 = line.EndPoint;
                                line.StartPoint = new Point3d(
                                    Math.Round(p1.X, prec, MidpointRounding.ToEven),
                                    Math.Round(p1.Y, prec, MidpointRounding.ToEven),
                                    Math.Round(p1.Z, prec, MidpointRounding.ToEven)
                                );
                                line.EndPoint = new Point3d(
                                    Math.Round(p2.X, prec, MidpointRounding.ToEven),
                                    Math.Round(p2.Y, prec, MidpointRounding.ToEven),
                                    Math.Round(p2.Z, prec, MidpointRounding.ToEven)
                                );
                                processed = true;
                            }
                            else
                            {
                                // C. Circle 객체
                                Circle circle = cloneEnt as Circle;
                                if (circle != null)
                                {
                                    Point3d cLoc = circle.Center;
                                    circle.Center = new Point3d(
                                        Math.Round(cLoc.X, prec, MidpointRounding.ToEven),
                                        Math.Round(cLoc.Y, prec, MidpointRounding.ToEven),
                                        Math.Round(cLoc.Z, prec, MidpointRounding.ToEven)
                                    );
                                    processed = true;
                                }
                                else
                                {
                                    // D. Arc 객체
                                    Arc arc = cloneEnt as Arc;
                                    if (arc != null)
                                    {
                                        Point3d arcLoc = arc.Center;
                                        arc.Center = new Point3d(
                                            Math.Round(arcLoc.X, prec, MidpointRounding.ToEven),
                                            Math.Round(arcLoc.Y, prec, MidpointRounding.ToEven),
                                            Math.Round(arcLoc.Z, prec, MidpointRounding.ToEven)
                                        );
                                        processed = true;
                                    }
                                    else
                                    {
                                        // E. DBPoint 객체
                                        DBPoint dbPoint = cloneEnt as DBPoint;
                                        if (dbPoint != null)
                                        {
                                            Point3d ptLoc = dbPoint.Position;
                                            dbPoint.Position = new Point3d(
                                                Math.Round(ptLoc.X, prec, MidpointRounding.ToEven),
                                                Math.Round(ptLoc.Y, prec, MidpointRounding.ToEven),
                                                Math.Round(ptLoc.Z, prec, MidpointRounding.ToEven)
                                            );
                                            processed = true;
                                        }
                                        else
                                        {
                                            // F. DBText 객체
                                            DBText dbText = cloneEnt as DBText;
                                            if (dbText != null)
                                            {
                                                Point3d textPos = dbText.Position;
                                                dbText.Position = new Point3d(
                                                    Math.Round(textPos.X, prec, MidpointRounding.ToEven),
                                                    Math.Round(textPos.Y, prec, MidpointRounding.ToEven),
                                                    Math.Round(textPos.Z, prec, MidpointRounding.ToEven)
                                                );
                                                if (dbText.Justify != AttachmentPoint.BaseLeft)
                                                {
                                                    Point3d alignPos = dbText.AlignmentPoint;
                                                    dbText.AlignmentPoint = new Point3d(
                                                        Math.Round(alignPos.X, prec, MidpointRounding.ToEven),
                                                        Math.Round(alignPos.Y, prec, MidpointRounding.ToEven),
                                                        Math.Round(alignPos.Z, prec, MidpointRounding.ToEven)
                                                    );
                                                }
                                                processed = true;
                                            }
                                            else
                                            {
                                                // G. MText 객체
                                                MText mText = cloneEnt as MText;
                                                if (mText != null)
                                                {
                                                    Point3d mTextLoc = mText.Location;
                                                    mText.Location = new Point3d(
                                                        Math.Round(mTextLoc.X, prec, MidpointRounding.ToEven),
                                                        Math.Round(mTextLoc.Y, prec, MidpointRounding.ToEven),
                                                        Math.Round(mTextLoc.Z, prec, MidpointRounding.ToEven)
                                                    );
                                                    processed = true;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (processed)
                        {
                            btr.AppendEntity(cloneEnt);
                            tr.AddNewlyCreatedDBObject(cloneEnt, true);
                            convertedCount++;
                        }
                        else
                        {
                            cloneEnt.Dispose();
                        }
                    }

                    tr.Commit();
                }

                ed.WriteMessage(string.Format("\n[완료] 총 {0}개 객체의 좌표를 오사오입 조정(초록색 복제본 생성)하였습니다.\n", convertedCount));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] 좌표 정밀도 조정 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
        }
    }
}
