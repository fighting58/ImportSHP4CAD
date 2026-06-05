using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using ShpCadImporter.Labeling;

namespace ShpCadImporter.Commands
{
    /// <summary>
    /// AutoCAD Map 3D / Civil 3D 도면 내 폴리라인(LWPolyline)에 부착된 객체데이터(Object Data, OD)를 추출하여
    /// 폴리곤의 시각적 중심점(Polylabel)과 최적 각도(Ray-Casting)에 맞춰 텍스트 레이블을 대화식으로 자동 생성해 주는 커맨드 클래스.
    /// 기존 LISP의 C:ODLABEL 기능을 C# .NET API로 마이그레이션 하였으며, 단축명령어는 ODL로 지정합니다.
    /// </summary>
    public class ODLabelCommand
    {
        [CommandMethod("ODLABEL")]
        [CommandMethod("ODL")]
        public void RunODLabel()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                ed.WriteMessage("\n[객체데이터(OD) 텍스트 레이블 생성 작업을 시작합니다: ODL]\n");

                // 1. 객체 선택 (LWPolyline 필터 적용)
                TypedValue[] tv = new TypedValue[]
                {
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
                };
                SelectionFilter filter = new SelectionFilter(tv);

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\n객체데이터(OD)를 추출할 폴리라인(LWPolyline)을 선택하세요: ";

                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
                {
                    ed.WriteMessage("\n[취소됨] 객체가 선택되지 않았습니다.\n");
                    return;
                }

                // 2. 표기할 OD field 입력 요청
                PromptStringOptions psoStr = new PromptStringOptions("\n표기할 OD 필드명을 입력하세요 (대소문자 구분 없음): ");
                psoStr.AllowSpaces = true;

                PromptResult prStr = ed.GetString(psoStr);
                if (prStr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 필드명 입력이 취소되었습니다.\n");
                    return;
                }

                string fieldName = prStr.StringResult.Trim();
                if (string.IsNullOrEmpty(fieldName))
                {
                    ed.WriteMessage("\n[오류] 필드명이 입력되지 않았습니다.\n");
                    return;
                }

                ed.WriteMessage(string.Format("\n'{0}' 필드 값을 추출하여 실시간 도심 레이블 생성을 시작합니다...", fieldName));

                int processedCount = 0;
                SelectionSet ss = psr.Value;

                // 3. 트랜잭션 기동
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    foreach (SelectedObject so in ss)
                    {
                        if (so == null) continue;

                        Polyline pline = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Polyline;
                        if (pline == null) continue;

                        // Map 3D LISP API (Application.Invoke)를 이용한 객체데이터(OD) 테이블 및 값 분석
                        List<string> odTables = GetOdTables(so.ObjectId);
                        if (odTables.Count == 0) continue;

                        string odValue = null;
                        foreach (string table in odTables)
                        {
                            string exactFieldName = GetExactFieldNameCaseInsensitive(table, fieldName);
                            if (string.IsNullOrEmpty(exactFieldName))
                            {
                                exactFieldName = fieldName; // 대소문자 무관 검색 실패 시 원본 입력값으로 Fallback 시도
                            }

                            odValue = GetOdField(so.ObjectId, table, exactFieldName);
                            if (!string.IsNullOrEmpty(odValue))
                            {
                                break; // 첫 번째로 발견된 필드 값 매칭 사용
                            }
                        }

                        // 유효한 OD 필드 값이 존재하는 경우에만 레이블 생성 진행
                        if (!string.IsNullOrEmpty(odValue))
                        {
                            // A. Polylabel 시각적 중심좌표 산출
                            double[][] extRing = new double[pline.NumberOfVertices][];
                            List<Point2d> pts = new List<Point2d>();
                            for (int j = 0; j < pline.NumberOfVertices; j++)
                            {
                                Point2d p2d = pline.GetPoint2dAt(j);
                                extRing[j] = new double[] { p2d.X, p2d.Y };
                                pts.Add(p2d);
                            }

                            double[] centerCoords = Polylabel.GetPolylabel(extRing, null, 0.5);
                            if (centerCoords == null || centerCoords.Length < 2) continue;

                            Point2d centerPt = new Point2d(centerCoords[0], centerCoords[1]);
                            Point3d centerPt3d = new Point3d(centerCoords[0], centerCoords[1], pline.Elevation);

                            // B. Ray-Casting 기반 최적 텍스트 회전각 계산
                            double textAngle = GetBestAngle(centerPt, pts);

                            // C. 텍스트 개체 생성
                            DBText dbText = new DBText();
                            dbText.SetDatabaseDefaults();
                            dbText.TextString = odValue;
                            dbText.Height = 1.0; // 높이 규격 1.0 지정
                            dbText.Rotation = textAngle;

                            // 한글 깨짐 방지를 위해 "SHP_한글" 텍스트 스타일 적용
                            dbText.TextStyleId = CAD.CadEntityBuilder.GetOrCreateKoreanTextStyle(db, tr);

                            // Middle Center 정렬 수립
                            dbText.Justify = AttachmentPoint.MiddleCenter;
                            dbText.AlignmentPoint = centerPt3d;
                            dbText.Position = centerPt3d;

                            btr.AppendEntity(dbText);
                            tr.AddNewlyCreatedDBObject(dbText, true);

                            processedCount++;
                        }
                    }

