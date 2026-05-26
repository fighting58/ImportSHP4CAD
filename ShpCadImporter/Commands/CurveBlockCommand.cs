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
    /// 단일 호(Arc)를 입력받아 지정된 도로폭, 중앙종거, 계산방식(Simple/Refine)에 따라 
    /// 곡선 가구(街區, Block)의 안쪽/바깥쪽 경계용 폴리라인을 생성하는 AutoCAD 커맨드 클래스.
    /// static 변수를 통해 다음 명령어 실행 시에도 이전 입력값들이 지속되도록 구성하였습니다.
    /// </summary>
    public class CurveBlockCommand
    {
        // 세션 유지 변수 (다음 명령 호출 시에도 입력한 값 유지)
        private static double _roadWidth = 20.0;     // 도로폭 디폴트값: 20
        private static double _jonggeo = 0.05;       // 중앙종거 디폴트값: 0.05
        private static string _calcMode = "Simple";   // 계산방식 디폴트값: Simple

        // 가구(街區, Block) 의미에 일치하는 단축 명령어:
        // GAGU (가구계산)
        [CommandMethod("GAGU")]
        public void CalculateCurveBlock()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. 호(Arc) 입력 요청
            PromptEntityOptions peo = new PromptEntityOptions("\n곡선가구(블록) 계산을 위한 호(안쪽)를 선택하세요: ");
            peo.SetRejectMessage("\n단일 호만 처리할 수 있습니다.");
            peo.AddAllowedClass(typeof(Arc), exactMatch: true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[취소됨] 호가 선택되지 않았습니다.\n");
                return;
            }

            ObjectId arcId = per.ObjectId;

            // 2. 도로폭 입력 요청 (이전값 유지)
            PromptDoubleOptions pdoWidth = new PromptDoubleOptions(string.Format("\n도로폭을 입력하세요(단위: m) <{0}>: ", _roadWidth));
            pdoWidth.UseDefaultValue = true;
            pdoWidth.DefaultValue = _roadWidth;
            PromptDoubleResult pdrWidth = ed.GetDouble(pdoWidth);
            if (pdrWidth.Status == PromptStatus.OK)
            {
                _roadWidth = pdrWidth.Value;
            }
            else if (pdrWidth.Status != PromptStatus.None)
            {
                ed.WriteMessage("\n[취소됨] 입력이 취소되었습니다.\n");
                return;
            }

            // 3. 중앙종거 입력 요청 (이전값 유지)
            PromptDoubleOptions pdoJong = new PromptDoubleOptions(string.Format("\n중앙종거를 입력하세요(단위: m) <{0}>: ", _jonggeo));
            pdoJong.UseDefaultValue = true;
            pdoJong.DefaultValue = _jonggeo;
            PromptDoubleResult pdrJong = ed.GetDouble(pdoJong);
            if (pdrJong.Status == PromptStatus.OK)
            {
                _jonggeo = pdrJong.Value;
            }
            else if (pdrJong.Status != PromptStatus.None)
            {
                ed.WriteMessage("\n[취소됨] 입력이 취소되었습니다.\n");
                return;
            }

            // 4. 계산방식 선택 요청 (Simple / Refine) (이전값 유지)
            PromptKeywordOptions pkoMode = new PromptKeywordOptions(string.Format("\n계산방식을 선택하세요 [Simple/Refine] <{0}>: ", _calcMode));
            pkoMode.Keywords.Add("Simple");
            pkoMode.Keywords.Add("Refine");
            pkoMode.Keywords.Default = _calcMode;
            pkoMode.AllowNone = true;
            PromptResult prMode = ed.GetKeywords(pkoMode);
            if (prMode.Status == PromptStatus.OK)
            {
                _calcMode = prMode.StringResult;
            }
            else if (prMode.Status != PromptStatus.None)
            {
                ed.WriteMessage("\n[취소됨] 입력이 취소되었습니다.\n");
                return;
            }

            // 5. 계산 파이프라인 수행
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Arc arcObj = tr.GetObject(arcId, OpenMode.ForRead) as Arc;
                if (arcObj == null)
                {
                    ed.WriteMessage("\n[오류] 선택한 호 객체를 열 수 없습니다.\n");
                    return;
                }

                // 호 제원 및 각도 추출
                double radius = arcObj.Radius;
                Point3d center = arcObj.Center;
                double startAngle = arcObj.StartAngle;
                double endAngle = arcObj.EndAngle;

                // 호 전체 각도 산출 (역방향 회전 보정)
                double arcAngle = endAngle - startAngle;
                if (arcAngle < 0)
                {
                    arcAngle += 2.0 * Math.PI;
                }

                // 중앙종거와 반지름 비교를 통한 기하 분석
                double tmp = 1.0 - (_jonggeo / radius);
                // Math.Acos 도메인인 [-1, 1] 범위로 클램핑 처리 (안전장치)
                tmp = Math.Max(-1.0, Math.Min(1.0, tmp));
                double theta = 2.0 * Math.Acos(tmp);

                if (theta <= 0)
                {
                    ed.WriteMessage("\n[오류] 중앙종거와 호의 기하 계산에 오류가 발생했습니다. 중앙종거 값을 확인하세요.\n");
                    return;
                }

                // 등분 개수(n) 계산 및 각도 단위 산출
                int n = (int)Math.Ceiling(arcAngle / theta);
                if (n <= 0) n = 1;
                double thetaPrime = arcAngle / n;

                ed.WriteMessage(string.Format("\n[분석 결과] 등분 개수(n) = {0}, 등분 각도(ThetaPrime) = {1:F6} rad\n", n, thetaPrime));

                List<Point2d> innerPoints = new List<Point2d>();
                List<Point2d> outerPoints = new List<Point2d>();

                // 가상의 Arc를 데이터베이스에 추가/삭제하지 않고, 수학 공식을 통하여 다이렉트 좌표 계산 (성능 대폭 향상)
                if (_calcMode.Equals("Refine", StringComparison.OrdinalIgnoreCase))
                {
                    // --- REFINE 방식 계산 ---
                    double outerRadiusRefine = (radius + _roadWidth) / Math.Cos(thetaPrime / 2.0);

                    // 안쪽 곡선(PolyIn) 좌표 채우기 (n + 1 개 점)
                    for (int i = 0; i <= n; i++)
                    {
                        double angle = startAngle + (i * thetaPrime);
                        double x = center.X + (radius * Math.Cos(angle));
                        double y = center.Y + (radius * Math.Sin(angle));
                        innerPoints.Add(new Point2d(x, y));
                    }

                    // 바깥쪽 곡선(PolyOut) 좌표 채우기 (n + 2 개 점)
                    // i = 0: StartPoint 기점 (radius + RoadWidth)
                    double xStart = center.X + ((radius + _roadWidth) * Math.Cos(startAngle));
                    double yStart = center.Y + ((radius + _roadWidth) * Math.Sin(startAngle));
                    outerPoints.Add(new Point2d(xStart, yStart));

                    // i = 1 ~ n: 외선장 반지름(outerRadiusRefine) 및 하프 오프셋 각도 적용
                    for (int i = 1; i <= n; i++)
                    {
                        double angleMid = startAngle + ((i - 0.5) * thetaPrime);
                        double x = center.X + (outerRadiusRefine * Math.Cos(angleMid));
                        double y = center.Y + (outerRadiusRefine * Math.Sin(angleMid));
                        outerPoints.Add(new Point2d(x, y));
                    }

                    // i = n + 1: EndPoint 기점 (radius + RoadWidth)
                    double xEnd = center.X + ((radius + _roadWidth) * Math.Cos(endAngle));
                    double yEnd = center.Y + ((radius + _roadWidth) * Math.Sin(endAngle));
                    outerPoints.Add(new Point2d(xEnd, yEnd));
                }
                else
                {
                    // --- SIMPLE 방식 계산 ---
                    double outerRadiusSimple = radius + _roadWidth;

                    // 안쪽(PolyIn) 및 바깥쪽(PolyOut) 좌표 채우기 (각각 n + 1 개 점)
                    for (int i = 0; i <= n; i++)
                    {
                        double angle = startAngle + (i * thetaPrime);
                        
                        // 안쪽 좌표
                        double xIn = center.X + (radius * Math.Cos(angle));
                        double yIn = center.Y + (radius * Math.Sin(angle));
                        innerPoints.Add(new Point2d(xIn, yIn));

                        // 바깥쪽 좌표
                        double xOut = center.X + (outerRadiusSimple * Math.Cos(angle));
                        double yOut = center.Y + (outerRadiusSimple * Math.Sin(angle));
                        outerPoints.Add(new Point2d(xOut, yOut));
                    }
                }

                // 6. 도면 공간에 폴리라인 엔티티 신규 드로잉
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // 6.1 안쪽 폴리라인 생성
                Polyline polyIn = new Polyline();
                polyIn.SetDatabaseDefaults();
                polyIn.Layer = arcObj.Layer;
                polyIn.Closed = false;
                polyIn.ColorIndex = 2; // Yellow (VBA acYellow 일치)

                for (int i = 0; i < innerPoints.Count; i++)
                {
                    polyIn.AddVertexAt(i, innerPoints[i], 0, 0, 0);
                }

                btr.AppendEntity(polyIn);
                tr.AddNewlyCreatedDBObject(polyIn, true);

                // 6.2 바깥쪽 폴리라인 생성
                Polyline polyOut = new Polyline();
                polyOut.SetDatabaseDefaults();
                polyOut.Layer = arcObj.Layer;
                polyOut.Closed = false;
                polyOut.ColorIndex = 5; // Blue (VBA acBlue 일치)

                for (int i = 0; i < outerPoints.Count; i++)
                {
                    polyOut.AddVertexAt(i, outerPoints[i], 0, 0, 0);
                }

                btr.AppendEntity(polyOut);
                tr.AddNewlyCreatedDBObject(polyOut, true);

                tr.Commit();
                ed.WriteMessage(string.Format("\n[완료] {0} 방식으로 안쪽(노란색) 및 바깥쪽(파란색) 곡선가구(블록) 경계 폴리라인 생성이 완료되었습니다.\n", _calcMode));
            }
        }
    }
}
