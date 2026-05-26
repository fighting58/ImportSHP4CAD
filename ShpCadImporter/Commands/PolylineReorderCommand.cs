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
    /// 폴리라인의 정점 인덱스 순서를 사용자의 선택에 맞춰 변경하는 AutoCAD 커맨드 클래스.
    /// 열린 폴리라인인 경우 전체 역순 배치하며, 닫힌 폴리라인인 경우 입력받은 인접한 두 점을 기준으로 회전/역순 재배치합니다.
    /// </summary>
    public class PolylineReorderCommand
    {
        // AutoCAD 예약 단축어와 충돌하지 않는 4글자 단축 명령어 지정:
        // IDXO (Index Order)
        [CommandMethod("IDXO")]
        public void ReorderPolylineVertices()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. 폴리라인 입력 요청
            PromptEntityOptions peo = new PromptEntityOptions("\n순서를 변경할 폴리라인을 선택하세요: ");
            peo.SetRejectMessage("\n선택한 객체가 폴리라인이 아닙니다.");
            peo.AddAllowedClass(typeof(Polyline), exactMatch: true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[취소됨] 객체가 선택되지 않았습니다.\n");
                return;
            }

            ObjectId plineId = per.ObjectId;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline pline = tr.GetObject(plineId, OpenMode.ForWrite) as Polyline;
                if (pline == null)
                {
                    ed.WriteMessage("\n[오류] 폴리라인을 열 수 없습니다.\n");
                    return;
                }

                int N = pline.NumberOfVertices;
                if (N < 2)
                {
                    ed.WriteMessage("\n[오류] 정점 개수가 2개 이상이어야 재배치가 가능합니다.\n");
                    return;
                }

                bool isClosed = pline.Closed;

                // 기존 정점 속성(좌표, 벌지, 시작/끝 너비) 임시 저장
                List<Point2d> coords = new List<Point2d>();
                List<double> bulges = new List<double>();
                List<double> startWidths = new List<double>();
                List<double> endWidths = new List<double>();

                for (int i = 0; i < N; i++)
                {
                    coords.Add(pline.GetPoint2dAt(i));
                    bulges.Add(pline.GetBulgeAt(i));
                    startWidths.Add(pline.GetStartWidthAt(i));
                    endWidths.Add(pline.GetEndWidthAt(i));
                }

                List<int> newIndices = new List<int>();

                if (!isClosed)
                {
                    // A. 열린 폴리라인인 경우: [N-1, N-2, ..., 0] 역순 재배치
                    for (int i = N - 1; i >= 0; i--)
                    {
                        newIndices.Add(i);
                    }
                    ed.WriteMessage("\n[열린 폴리라인] 전체 정점의 순서가 역순으로 변경되었습니다.\n");
                }
                else
                {
                    // B. 닫힌 폴리라인인 경우
                    ed.WriteMessage("\n인접한 두 정점을 입력하세요.");

                    // 첫 번째 정점 선택 프롬프트
                    PromptPointOptions ppo1 = new PromptPointOptions("\n첫 번째 정점 근처를 선택하세요: ");
                    PromptPointResult ppr1 = ed.GetPoint(ppo1);
                    if (ppr1.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\n[취소됨] 정점이 입력되지 않았습니다.\n");
                        return;
                    }
                    Point3d p1 = ppr1.Value;

                    // 두 번째 정점 선택 프롬프트
                    PromptPointOptions ppo2 = new PromptPointOptions("\n두 번째 정점 근처를 선택하세요: ");
                    ppo2.UseBasePoint = true;
                    ppo2.BasePoint = p1;
                    PromptPointResult ppr2 = ed.GetPoint(ppo2);
                    if (ppr2.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\n[취소됨] 정점이 입력되지 않았습니다.\n");
                        return;
                    }
                    Point3d p2 = ppr2.Value;

                    // 선택 좌표 기준 가장 가까운 정점의 인덱스 검색 (0-based)
                    int idx1 = FindClosestVertexIndex(pline, p1);
                    int idx2 = FindClosestVertexIndex(pline, p2);

                    // 인접 여부 확인 (닫힌 폴리라인이므로 순환 연결성 고려)
                    bool isForward = (idx1 + 1) % N == idx2;
                    bool isReverse = (idx2 + 1) % N == idx1;

                    if (!isForward && !isReverse)
                    {
                        // 입력한 두 점이 인접하지 않은 경우 오류 메시지 출력 후 종료
                        ed.WriteMessage("\n인집한 두 점을 선택하여야 합니다.\n");
                        return;
                    }

                    // 1-based 인덱스 정보 산출 (사용자 가독성 일치용)
                    int userIdx1 = idx1 + 1;
                    int userIdx2 = idx2 + 1;

                    if (isForward)
                    {
                        // 예시 1) [3, 4] 순방향 입력 시: [3, 4, 5, 6, 1, 2] 순으로 정렬
                        for (int k = 0; k < N; k++)
                        {
                            newIndices.Add((idx1 + k) % N);
                        }
                        ed.WriteMessage(string.Format("\n[닫힌 폴리라인] 입력받은 인덱스 [{0}, {1}](순방향) 기준으로 재배치 완료 (닫힌 상태 유지)\n", userIdx1, userIdx2));
                    }
                    else
                    {
                        // 예시 2) [3, 2] 역방향 입력 시: [3, 2, 1, 6, 5, 4] 순으로 정렬
                        for (int k = 0; k < N; k++)
                        {
                            int idx = (idx1 - k) % N;
                            if (idx < 0) idx += N;
                            newIndices.Add(idx);
                        }
                        ed.WriteMessage(string.Format("\n[닫힌 폴리라인] 입력받은 인덱스 [{0}, {1}](역방향) 기준으로 재배치 완료 (닫힌 상태 유지)\n", userIdx1, userIdx2));
                    }
                }

                // 2. 새로운 정점 리스트 구축 (벌지 및 선폭 변환 처리)
                List<Point2d> newCoords = new List<Point2d>();
                List<double> newBulges = new List<double>();
                List<double> newStartWidths = new List<double>();
                List<double> newEndWidths = new List<double>();

                for (int i = 0; i < N; i++)
                {
                    int u = newIndices[i];
                    newCoords.Add(coords[u]);

                    if (i < N - 1 || isClosed)
                    {
                        int v = newIndices[(i + 1) % N];

                        // 원래 순서 u -> v 가 순방향인지 역방향인지 확인
                        if (v == (u + 1) % N)
                        {
                            // 순방향 세그먼트: 기존 속성 상속
                            newBulges.Add(bulges[u]);
                            newStartWidths.Add(startWidths[u]);
                            newEndWidths.Add(endWidths[u]);
                        }
                        else if (u == (v + 1) % N)
                        {
                            // 역방향 세그먼트: 벌지 부호 반전, 선폭 시작/끝 대칭 교환
                            newBulges.Add(-bulges[v]);
                            newStartWidths.Add(endWidths[v]);
                            newEndWidths.Add(startWidths[v]);
                        }
                        else
                        {
                            newBulges.Add(0);
                            newStartWidths.Add(0);
                            newEndWidths.Add(0);
                        }
                    }
                    else
                    {
                        // 열린 폴리라인의 종단 정점
                        newBulges.Add(0);
                        newStartWidths.Add(0);
                        newEndWidths.Add(0);
                    }
                }

                // 3. 기존 폴리라인 데이터 수정 적용
                for (int i = 0; i < N; i++)
                {
                    pline.SetPointAt(i, newCoords[i]);
                    pline.SetBulgeAt(i, newBulges[i]);
                    pline.SetStartWidthAt(i, newStartWidths[i]);
                    pline.SetEndWidthAt(i, newEndWidths[i]);
                }

                tr.Commit();
                ed.WriteMessage("\n[완료] 폴리라인 정점 인덱스 재정렬이 정상 완료되었습니다.\n");
            }
        }

        /// <summary>
        /// 클릭 지점과 가장 가까운 폴리라인 정점의 인덱스(0-based)를 검색합니다.
        /// </summary>
        private int FindClosestVertexIndex(Polyline pline, Point3d pt)
        {
            int closestIdx = 0;
            double minDistance = double.MaxValue;
            int N = pline.NumberOfVertices;

            for (int i = 0; i < N; i++)
            {
                Point3d vPt = pline.GetPoint3dAt(i);
                double dist = pt.DistanceTo(vPt);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestIdx = i;
                }
            }

            return closestIdx;
        }
    }
}