                    tr.Commit();
                }

                ed.WriteMessage(string.Format("\n[완료] 총 {0}개 객체에서 OD 데이터 레이블을 성공적으로 추출하여 생성하였습니다.\n", processedCount));
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                if (ex.ErrorStatus == ErrorStatus.NotApplicable)
                {
                    ed.WriteMessage("\n[오류] AutoCAD Map 3D 또는 Civil 3D 환경에서만 객체데이터(OD)를 추출할 수 있습니다.\n");
                }
                else
                {
                    ed.WriteMessage(string.Format("\n[오류 발생] OD 레이블 생성 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] OD 레이블 생성 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
        }

        #region Map 3D AutoLISP Interop Helper Methods

        /// <summary>
        /// 객체에 부착된 모든 OD 테이블 이름 목록을 반환합니다.
        /// </summary>
        private static List<string> GetOdTables(ObjectId entId)
        {
            List<string> tableNames = new List<string>();
            try
            {
                ResultBuffer args = new ResultBuffer();
                args.Add(new TypedValue((int)LispDataType.Text, "ADE_ODGETTABLES"));
                args.Add(new TypedValue((int)LispDataType.ObjectId, entId));

                ResultBuffer res = Application.Invoke(args);
                if (res != null)
                {
                    foreach (TypedValue tv in res)
                    {
                        if (tv.TypeCode == (int)LispDataType.Text)
                        {
                            tableNames.Add((string)tv.Value);
                        }
                    }
                }
            }
            catch
            {
                // Map 3D가 아닐 경우 공백 리스트 반환
            }
            return tableNames;
        }

        /// <summary>
        /// 테이블의 정의(ADE_ODTABLEDEFN)를 조회하여 대소문자 구분 없이 매칭되는 필드의 실제(정확한) 이름을 찾습니다.
        /// </summary>
        private static string GetExactFieldNameCaseInsensitive(string tableName, string fieldName)
        {
            try
            {
                ResultBuffer args = new ResultBuffer();
                args.Add(new TypedValue((int)LispDataType.Text, "ADE_ODTABLEDEFN"));
                args.Add(new TypedValue((int)LispDataType.Text, tableName));

                ResultBuffer res = Application.Invoke(args);
                if (res != null)
                {
                    bool nextIsFieldName = false;
                    foreach (TypedValue tv in res)
                    {
                        if (tv.TypeCode == (int)LispDataType.Text)
                        {
                            string val = (string)tv.Value;
                            if (nextIsFieldName)
                            {
                                if (val.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                                {
                                    return val; // 정확한 대소문자를 포함한 실제 필드명 반환
                                }
                                nextIsFieldName = false;
                            }
                            else if (val.Equals("ColName", StringComparison.OrdinalIgnoreCase))
                            {
                                nextIsFieldName = true;
                            }
                        }
                        else
                        {
                            // ListBegin 이나 ListEnd가 아닌 다른 타입이 끼면 상태 리셋
                            if (tv.TypeCode != (int)LispDataType.ListBegin && tv.TypeCode != (int)LispDataType.ListEnd)
                            {
                                nextIsFieldName = false;
                            }
                        }
                    }
                }
            }
            catch
            {
                // 실패 시 침묵
            }
            return null;
        }

        /// <summary>
        /// 객체데이터의 특정 테이블, 특정 필드값을 획득합니다.
        /// </summary>
        private static string GetOdField(ObjectId entId, string tableName, string fieldName)
        {
            try
            {
                ResultBuffer args = new ResultBuffer();
                args.Add(new TypedValue((int)LispDataType.Text, "ADE_ODGETFIELD"));
                args.Add(new TypedValue((int)LispDataType.ObjectId, entId));
                args.Add(new TypedValue((int)LispDataType.Text, tableName));
                args.Add(new TypedValue((int)LispDataType.Text, fieldName));
                args.Add(new TypedValue((int)LispDataType.Int16, (short)0)); // 16비트 정수로 첫 번째 레코드 검색

                ResultBuffer res = Application.Invoke(args);
                if (res != null)
                {
                    foreach (TypedValue tv in res)
                    {
                        if (tv.TypeCode == (int)LispDataType.Nil)
                        {
                            return null;
                        }

                        // ListBegin, ListEnd, Nil이 아닌 모든 유효한 값을 텍스트(문자열)로 변환하여 반환
                        if (tv.TypeCode != (int)LispDataType.ListBegin &&
                            tv.TypeCode != (int)LispDataType.ListEnd &&
                            tv.TypeCode != (int)LispDataType.Nil)
                        {
                            if (tv.Value != null)
                            {
                                string valStr = tv.Value.ToString().Trim();
                                if (!string.IsNullOrEmpty(valStr))
                                {
                                    return valStr;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // 실패 시 침묵
            }
            return null;
        }

        #endregion

        #region Ray-Casting Text Rotation Angle Calculator

        /// <summary>
        /// 폴리곤 외곽선 경계 좌표들과 시각적 중심점을 기준으로 가장 균형 잡힌 글자 배치 각도를 역산합니다.
        /// </summary>
        private static double GetBestAngle(Point2d pc, List<Point2d> pts)
        {
            double bestAng = 0.0;
            double maxLen = 0.0;
            double ang = 0.0;

            for (int step = 0; step < 18; step++) // 10도 간격으로 180도 전각 회전 검증
            {
                double len = 0.0;
                double[] angles = new double[] { ang, ang + Math.PI };

                foreach (double a in angles)
                {
                    double minT = 1e12;
                    bool found = false;

                    for (int i = 0; i < pts.Count; i++)
                    {
                        Point2d p1 = pts[i];
                        Point2d p2 = pts[(i + 1) % pts.Count];

                        double? tVal = IntersectRayDist(pc, a, p1, p2);
                        if (tVal.HasValue && tVal.Value < minT)
                        {
                            minT = tVal.Value;
                            found = true;
                        }
                    }

                    if (found)
                    {
                        len += minT;
                    }
                }

                if (len > maxLen)
                {
                    maxLen = len;
                    bestAng = ang;
                }

                ang += Math.PI / 18.0;
            }

            // 가독성을 위한 -90 ~ 90도 범위로 각도 표준화 (반전 방지)
            if (bestAng > Math.PI / 2.0 && bestAng <= 3.0 * Math.PI / 2.0)
            {
                bestAng -= Math.PI;
            }

            return bestAng;
        }

        /// <summary>
        /// 반직선과 폴리곤의 특정 선분이 만나는 교차 거리를 산출합니다.
        /// </summary>
        private static double? IntersectRayDist(Point2d pc, double ang, Point2d p1, Point2d p2)
        {
            double v1x = Math.Cos(ang);
            double v1y = Math.Sin(ang);
            double v2x = p2.X - p1.X;
            double v2y = p2.Y - p1.Y;

            double det = v1x * v2y - v1y * v2x;
            if (Math.Abs(det) > 1e-9)
            {
                double tParam = ((p1.X - pc.X) * v2y - (p1.Y - pc.Y) * v2x) / det;
                double uParam = ((p1.X - pc.X) * v1y - (p1.Y - pc.Y) * v1x) / det;

                if (tParam > 0 && uParam >= 0 && uParam <= 1)
                {
                    return tParam;
                }
            }
            return null;
        }

        #endregion
    }
}
