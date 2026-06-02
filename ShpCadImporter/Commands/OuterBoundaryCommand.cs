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
    /// 선택된 폴리곤/라인/아크 객체들의 외곽선(Outer Boundary)을 추출하는 AutoCAD 커맨드 클래스.
    /// 기존 LISP의 C:OUTERBOUND 매크로 기능을 완전한 프로그래밍 방식(Programmatic)으로 재현 및 개선하였습니다.
    /// </summary>
    public class OuterBoundaryCommand
    {
        // LISP 커맨드명과 일치하는 명칭 및 단축어 OB 등록
        [CommandMethod("OUTERBOUND")]
        [CommandMethod("OB")]
        public void ExtractOuterBoundary()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                // 1. 외곽선을 추출할 대상 객체 선택 필터 (LINE, LWPOLYLINE, POLYLINE, ARC)
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
                pso.MessageForAdding = "\n외곽선을 추출할 객체들(Line, Polyline, Arc)을 선택하세요: ";

                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 객체가 선택되지 않았습니다.\n");
                    return;
                }

                SelectionSet ss = psr.Value;
                if (ss == null || ss.Count == 0) return;

                ed.WriteMessage(string.Format("\n선택된 객체 수: {0}개. 외곽선 추출을 시작합니다...\n", ss.Count));

                // 2. 트랜잭션 시작
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // A. 선택된 커브들을 수집
                    DBObjectCollection curves = new DBObjectCollection();
                    foreach (SelectedObject so in ss)
                    {
                        if (so == null) continue;

                        Curve curve = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Curve;
                        if (curve != null)
                        {
                            curves.Add(curve);
                        }
                    }

                    if (curves.Count == 0)
                    {
                        ed.WriteMessage("\n[오류] 선택된 객체 중 유효한 커브가 없습니다.\n");
                        return;
                    }

                    // B. 커브들로부터 Region 생성
                    DBObjectCollection regions = null;
                    try
                    {
                        regions = Region.CreateFromCurves(curves);
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage(string.Format("\n[오류] 영역(Region) 생성 중 예외 발생: {0}\n", ex.Message));
                        return;
                    }

                    if (regions == null || regions.Count == 0)
                    {
                        ed.WriteMessage("\n[오류] 선택한 객체들로 영역(Region)을 형성할 수 없습니다. 서로 닫힌 루프를 이루는지 확인하십시오.\n");
                        return;
                    }

                    // C. 생성된 Region들을 하나로 병합(Union)
                    Region unionedRegion = regions[0] as Region;
                    List<Region> tempRegions = new List<Region>();

                    for (int i = 1; i < regions.Count; i++)
                    {
                        Region otherRegion = regions[i] as Region;
                        if (otherRegion != null)
                        {
                            try
                            {
                                unionedRegion.BooleanOperation(BooleanOperationType.BoolUnite, otherRegion);
                            }
                            catch (System.Exception ex)
                            {
                                ed.WriteMessage(string.Format("\n[경고] 영역 병합 중 일부 오류 무시: {0}\n", ex.Message));
                            }
                            finally
                            {
                                if (!otherRegion.IsDisposed)
                                {
                                    otherRegion.Dispose();
                                }
                            }
                        }
                    }

                    // D. 병합된 Region을 Explode하여 경계 커브 획득
                    DBObjectCollection explodedObjects = new DBObjectCollection();
                    try
                    {
                        unionedRegion.Explode(explodedObjects);
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage(string.Format("\n[오류] 영역 분해(Explode) 중 예외 발생: {0}\n", ex.Message));
                        unionedRegion.Dispose();
                        return;
                    }

                    // 병합 영역 해제
                    unionedRegion.Dispose();

                    // E. Explode된 커브 객체 추출 및 수집
                    List<Curve> explodedCurves = new List<Curve>();
                    foreach (DBObject obj in explodedObjects)
                    {
                        Curve c = obj as Curve;
                        if (c != null)
                        {
                            explodedCurves.Add(c);
                        }
                        else
                        {
                            obj.Dispose();
                        }
                    }

                    if (explodedCurves.Count == 0)
                    {
                        ed.WriteMessage("\n[오류] 분해된 경계 곡선이 존재하지 않습니다.\n");
                        return;
                    }

                    // F. 경계 커브들을 인접성 기준으로 정렬하여 루프(Loop)별로 그룹화
                    List<List<Curve>> loops = GroupCurvesIntoLoops(explodedCurves);

                    // G. 각 루프별로 Polyline을 빌드하고 최댓값 면적 검색
                    Polyline maxAreaPline = null;
                    double maxArea = -1.0;
                    List<Polyline> createdPlines = new List<Polyline>();

                    foreach (var loop in loops)
                    {
                        Polyline pline = CreatePolylineFromCurves(loop);
                        if (pline != null && pline.NumberOfVertices > 0)
                        {
                            createdPlines.Add(pline);
                            double area = 0.0;
                            try
                            {
                                area = pline.Area;
                            }
                            catch
                            {
                                area = 0.0;
                            }

                            if (area > maxArea)
                            {
                                maxArea = area;
                                maxAreaPline = pline;
                            }
                        }
                    }

                    // H. 최종 가장 큰 영역을 가지는 외곽 폴리라인을 도면에 추가
                    if (maxAreaPline != null)
                    {
                        // 현재 공간(ModelSpace 또는 Layout Space) 획득
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                        // 색상을 3번(Green)으로 설정 (LISP 기능 명세 동일)
                        maxAreaPline.ColorIndex = 3;

                        // 도면에 등록
                        btr.AppendEntity(maxAreaPline);
                        tr.AddNewlyCreatedDBObject(maxAreaPline, true);

                        ed.WriteMessage(string.Format("\n[완료] 성공적으로 가장 면적이 큰 외곽선을 추출하여 추가했습니다. (면적: {0:F3})\n", maxArea));
                    }
                    else
                    {
                        ed.WriteMessage("\n[오류] 외곽선 폴리라인 구성에 실패했습니다.\n");
                    }

                    // I. 메모리 자원 정리
                    // 1. Explode되었던 임시 Curve들 해제
                    foreach (var curve in explodedCurves)
                    {
                        if (!curve.IsDisposed)
                        {
                            curve.Dispose();
                        }
                    }

                    // 2. 최종 외곽선이 아닌 나머지 내부 루프 폴리라인들 해제
                    foreach (var pline in createdPlines)
                    {
                        if (pline != maxAreaPline && !pline.IsDisposed)
                        {
                            pline.Dispose();
                        }
                    }

                    // 변경사항 커밋
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] 외곽선 추출 작업 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
        }

        /// <summary>
        /// 곡선 컬렉션을 시작점/끝점 기하학적 인접도에 맞춰 정렬하여 독립된 루프 리스트로 재구성합니다.
        /// </summary>
        public static List<List<Curve>> GroupCurvesIntoLoops(List<Curve> curves, double tolerance = 1e-4)
        {
            List<List<Curve>> loops = new List<List<Curve>>();
            HashSet<Curve> unused = new HashSet<Curve>(curves);

            while (unused.Count > 0)
            {
                List<Curve> loop = new List<Curve>();

                Curve currentCurve = null;
                foreach (var c in unused)
                {
                    currentCurve = c;
                    break;
                }

                unused.Remove(currentCurve);
                loop.Add(currentCurve);

                Point3d loopStart = currentCurve.StartPoint;
                Point3d currentEnd = currentCurve.EndPoint;

                // 순방향 탐색
                bool progress = true;
                while (progress)
                {
                    progress = false;

                    if (currentEnd.DistanceTo(loopStart) < tolerance)
                    {
                        break;
                    }

                    Curve nextCurve = null;
                    bool reverseNext = false;

                    foreach (var c in unused)
                    {
                        if (c.StartPoint.DistanceTo(currentEnd) < tolerance)
                        {
                            nextCurve = c;
                            reverseNext = false;
                            break;
                        }
                        else if (c.EndPoint.DistanceTo(currentEnd) < tolerance)
                        {
                            nextCurve = c;
                            reverseNext = true;
                            break;
                        }
                    }

                    if (nextCurve != null)
                    {
                        unused.Remove(nextCurve);
                        loop.Add(nextCurve);
                        currentEnd = reverseNext ? nextCurve.StartPoint : nextCurve.EndPoint;
                        progress = true;
                    }
                }

                // 역방향 탐색 (열린 곡선 체인이거나 시작 지점을 잘라 시작한 경우를 대비)
                progress = true;
                while (progress)
                {
                    progress = false;

                    if (currentEnd.DistanceTo(loopStart) < tolerance)
                    {
                        break;
                    }

                    Curve nextCurve = null;
                    bool reverseNext = false;

                    foreach (var c in unused)
                    {
                        if (c.EndPoint.DistanceTo(loopStart) < tolerance)
                        {
                            nextCurve = c;
                            reverseNext = false;
                            break;
                        }
                        else if (c.StartPoint.DistanceTo(loopStart) < tolerance)
                        {
                            nextCurve = c;
                            reverseNext = true;
                            break;
                        }
                    }

                    if (nextCurve != null)
                    {
                        unused.Remove(nextCurve);
                        loop.Insert(0, nextCurve);
                        loopStart = reverseNext ? nextCurve.EndPoint : nextCurve.StartPoint;
                        progress = true;
                    }
                }

                loops.Add(loop);
            }

            return loops;
        }

        /// <summary>
        /// 정렬된 단일 루프의 커브들로부터 닫힌 LWPolyline을 재구성합니다. Arc의 Bulge도 정확히 계산하여 반영합니다.
        /// </summary>
        public static Polyline CreatePolylineFromCurves(List<Curve> loop, double tolerance = 1e-4)
        {
            Polyline pl = new Polyline();
            pl.SetDatabaseDefaults();

            if (loop == null || loop.Count == 0) return pl;

            // 원(Circle) 객체 1개 단독으로 루프가 형성된 경우의 특수 예외 처리
            if (loop.Count == 1 && loop[0] is Circle)
            {
                Circle circle = (Circle)loop[0];
                Point3d center = circle.Center;
                double radius = circle.Radius;
                pl.Elevation = center.Z;
                pl.AddVertexAt(0, new Point2d(center.X - radius, center.Y), 1.0, 0.0, 0.0);
                pl.AddVertexAt(1, new Point2d(center.X + radius, center.Y), 1.0, 0.0, 0.0);
                pl.Closed = true;
                return pl;
            }

            pl.Elevation = loop[0].StartPoint.Z;

            // 루프 전체를 순회하며 시작 위치 설정 및 각 정점의 2D 점과 아크 Bulge 계산
            Point3d currentPt;
            if (loop.Count > 1)
            {
                Curve first = loop[0];
                Curve second = loop[1];
                if (first.EndPoint.DistanceTo(second.StartPoint) < tolerance ||
                    first.EndPoint.DistanceTo(second.EndPoint) < tolerance)
                {
                    currentPt = first.StartPoint;
                }
                else
                {
                    currentPt = first.EndPoint;
                }
            }
            else
            {
                currentPt = loop[0].StartPoint;
            }

            for (int i = 0; i < loop.Count; i++)
            {
                Curve curve = loop[i];
                bool isReversed = false;

                if (curve.StartPoint.DistanceTo(currentPt) < tolerance)
                {
                    isReversed = false;
                    currentPt = curve.EndPoint;
                }
                else if (curve.EndPoint.DistanceTo(currentPt) < tolerance)
                {
                    isReversed = true;
                    currentPt = curve.StartPoint;
                }
                else
                {
                    isReversed = false;
                    currentPt = curve.EndPoint;
                }

                Point3d startPt = isReversed ? curve.EndPoint : curve.StartPoint;
                Point2d pt2d = new Point2d(startPt.X, startPt.Y);

                double bulge = 0.0;
                if (curve is Arc)
                {
                    Arc arc = (Arc)curve;
                    double diff = arc.EndAngle - arc.StartAngle;
                    if (diff < 0) diff += 2 * Math.PI;
                    bulge = Math.Tan(diff / 4.0);
                    if (isReversed)
                    {
                        bulge = -bulge;
                    }
                }

                pl.AddVertexAt(i, pt2d, bulge, 0.0, 0.0);
            }

            pl.Closed = true;
            return pl;
        }
    }
}
