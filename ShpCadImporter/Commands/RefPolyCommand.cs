using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ShpCadImporter.Commands
{
    /// <summary>
    /// 대상 폴리라인(Polyline)의 일부 구간을 참조 폴리라인(Reference Polyline)의 세그먼트로 치환하여 
    /// 선형을 정밀하게 수정하는 AutoCAD 커맨드 클래스.
    /// 기존 LISP의 REFPOLY_CW 및 REFPOLY_CCW 기능을 메모리 내 최적화 연산 방식으로 완벽하게 구현하였습니다.
    /// </summary>
    public class RefPolyCommand
    {
        // 시계방향(CW) 모드 커맨드 및 단축어 F2P, REFPOLY_CW 등록
        [CommandMethod("F2P")]
        [CommandMethod("REFPOLY_CW")]
        public void RefPolyClockwise()
        {
            RunRefPolyEngine("시계방향(CW)", isCCWMode: false);
        }

        // 반시계방향(CCW) 모드 커맨드 및 단축어 F3P, REFPOLY_CCW 등록
        [CommandMethod("F3P")]
        [CommandMethod("REFPOLY_CCW")]
        public void RefPolyCounterClockwise()
        {
            RunRefPolyEngine("반시계방향(CCW)", isCCWMode: true);
        }

        /// <summary>
        /// 선형 치환 핵심 엔진 메서드
        /// </summary>
        private void RunRefPolyEngine(string modeLabel, bool isCCWMode)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            ObjectId refPlineId = ObjectId.Null;
            List<Curve> explodedCurves = new List<Curve>();
            List<Polyline> createdPlines = new List<Polyline>();
            Polyline refPline = null;
            object oldOsMode = null;

            try
            {
                ed.WriteMessage(string.Format("\n[{0} 선형 수정 작업을 시작합니다]\n", modeLabel));

                // 1. 참조 대상 객체들 선택 필터 구성 및 입력 요청 (참조 객체 먼저 선택)
                TypedValue[] tv = new TypedValue[]
                {
                    new TypedValue((int)DxfCode.Operator, "<or"),
                    new TypedValue((int)DxfCode.Start, "LINE"),
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                    new TypedValue((int)DxfCode.Start, "POLYLINE"),
                    new TypedValue((int)DxfCode.Start, "ARC"),
                    new TypedValue((int)DxfCode.Operator, "or>")
                };
                SelectionFilter filter = new SelectionFilter(tv);

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\n선형을 참조할 객체들(Line, Polyline, Arc)을 드래그 선택하세요: ";

                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 참조 객체들이 선택되지 않았습니다.\n");
                    return;
                }

                SelectionSet ssRef = psr.Value;
                if (ssRef == null || ssRef.Count == 0) return;

                // 2. [선 연산 및 초록 참조선 도면 삽입] ➔ 이 시점에 즉시 참조선 렌더링!
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // A. 참조 객체 수집
                    DBObjectCollection refCurves = new DBObjectCollection();
                    foreach (SelectedObject so in ssRef)
                    {
                        if (so == null) continue;
                        Curve curve = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Curve;
                        if (curve != null) refCurves.Add(curve);
                    }

                    if (refCurves.Count == 0)
                    {
                        ed.WriteMessage("\n[오류] 선택된 참조 객체 중 유효한 커브가 없습니다.\n");
                        return;
                    }

                    // B. 영역 생성 및 병합
                    DBObjectCollection regions = null;
                    try { regions = Region.CreateFromCurves(refCurves); }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage(string.Format("\n[오류] 참조 영역 생성 중 예외 발생: {0}\n", ex.Message));
                        return;
                    }

                    if (regions == null || regions.Count == 0)
                    {
                        ed.WriteMessage("\n[오류] 참조 객체들로 영역을 형성할 수 없습니다. 닫힌 루프 형태인지 확인하세요.\n");
                        return;
                    }

                    Region unionedRegion = regions[0] as Region;
                    for (int i = 1; i < regions.Count; i++)
                    {
                        Region otherRegion = regions[i] as Region;
                        if (otherRegion != null)
                        {
                            try { unionedRegion.BooleanOperation(BooleanOperationType.BoolUnite, otherRegion); }
                            catch {}
                            finally { if (!otherRegion.IsDisposed) otherRegion.Dispose(); }
                        }
                    }

                    // C. 영역 분해 및 외곽선 추출
                    DBObjectCollection explodedObjects = new DBObjectCollection();
                    try { unionedRegion.Explode(explodedObjects); }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage(string.Format("\n[오류] 참조 영역 분해 중 예외 발생: {0}\n", ex.Message));
                        unionedRegion.Dispose();
                        return;
                    }
                    unionedRegion.Dispose();

                    foreach (DBObject obj in explodedObjects)
                    {
                        Curve c = obj as Curve;
                        if (c != null) explodedCurves.Add(c);
                        else obj.Dispose();
                    }

                    if (explodedCurves.Count == 0)
                    {
                        ed.WriteMessage("\n[오류] 분해된 참조 경계 곡선이 없습니다.\n");
                        return;
                    }

                    // D. 최외곽 폴리라인 합성
                    List<List<Curve>> loops = OuterBoundaryCommand.GroupCurvesIntoLoops(explodedCurves);
                    double maxArea = -1.0;

                    foreach (var loop in loops)
                    {
                        Polyline pline = OuterBoundaryCommand.CreatePolylineFromCurves(loop);
                        if (pline != null && pline.NumberOfVertices > 0)
                        {
                            createdPlines.Add(pline);
                            double area = 0.0;
                            try { area = pline.Area; } catch { area = 0.0; }

                            if (area > maxArea)
                            {
                                maxArea = area;
                                refPline = pline;
                            }
                        }
                    }

                    if (refPline == null)
                    {
                        ed.WriteMessage("\n[오류] 참조 외곽선 추출에 실패하였습니다.\n");
                        return;
                    }

                    // E. 참조 외곽선 폴리라인을 초록색(색상 3)으로 도면에 실제로 신설 삽입
                    refPline.ColorIndex = 3;
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    btr.AppendEntity(refPline);
                    tr.AddNewlyCreatedDBObject(refPline, true);

                    refPlineId = refPline.ObjectId; // 가이드용 임시 참조선 ID 획득

                    // F. 트랜잭션 확정하여 도면에 삽입
                    tr.Commit();
                }

                // 💡 참조선 도면 삽입 후 화면 리프레시 ➔ 이제 초록색 참조선이 즉시 도면에 보입니다!
                doc.TransactionManager.QueueForGraphicsFlush();
                ed.UpdateScreen();

                // 3. 대상 폴리라인 선택 (초록 가이드선을 보면서 선택할 수 있음!)
                PromptEntityOptions peo = new PromptEntityOptions("\n수정할 대상 폴리라인을 선택하세요: ");
                peo.SetRejectMessage("\n선택한 객체가 폴리라인이 아닙니다.");
                peo.AddAllowedClass(typeof(Polyline), exactMatch: true);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 대상 객체가 선택되지 않았습니다.\n");
                    return;
                }

                ObjectId targetId = per.ObjectId;

                // 4. [치환 연산 트랜잭션 시작]
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // A. 대상 폴리라인 열기 및 검증
                    Polyline targetPline = tr.GetObject(targetId, OpenMode.ForRead) as Polyline;
                    if (targetPline == null)
                    {
                        ed.WriteMessage("\n[오류] 대상 폴리라인을 읽기 모드로 열 수 없습니다.\n");
                        return;
                    }

                    if (targetPline.NumberOfVertices < 2)
                    {
                        ed.WriteMessage("\n[오류] 대상 폴리라인의 정점 개수가 너무 적어 선형을 수정할 수 없습니다.\n");
                        return;
                    }

                    // B. 대상/참조 폴리라인으로부터 2D 정점 추출
                    List<Point2d> targetPoints = new List<Point2d>();
                    for (int i = 0; i < targetPline.NumberOfVertices; i++)
                    {
                        targetPoints.Add(targetPline.GetPoint2dAt(i));
                    }

                    List<Point2d> refPoints = new List<Point2d>();
                    // 💡 DB에 등록된 refPline을 ForRead 모드로 다시 열어서 정점 추출
                    Polyline dbRefPline = tr.GetObject(refPlineId, OpenMode.ForRead) as Polyline;
                    if (dbRefPline == null)
                    {
                        ed.WriteMessage("\n[오류] 생성된 참조선을 다시 열 수 없습니다.\n");
                        return;
                    }
                    for (int i = 0; i < dbRefPline.NumberOfVertices; i++)
                    {
                        refPoints.Add(dbRefPline.GetPoint2dAt(i));
                    }

                    // C. CW/CCW 모드에 따라 정점 리스트 Winding 방향 정규화 수행
                    if (isCCWMode)
                    {
                        targetPoints = EnsureClockwise(targetPoints);
                        refPoints = EnsureCounterClockwise(refPoints);
                    }
                    else
                    {
                        targetPoints = EnsureClockwise(targetPoints);
                        refPoints = EnsureClockwise(refPoints);
                    }

                    // D. 네 점 클릭 입력 요청 (P1, Q1, P2, Q2)
                    try
                    {
                        oldOsMode = Application.GetSystemVariable("OSMODE");
                        Application.SetSystemVariable("OSMODE", 1); // 끝점 스냅
                    }
                    catch {}

                    PromptPointOptions ppo1 = new PromptPointOptions("\n대상 폴리라인 변경 시작점(P1)을 선택하세요: ");
                    PromptPointResult ppr1 = ed.GetPoint(ppo1);
                    if (ppr1.Status != PromptStatus.OK) return;
                    Point3d p1 = ppr1.Value;

                    PromptPointOptions ppo2 = new PromptPointOptions("\n참조 폴리라인 변경 시작점(Q1)을 선택하세요: ");
                    PromptPointResult ppr2 = ed.GetPoint(ppo2);
                    if (ppr2.Status != PromptStatus.OK) return;
                    Point3d q1 = ppr2.Value;

                    PromptPointOptions ppo3 = new PromptPointOptions("\n대상 폴리라인 변경 끝점(P2)을 선택하세요: ");
                    PromptPointResult ppr3 = ed.GetPoint(ppo3);
                    if (ppr3.Status != PromptStatus.OK) return;
                    Point3d p2 = ppr3.Value;

                    PromptPointOptions ppo4 = new PromptPointOptions("\n참조 폴리라인 변경 끝점(Q2)을 선택하세요: ");
                    PromptPointResult ppr4 = ed.GetPoint(ppo4);
                    if (ppr4.Status != PromptStatus.OK) return;
                    Point3d q2 = ppr4.Value;

                    // E. 최인접 정점 인덱스 탐색
                    int idxP1 = GetNearestVertexIndex(p1, targetPoints);
                    int idxQ1 = GetNearestVertexIndex(q1, refPoints);
                    int idxP2 = GetNearestVertexIndex(p2, targetPoints);
                    int idxQ2 = GetNearestVertexIndex(q2, refPoints);

                    ed.WriteMessage(string.Format("\n인덱스 매핑: P1={0}, P2={1}, Q1={2}, Q2={3}\n", idxP1, idxP2, idxQ1, idxQ2));

                    if (idxP1 == idxP2 || idxQ1 == idxQ2)
                    {
                        ed.WriteMessage("\n[오류] 선택한 정점이 일치하거나 중복되어 치환할 수 없습니다.\n");
                        return;
                    }

                    // F. 참조선 세그먼트 segQ 추출 (Q1에서 Q2까지 순환 누적)
                    List<Point2d> segQ = new List<Point2d>();
                    int idx = idxQ1;
                    while (idx != idxQ2)
                    {
                        segQ.Add(refPoints[idx]);
                        idx = (idx + 1) % refPoints.Count;
                    }
                    segQ.Add(refPoints[idxQ2]);

                    // G. 대상 정점들을 치환하여 새로운 정점 조합 생성
                    List<Point2d> newPoints = new List<Point2d>();
                    if (idxP1 <= idxP2)
                    {
                        for (int j = 0; j < idxP1; j++) newPoints.Add(targetPoints[j]);
                        newPoints.AddRange(segQ);
                        for (int j = idxP2 + 1; j < targetPoints.Count; j++) newPoints.Add(targetPoints[j]);
                    }
                    else
                    {
                        newPoints.AddRange(segQ);
                        for (int j = idxP2 + 1; j < idxP1; j++) newPoints.Add(targetPoints[j]);
                    }

                    // H. 변경 여부 검증 및 대상선 선형 수정 적용
                    bool isIdentical = true;
                    if (newPoints.Count != targetPoints.Count)
                    {
                        isIdentical = false;
                    }
                    else
                    {
                        for (int j = 0; j < newPoints.Count; j++)
                        {
                            if (newPoints[j].GetDistanceTo(targetPoints[j]) > 1e-4)
                            {
                                isIdentical = false;
                                break;
                            }
                        }
                    }

                    if (isIdentical)
                    {
                        ed.WriteMessage("\n[안내] 변경 전후의 선형이 완전히 일치합니다. 선형 대체를 수행하지 않습니다.\n");
                    }
                    else
                    {
                        // 대상 폴리라인 쓰기 모드로 변경 후 정점 전체 치환
                        Polyline targetPlineWrite = tr.GetObject(targetId, OpenMode.ForWrite) as Polyline;
                        if (targetPlineWrite != null)
                        {
                            int originalCount = targetPlineWrite.NumberOfVertices;
                            for (int j = originalCount - 1; j >= 1; j--)
                            {
                                targetPlineWrite.RemoveVertexAt(j);
                            }
                            for (int j = 0; j < newPoints.Count; j++)
                            {
                                targetPlineWrite.AddVertexAt(j + 1, newPoints[j], 0.0, 0.0, 0.0);
                            }
                            targetPlineWrite.RemoveVertexAt(0);

                            ed.WriteMessage("\n[성공] 대상 폴리라인 선형이 성공적으로 수정/치환되었습니다.\n");
                        }
                    }

                    // 트랜잭션 확정
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] 선형 수정 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
            finally
            {
                // OSNAP 원복
                if (oldOsMode != null)
                {
                    try { Application.SetSystemVariable("OSMODE", oldOsMode); }
                    catch {}
                }

                // M. 임시 기하 생성물 메모리 자원 완전 해제
                foreach (var c in explodedCurves) { if (!c.IsDisposed) c.Dispose(); }
                foreach (var p in createdPlines)
                {
                    if (p != refPline)
                    {
                        if (!p.IsDisposed) p.Dispose();
                    }
                }

                // 연산이 성공적으로 끝나거나 도중에 취소(불완전 종료)된 경우 임시 참조선 제거
                if (refPlineId != ObjectId.Null && !refPlineId.IsErased)
                {
                    try
                    {
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            Entity ent = tr.GetObject(refPlineId, OpenMode.ForWrite) as Entity;
                            if (ent != null)
                            {
                                ent.Erase();
                            }
                            tr.Commit();
                        }
                    }
                    catch
                    {
                        // 실패 시 무시
                    }
                }
            }
        }

        /// <summary>
        /// 2D 다각형 정점들의 Winding 방향 판단용 signed area 계산 (Shoelace formula)
        /// </summary>
        private static double GetSignedArea(List<Point2d> pts)
        {
            double area = 0.0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                Point2d p1 = pts[i];
                Point2d p2 = pts[(i + 1) % n];
                area += (p2.X - p1.X) * (p2.Y + p1.Y);
            }
            return 0.5 * area;
        }

        /// <summary>
        /// 다각형이 시계방향(CW) 정렬이 되도록 강제합니다.
        /// </summary>
        private static List<Point2d> EnsureClockwise(List<Point2d> pts)
        {
            double area = GetSignedArea(pts);
            if (area < 0) // 음수 = CCW 상태
            {
                List<Point2d> rev = new List<Point2d>(pts);
                rev.Reverse();
                return rev;
            }
            return pts;
        }

        /// <summary>
        /// 다각형이 반시계방향(CCW) 정렬이 되도록 강제합니다.
        /// </summary>
        private static List<Point2d> EnsureCounterClockwise(List<Point2d> pts)
        {
            double area = GetSignedArea(pts);
            if (area > 0) // 양수 = CW 상태
            {
                List<Point2d> rev = new List<Point2d>(pts);
                rev.Reverse();
                return rev;
            }
            return pts;
        }

        /// <summary>
        /// 선택한 포인트 위치와 가장 인접한 정점의 인덱스를 반환합니다.
        /// </summary>
        private static int GetNearestVertexIndex(Point3d pickPt, List<Point2d> vertices)
        {
            int nearestIndex = -1;
            double minDistance = double.MaxValue;
            for (int i = 0; i < vertices.Count; i++)
            {
                Point2d v = vertices[i];
                double dist = pickPt.DistanceTo(new Point3d(v.X, v.Y, pickPt.Z));
                if (dist < minDistance)
                {
                    minDistance = dist;
                    nearestIndex = i;
                }
            }
            return nearestIndex;
        }
    }
}
