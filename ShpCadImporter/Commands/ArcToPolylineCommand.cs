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
    /// 호(Arc) 객체 또는 호 세그먼트가 포함된 폴리라인(LWPolyline)을 
    /// 지정한 옵션(분할수 N, 최대 중앙종거 M, 고정 거리 D)에 따라 직선 세그먼트 형태의 폴리라인으로 변환해주는 AutoCAD 커맨드 클래스.
    /// 기존 LISP의 C:ARCTOPOLYLINE 기능을 완벽히 프로그램 방식으로 재현하며, [Rule 3 예외]에 따라 생성물은 노란색(Color 2)을 적용합니다.
    /// </summary>
    public class ArcToPolylineCommand
    {
        // 사용자 요청에 맞추어 단축명령어 A2PL 등록 및 공식명칭 ARCTOPOLYLINE 동시 지원
        [CommandMethod("ARCTOPOLYLINE")]
        [CommandMethod("A2PL")]
        public void ConvertArcToPolyline()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                ed.WriteMessage("\n[호 폴리라인 변환 작업을 시작합니다: Arc to Polyline]\n");

                // 1. 변환 대상 객체 선택 필터 (ARC, LWPOLYLINE)
                TypedValue[] tv = new TypedValue[]
                {
                    new TypedValue((int)DxfCode.Operator, "<or"),
                    new TypedValue((int)DxfCode.Start, "ARC"),
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                    new TypedValue((int)DxfCode.Start, "POLYLINE"),
                    new TypedValue((int)DxfCode.Operator, "or>")
                };
                SelectionFilter filter = new SelectionFilter(tv);

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\n변환할 호(Arc) 또는 폴리라인을 선택하세요: ";

                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 객체가 선택되지 않았습니다.\n");
                    return;
                }

                SelectionSet ss = psr.Value;
                if (ss == null || ss.Count == 0) return;

                // 2. 변환 방식 분할 옵션 키워드 입력 요청
                PromptKeywordOptions pko = new PromptKeywordOptions("\n변환 옵션을 선택하세요 [분할(N)/중앙종거(M)/거리(D)]: ");
                pko.Keywords.Add("Number", "N", "분할(N)");
                pko.Keywords.Add("Midordinate", "M", "중앙종거(M)");
                pko.Keywords.Add("Distance", "D", "거리(D)");
                pko.Keywords.Default = "Number";

                PromptResult pr = ed.GetKeywords(pko);
                if (pr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 옵션 선택이 취소되었습니다.\n");
                    return;
                }

                string mode = pr.StringResult;
                double val = 0.0;

                // 3. 옵션별 세부 변수값 입력 요청
                if (mode == "Number")
                {
                    PromptIntegerOptions pio = new PromptIntegerOptions("\n분할 개수 입력 <10>: ");
                    pio.DefaultValue = 10;
                    pio.UseDefaultValue = true;
                    pio.AllowNegative = false;
                    pio.AllowZero = false;
                    
                    PromptIntegerResult pir = ed.GetInteger(pio);
                    if (pir.Status != PromptStatus.OK) return;
                    mode = "N";
                    val = pir.Value;
                }
                else if (mode == "Midordinate")
                {
                    PromptDoubleOptions pdo = new PromptDoubleOptions("\n최대 중앙종거(m) 입력 <0.05>: ");
                    pdo.DefaultValue = 0.05;
                    pdo.UseDefaultValue = true;
                    pdo.AllowNegative = false;
                    pdo.AllowZero = false;

                    PromptDoubleResult pdr = ed.GetDouble(pdo);
                    if (pdr.Status != PromptStatus.OK) return;
                    mode = "M";
                    val = pdr.Value;
                }
                else if (mode == "Distance")
                {
                    PromptDoubleOptions pdo = new PromptDoubleOptions("\n세그먼트 길이(m) 입력 <1.0>: ");
                    pdo.DefaultValue = 1.0;
                    pdo.UseDefaultValue = true;
                    pdo.AllowNegative = false;
                    pdo.AllowZero = false;

                    PromptDoubleResult pdr = ed.GetDouble(pdo);
                    if (pdr.Status != PromptStatus.OK) return;
                    mode = "D";
                    val = pdr.Value;
                }

                int convertedCount = 0;

                // 4. 트랜잭션 기동 및 직선화 폴리라인 신설 작업 진행
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    foreach (SelectedObject so in ss)
                    {
                        if (so == null) continue;

                        Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        string layerName = ent.Layer;

                        // A. ARC 객체인 경우
                        if (ent is Arc)
                        {
                            Arc arc = (Arc)ent;
                            List<Point3d> pts = GetArcSegmentedPoints(arc, arc.StartParam, arc.EndParam, mode, val);
                            if (pts.Count >= 2)
                            {
                                CreateLightweightPolyline(pts, layerName, btr, tr);
                                convertedCount++;
                            }
                        }
                        // B. Polyline (LWPolyline 또는 구형 Polyline) 객체인 경우
                        else if (ent is Polyline)
                        {
                            Polyline pline = (Polyline)ent;
                            List<Point3d> pts = new List<Point3d>();
                            int numSegs = (int)pline.EndParam;

                            for (int j = 0; j < numSegs; j++)
                            {
                                double bulge = pline.GetBulgeAt(j);
                                Point3d p1 = pline.GetPointAtParameter(j);

                                if (bulge == 0.0)
                                {
                                    pts.Add(p1);
                                }
                                else
                                {
                                    // Bulge가 있는 호 구간만 조밀 직선 분할 진행
                                    List<Point3d> segPts = GetArcSegmentedPoints(pline, j, j + 1, mode, val);
                                    
                                    // 정점 중복 방지를 위해 마지막 정점은 제외하고 누적
                                    for (int k = 0; k < segPts.Count - 1; k++)
                                    {
                                        pts.Add(segPts[k]);
                                    }
                                }
                            }

                            Polyline newPline = null;
                            if (pline.Closed)
                            {
                                newPline = CreateLightweightPolyline(pts, layerName, btr, tr);
                                newPline.Closed = true;
                            }
                            else
                            {
                                // 닫히지 않은 경우 마지막 정점 추가
                                pts.Add(pline.GetPointAtParameter(numSegs));
                                newPline = CreateLightweightPolyline(pts, layerName, btr, tr);
                            }

                            if (newPline != null)
                            {
                                convertedCount++;
                            }
                        }
                    }

                    tr.Commit();
                }

                ed.WriteMessage(string.Format("\n[완료] 총 {0}개의 객체가 성공적으로 세그먼트 폴리라인(노란색)으로 변환되었습니다.\n", convertedCount));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] 호 폴리라인 변환 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
        }

        /// <summary>
        /// 호 구간의 반경과 중심각을 도출하고 주어진 모드 조건에 맞게 좌표 분할 포인트를 획득합니다.
        /// </summary>
        private static List<Point3d> GetArcSegmentedPoints(Curve obj, double startParam, double endParam, string mode, double val)
        {
            double distStart = obj.GetDistanceAtParameter(startParam);
            double distEnd = obj.GetDistanceAtParameter(endParam);
            double totalDist = Math.Abs(distEnd - distStart);

            double radius = 0.0;
            double ang = 0.0;

            // 단독 Arc 객체 분석
            Arc arcObj = obj as Arc;
            if (arcObj != null)
            {
                radius = arcObj.Radius;
                ang = arcObj.EndAngle - arcObj.StartAngle;
                if (ang < 0) ang += 2.0 * Math.PI;
            }
            // Polyline Bulge 세그먼트 분석
            else
            {
                Polyline pline = obj as Polyline;
                if (pline != null)
                {
                    int segmentIndex = (int)Math.Floor(startParam);
                    double bulge = pline.GetBulgeAt(segmentIndex);
                    Point3d p1 = obj.GetPointAtParameter(startParam);
                    Point3d p2 = obj.GetPointAtParameter(endParam);
                    double distP1P2 = p1.DistanceTo(p2);
                    
                    ang = 4.0 * Math.Atan(Math.Abs(bulge));
                    radius = (distP1P2 / 2.0) / Math.Sin(ang / 2.0);
                }
            }

            int n = 1;
            if (mode == "N")
            {
                n = (int)Math.Max(1, val);
            }
            else if (mode == "M")
            {
                if (radius <= val)
                {
                    n = 1;
                }
                else
                {
                    double x = 1.0 - val / radius;
                    double segAng = 2.0 * Math.Acos(x);
                    n = (int)Math.Floor(Math.Max(1.0, Math.Abs(ang / segAng)));
                    if (Math.Abs(ang) % segAng > 0.0001)
                    {
                        n++;
                    }
                }
            }
            else if (mode == "D")
            {
                if (val <= 2.0 * radius)
                {
                    double x = val / (2.0 * radius);
                    double segAng = 2.0 * Math.Asin(x);
                    n = (int)Math.Floor(ang / segAng);
                }
                else
                {
                    n = 1;
                }
            }

            List<Point3d> pts = new List<Point3d>();
            if (mode == "D")
            {
                pts.Add(obj.GetPointAtParameter(startParam));
                int i = 1;
                double segDist = distStart + i * val;
                while (segDist + 1e-6 < distEnd)
                {
                    pts.Add(obj.GetPointAtDist(segDist));
                    i++;
                    segDist = distStart + i * val;
                }
                pts.Add(obj.GetPointAtParameter(endParam));
            }
            else
            {
                for (int i = 0; i <= n; i++)
                {
                    double segDist = distStart + (i / (double)n) * totalDist;
                    if (segDist > distEnd) segDist = distEnd;
                    pts.Add(obj.GetPointAtDist(segDist));
                }
            }

            return pts;
        }

        /// <summary>
        /// 추출된 좌표 리스트로 2번(노란색) LightweightPolyline을 생성해 도면에 추가합니다.
        /// </summary>
        private static Polyline CreateLightweightPolyline(List<Point3d> pts, string layerName, BlockTableRecord btr, Transaction tr)
        {
            Polyline pline = new Polyline();
            pline.SetDatabaseDefaults();

            for (int i = 0; i < pts.Count; i++)
            {
                Point3d p = pts[i];
                pline.AddVertexAt(i, new Point2d(p.X, p.Y), 0.0, 0.0, 0.0);
            }

            // [Rule 3 예외] 신설되는 직선화 도형은 노란색(Color 2)을 적용한다.
            pline.ColorIndex = 2;
            pline.Layer = layerName;

            btr.AppendEntity(pline);
            tr.AddNewlyCreatedDBObject(pline, true);

            return pline;
        }
    }
}
