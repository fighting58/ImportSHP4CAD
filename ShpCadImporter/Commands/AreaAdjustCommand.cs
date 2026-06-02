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
    /// 변 이동 또는 정점 고정 방식의 오프셋 연산을 수행하여 폴리라인의 면적을 정밀하게 가감 조정하는 AutoCAD 커맨드 클래스.
    /// 기존 LISP의 AREA_ADJUST 및 AREA_ADJUST_FIXED 매크로 기능을 최적의 피드백 반복 연산 기법으로 이식하였습니다.
    /// </summary>
    public class AreaAdjustCommand
    {
        // 변 이동 면적 조정 커맨드 및 단축어 AFO 등록
        [CommandMethod("AREA_ADJUST")]
        [CommandMethod("AFO")]
        public void AreaAdjustMove()
        {
            RunAreaAdjust("변 이동 면적 조정", isFixedMode: false);
        }

        // 정점 고정 면적 조정 커맨드 및 단축어 AFO2 등록
        [CommandMethod("AREA_ADJUST_FIXED")]
        [CommandMethod("AFO2")]
        public void AreaAdjustFixed()
        {
            RunAreaAdjust("정점 고정 면적 조정", isFixedMode: true);
        }

        /// <summary>
        /// 면적 조정 메인 쉘 공통 컨트롤러
        /// </summary>
        private void RunAreaAdjust(string modeLabel, bool isFixedMode)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                ed.WriteMessage(string.Format("\n[{0} 작업을 시작합니다]\n", modeLabel));

                // 1. 대상 폴리라인 선택 (LWPOLYLINE 만 수용)
                PromptEntityOptions peo = new PromptEntityOptions("\n대상 폴리라인을 선택하세요: ");
                peo.SetRejectMessage("\n선택한 객체가 폴리라인이 아닙니다.");
                peo.AddAllowedClass(typeof(Polyline), exactMatch: true);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 대상 객체가 선택되지 않았습니다.\n");
                    return;
                }

                ObjectId targetId = per.ObjectId;

                // 2. 대상 폴리라인 열기 및 정점 정보 수집
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline targetPline = tr.GetObject(targetId, OpenMode.ForRead) as Polyline;
                    if (targetPline == null)
                    {
                        ed.WriteMessage("\n[오류] 폴리라인을 열 수 없습니다.\n");
                        return;
                    }

                    List<Point2d> pts = new List<Point2d>();
                    for (int i = 0; i < targetPline.NumberOfVertices; i++)
                    {
                        pts.Add(targetPline.GetPoint2dAt(i));
                    }

                    if (pts.Count < 3)
                    {
                        ed.WriteMessage("\n[오류] 폴리라인의 정점 개수가 너무 부족합니다. (최소 3개 필요)\n");
                        return;
                    }

                    // 정점 Winding 방향을 항상 시계방향(CW)으로 정렬하여 내측/외측 오프셋 일치
                    pts = EnsureClockwise(pts);
                    double currentArea = GetSignedArea(pts);
                    ed.WriteMessage(string.Format("\n현재 면적: {0:F3}\n", currentArea));

                    // 3. 목표 면적 설정값 입력 프롬프트
                    PromptStringOptions pso = new PromptStringOptions("\n목표 면적 입력 (예: T150, D10, D-10, @120, #200, 150): ");
                    pso.AllowSpaces = false;
                    PromptResult pr = ed.GetString(pso);
                    if (pr.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\n[취소됨] 입력이 취소되었습니다.\n");
                        return;
                    }

                    string targetInput = pr.StringResult;
                    double? parsedTarget = ParseTarget(currentArea, targetInput);
                    if (!parsedTarget.HasValue)
                    {
                        ed.WriteMessage("\n[오류] 입력한 값이 올바른 면적 설정 포맷이 아닙니다.\n");
                        return;
                    }

                    double targetArea = parsedTarget.Value;
                    ed.WriteMessage(string.Format("최종 산출 목표 면적: {0:F3}\n", targetArea));

                    // 4. 조정 변 범위 지정을 위한 두 포인트 입력 요청 (P1, P2)
                    setOsmode(ed, 1); // Endpoint snap 강제 시도 (LISP setvar OSMODE 1 일치)
                    
                    PromptPointOptions ppo1 = new PromptPointOptions("\n조정할 변의 시작 위치(P1)를 선택하세요: ");
                    PromptPointResult ppr1 = ed.GetPoint(ppo1);
                    if (ppr1.Status != PromptStatus.OK) return;
                    Point3d p1 = ppr1.Value;

                    PromptPointOptions ppo2 = new PromptPointOptions("\n조정할 변의 끝 위치(P2)를 선택하세요: ");
                    PromptPointResult ppr2 = ed.GetPoint(ppo2);
                    if (ppr2.Status != PromptStatus.OK) return;
                    Point3d p2 = ppr2.Value;

                    resetOsmode(ed);

                    // 최인접 정점 인덱스 탐색
                    int idx1 = GetNearestVertexIndex(p1, pts);
                    int idx2 = GetNearestVertexIndex(p2, pts);

                    if (idx1 < 0 || idx2 < 0 || idx1 == idx2)
                    {
                        ed.WriteMessage("\n[오류] P1 또는 P2 정점의 인덱스가 올바르게 인식되지 않았거나 중복되었습니다.\n");
                        return;
                    }

                    ed.WriteMessage(string.Format("선택한 범위 인덱스: P1={0}, P2={1}\n", idx1, idx2));

                    // 5. 정점 고정(Fixed) 모드에서 인접 정점 선택 시 자동 중앙 정점 분할 연산 적용
                    if (isFixedMode && (idx1 + 1) % pts.Count == idx2)
                    {
                        Point2d pChord1 = pts[idx1];
                        Point2d pChord2 = pts[idx2];
                        Point2d midPt = new Point2d(0.5 * (pChord1.X + pChord2.X), 0.5 * (pChord1.Y + pChord2.Y));
                        
                        List<Point2d> tempPts = new List<Point2d>(pts);
                        if (idx1 < idx2)
                        {
                            tempPts.Insert(idx2, midPt);
                            idx2 = (idx2 + 1) % tempPts.Count;
                        }
                        else
                        {
                            tempPts.Insert(0, midPt);
                            idx1 = idx1 + 1;
                            idx2 = 1;
                        }
                        pts = tempPts;
                        ed.WriteMessage("\n[안내] 인접 정점이 선택되어 변 중앙에 고정 축 변경점(정점)을 자동 신설했습니다.\n");
                    }

                    // 6. 면적 가감 수렴 엔진 구동
                    double totalOffset = 0.0;
                    List<Point2d> newPts = null;
                    
                    if (isFixedMode)
                    {
                        newPts = AdjustAreaFixed(pts, idx1, idx2, targetArea, out totalOffset);
                    }
                    else
                    {
                        newPts = AdjustAreaMove(pts, idx1, idx2, targetArea, out totalOffset);
                    }

                    if (newPts == null)
                    {
                        ed.WriteMessage("\n[오류] 수렴 연산에 실패했습니다. 변 범위(P1/P2) 설정을 다시 조정하십시오.\n");
                        return;
                    }

                    // 변경사항 유무 검증
                    bool same = true;
                    if (newPts.Count != pts.Count) same = false;
                    else
                    {
                        for (int j = 0; j < newPts.Count; j++)
                        {
                            if (newPts[j].GetDistanceTo(pts[j]) > 1e-4)
                            {
                                same = false;
                                break;
                            }
                        }
                    }

                    if (same)
                    {
                        ed.WriteMessage("\n[안내] 변경 전후의 기하 선형 및 면적이 이미 동일하여 면적 조정을 건너뜁니다.\n");
                    }
                    else
                    {
                        // 7. [비파괴 기법] 원본을 유지하고 신규 녹색(색상 3) 결과물 생성 삽입
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                        Polyline newPline = (Polyline)targetPline.Clone();
                        
                        // 정점 수 업데이트
                        int originalCount = newPline.NumberOfVertices;
                        for (int j = originalCount - 1; j >= 1; j--)
                        {
                            newPline.RemoveVertexAt(j);
                        }
                        for (int j = 0; j < newPts.Count; j++)
                        {
                            newPline.AddVertexAt(j + 1, newPts[j], 0.0, 0.0, 0.0);
                        }
                        newPline.RemoveVertexAt(0);

                        // 색상을 3번(Green)으로 지정
                        newPline.ColorIndex = 3;

                        // 도면 기록
                        btr.AppendEntity(newPline);
                        tr.AddNewlyCreatedDBObject(newPline, true);

                        double finalArea = GetSignedArea(newPts);
                        if (Math.Abs(targetArea - finalArea) <= 0.01)
                        {
                            ed.WriteMessage(string.Format("\n[완료] 성공적으로 조정을 마쳤습니다.\n최종 면적: {0:F3} (오차 내 수렴), 전체 오프셋 이동값: {1:F5}\n", finalArea, totalOffset));
                        }
                        else
                        {
                            ed.WriteMessage(string.Format("\n[중단] 30회 수렴 루프 한계 초과.\n근사 면적: {0:F3} (오차: {1:F5}), 전체 오프셋 이동값: {2:F5}\n", finalArea, Math.Abs(targetArea - finalArea), totalOffset));
                        }
                    }

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] 면적 조정 작업 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
        }

        /// <summary>
        /// 변 이동 오프셋 수렴 연산 엔진
        /// </summary>
        private static List<Point2d> AdjustAreaMove(List<Point2d> ptsList, int idx1, int idx2, double targetArea, out double totalOffset)
        {
            int n = ptsList.Count;
            int iter = 0;
            double currentArea = GetSignedArea(ptsList);
            totalOffset = 0.0;
            bool changed = false;

            List<Point2d> currentPts = new List<Point2d>(ptsList);

            while (iter < 30 && Math.Abs(targetArea - currentArea) > 0.01)
            {
                // 1. 조정 범위 내 전체 선분 누적 길이 계산
                double segLen = 0.0;
                int i = idx1;
                bool continueTracing = true;
                while (continueTracing)
                {
                    Point2d p1 = currentPts[i];
                    Point2d p2 = currentPts[(i + 1) % n];
                    segLen += p1.GetDistanceTo(p2);
                    if ((i + 1) % n == idx2) continueTracing = false;
                    else i = (i + 1) % n;
                }

                // 2. 면적 차이로부터 피드백 오프셋 계산
                double offsetDist = (targetArea - currentArea) / (segLen * -1.0);
                totalOffset -= offsetDist;

                int idxPrev = (idx1 == 0) ? (n - 1) : (idx1 - 1);
                int idxNext = (idx2 + 1) % n;
                Point2d pPrev = currentPts[idxPrev];
                Point2d pNext = currentPts[idxNext];

                // 3. 내측 법선 방향으로 수직 오프셋 라인(Offset Lines)들 구축
                List<Point2d[]> offsetLines = new List<Point2d[]>();
                i = idx1;
                continueTracing = true;
                while (continueTracing)
                {
                    Point2d p1 = currentPts[i];
                    Point2d p2 = currentPts[(i + 1) % n];
                    double dVal = Math.Max(1e-9, p1.GetDistanceTo(p2));

                    double dx = p2.X - p1.X;
                    double dy = p2.Y - p1.Y;
                    Point2d vInward = new Point2d(dy / dVal, -dx / dVal);

                    Point2d p1Off = new Point2d(p1.X + offsetDist * vInward.X, p1.Y + offsetDist * vInward.Y);
                    Point2d p2Off = new Point2d(p2.X + offsetDist * vInward.X, p2.Y + offsetDist * vInward.Y);

                    offsetLines.Add(new Point2d[] { p1Off, p2Off });

                    if ((i + 1) % n == idx2) continueTracing = false;
                    else i = (i + 1) % n;
                }

                // 4. 인접선 및 상호 오프셋선들의 무한 교차 분석 수행
                List<Point2d> finalSeg = new List<Point2d>();

                // 시작부 교차 (정지한 직전 경계선과 첫 번째 이동선)
                Point2d[] linePrev = new Point2d[] { pPrev, currentPts[idx1] };
                Point2d[] lineFirst = offsetLines[0];
                Point2d? pNew = IntersectLines(linePrev[0], linePrev[1], lineFirst[0], lineFirst[1]);
                finalSeg.Add(pNew ?? lineFirst[0]);

                // 중간 노드 교차
                for (int j = 0; j < offsetLines.Count - 1; j++)
                {
                    Point2d[] L1 = offsetLines[j];
                    Point2d[] L2 = offsetLines[j + 1];
                    pNew = IntersectLines(L1[0], L1[1], L2[0], L2[1]);
                    finalSeg.Add(pNew ?? L1[1]);
                }

                // 끝부 교차 (마지막 이동선과 정지한 직후 경계선)
                Point2d[] lineNext = new Point2d[] { currentPts[idx2], pNext };
                Point2d[] lineLast = offsetLines[offsetLines.Count - 1];
                pNew = IntersectLines(lineLast[0], lineLast[1], lineNext[0], lineNext[1]);
                finalSeg.Add(pNew ?? lineLast[1]);

                // 5. 정점 리스트 정렬 조립하여 갱신
                List<Point2d> newPtsList = new List<Point2d>();
                if (idx1 <= idx2)
                {
                    for (int j = 0; j < idx1; j++) newPtsList.Add(currentPts[j]);
                    newPtsList.AddRange(finalSeg);
                    for (int j = idx2 + 1; j < n; j++) newPtsList.Add(currentPts[j]);
                }
                else
                {
                    int cntA = n - idx1;
                    List<Point2d> segA = finalSeg.GetRange(0, cntA);
                    List<Point2d> segB = finalSeg.GetRange(cntA, finalSeg.Count - cntA);

                    newPtsList.AddRange(segB);
                    for (int j = idx2 + 1; j < idx1; j++) newPtsList.Add(currentPts[j]);
                    newPtsList.AddRange(segA);
                }

                currentPts = newPtsList;
                currentArea = GetSignedArea(currentPts);
                changed = true;
                iter++;
            }

            return changed ? currentPts : null;
        }

        /// <summary>
        /// 정점 고정 오프셋 수렴 연산 엔진
        /// </summary>
        private static List<Point2d> AdjustAreaFixed(List<Point2d> ptsList, int idx1, int idx2, double targetArea, out double totalOffset)
        {
            int n = ptsList.Count;
            int iter = 0;
            double currentArea = GetSignedArea(ptsList);
            totalOffset = 0.0;
            double geomOffset = 0.0;
            bool changed = false;

            List<Point2d> basePtsList = new List<Point2d>(ptsList);
            List<Point2d> currentPts = new List<Point2d>(ptsList);

            while (iter < 30 && Math.Abs(targetArea - currentArea) > 0.01)
            {
                // 1. 고정 경계 내의 변형 정점(sub-pts) 세트 추출
                List<Point2d> subPts = new List<Point2d>();
                int i = idx1;
                bool continueTracing = true;
                while (continueTracing)
                {
                    subPts.Add(basePtsList[i]);
                    if (i == idx2) continueTracing = false;
                    else i = (i + 1) % n;
                }

                // 2. 가상의 변형 선분들 길이 계산
                double subLen = 0.0;
                i = idx1;
                continueTracing = true;
                while (continueTracing)
                {
                    Point2d p1 = basePtsList[i];
                    Point2d p2 = basePtsList[(i + 1) % n];
                    subLen += p1.GetDistanceTo(p2);
                    if ((i + 1) % n == idx2) continueTracing = false;
                    else i = (i + 1) % n;
                }

                // 3. 누적 정량 오프셋 가감
                double offsetDist = (targetArea - currentArea) / (subLen * -1.0);
                geomOffset += offsetDist;
                totalOffset = -geomOffset;

                // 4. 고정 정점들을 기점으로 하는 오프셋 수직 성분 선분군 생성
                List<Point2d[]> offsetLines = new List<Point2d[]>();
                for (int j = 0; j < subPts.Count - 1; j++)
                {
                    Point2d p1 = subPts[j];
                    Point2d p2 = subPts[j + 1];
                    double dVal = Math.Max(1e-9, p1.GetDistanceTo(p2));

                    double dx = p2.X - p1.X;
                    double dy = p2.Y - p1.Y;
                    Point2d vInward = new Point2d(dy / dVal, -dx / dVal);

                    Point2d p1Off = new Point2d(p1.X + geomOffset * vInward.X, p1.Y + geomOffset * vInward.Y);
                    Point2d p2Off = new Point2d(p2.X + geomOffset * vInward.X, p2.Y + geomOffset * vInward.Y);

                    offsetLines.Add(new Point2d[] { p1Off, p2Off });
                }

                // 5. 오프셋 선분들의 순수 내부 교차 정점(finalSeg) 추출
                List<Point2d> finalSeg = new List<Point2d>();
                for (int j = 0; j < offsetLines.Count - 1; j++)
                {
                    Point2d[] L1 = offsetLines[j];
                    Point2d[] L2 = offsetLines[j + 1];
                    Point2d? pNew = IntersectLines(L1[0], L1[1], L2[0], L2[1]);
                    finalSeg.Add(pNew ?? L1[1]);
                }

                // 6. 시작점/끝점(idx1, idx2)은 철저히 basePtsList 좌표로 보호하여 고정
                List<Point2d> newPtsList = new List<Point2d>();
                if (idx1 <= idx2)
                {
                    for (int j = 0; j <= idx1; j++) newPtsList.Add(basePtsList[j]);
                    newPtsList.AddRange(finalSeg);
                    for (int j = idx2; j < n; j++) newPtsList.Add(basePtsList[j]);
                }
                else
                {
                    int cntA = n - idx1 - 1;
                    List<Point2d> segA = new List<Point2d>();
                    for (int j = 0; j < cntA && j < finalSeg.Count; j++) segA.Add(finalSeg[j]);

                    List<Point2d> segB = new List<Point2d>();
                    for (int j = cntA; j < finalSeg.Count; j++) segB.Add(finalSeg[j]);

                    newPtsList.AddRange(segB);
                    for (int j = idx2; j <= idx1; j++) newPtsList.Add(basePtsList[j]);
                    newPtsList.AddRange(segA);
                }

                currentPts = newPtsList;
                currentArea = GetSignedArea(currentPts);
                changed = true;
                iter++;
            }

            return changed ? currentPts : null;
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
        /// 한국 지적 면적 오차 허용범위 공식에 따른 허용 오차 산정
        /// F = 0.026 * 0.026 * Scale * Sqrt(S)
        /// </summary>
        private static double CalcTolerance(double s, double scale)
        {
            double f = 0.026 * 0.026 * scale * Math.Sqrt(s);
            return Math.Round(f, 3);
        }

        /// <summary>
        /// 입력 면적 포맷을 정합성 있게 파싱
        /// </summary>
        private static double? ParseTarget(double currentArea, string input)
        {
            string trimmed = input.Trim().ToUpper();
            if (string.IsNullOrEmpty(trimmed)) return null;

            string prefix = trimmed.Substring(0, 1);
            double? regArea = null;
            double? scale = null;
            double? target = null;

            if (prefix == "T")
            {
                double val;
                if (double.TryParse(trimmed.Substring(1), out val))
                {
                    target = val;
                }
            }
            else if (prefix == "D")
            {
                double val;
                if (double.TryParse(trimmed.Substring(1), out val))
                {
                    target = currentArea + val;
                }
            }
            else if (prefix == "@")
            {
                double val;
                if (double.TryParse(trimmed.Substring(1), out val))
                {
                    regArea = val;
                    scale = 6000.0;
                }
            }
            else if (prefix == "#")
            {
                double val;
                if (double.TryParse(trimmed.Substring(1), out val))
                {
                    regArea = val;
                    scale = 1000.0;
                }
            }
            else
            {
                double val;
                if (double.TryParse(trimmed, out val))
                {
                    regArea = val;
                    scale = 1200.0;
                }
            }

            if (regArea.HasValue && scale.HasValue && !target.HasValue)
            {
                double tolArea = CalcTolerance(regArea.Value, scale.Value);
                if (currentArea >= regArea.Value)
                {
                    target = regArea.Value + tolArea - 1.0;
                }
                else
                {
                    target = regArea.Value - tolArea + 1.0;
                }
            }

            return target;
        }

        /// <summary>
        /// 두 무한 연장 직선의 교차 연산 검출
        /// </summary>
        public static Point2d? IntersectLines(Point2d p1, Point2d p2, Point2d p3, Point2d p4)
        {
            double den = (p4.Y - p3.Y) * (p2.X - p1.X) - (p4.X - p3.X) * (p2.Y - p1.Y);
            if (Math.Abs(den) < 1e-9) return null; // Parallel

            double ua = ((p4.X - p3.X) * (p1.Y - p3.Y) - (p4.Y - p3.Y) * (p1.X - p3.X)) / den;
            double x = p1.X + ua * (p2.X - p1.X);
            double y = p1.Y + ua * (p2.Y - p1.Y);
            return new Point2d(x, y);
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

        private static void setOsmode(Editor ed, int val)
        {
            try { Application.SetSystemVariable("OSMODE", val); } catch {}
        }

        private static void resetOsmode(Editor ed)
        {
            try { Application.SetSystemVariable("OSMODE", 16383); } catch {} // Restore standard snap state
        }
    }
}
