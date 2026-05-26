using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ShpCadImporter.Commands
{
    /// <summary>
    /// 선택한 텍스트(DBText/MText) 객체의 삽입점/정렬점을 기준으로 
    /// 텍스트 높이의 1.3배 반지름을 갖는 원을 생성하는 AutoCAD 커맨드 클래스.
    /// VBA의 DrawCirclesAroundText 매크로 기능을 C# .NET API로 변환하였습니다.
    /// </summary>
    public class DrawCirclesCommand
    {
        // AutoCAD 기본 예약 단축명령어(C, TEXT, TCIRCLE 등)와 충돌하지 않는 3~4글자 단축명령어 지정:
        // TXTC (Text Circle)
        [CommandMethod("TXTC")]
        public void DrawCirclesAroundText()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                // 1. 텍스트 객체만 선택되도록 선택 필터 구성 (VBA의 SelectOnScreen 보강)
                TypedValue[] tv = new TypedValue[]
                {
                    new TypedValue((int)DxfCode.Operator, "<or"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Operator, "or>")
                };
                SelectionFilter filter = new SelectionFilter(tv);

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\n도면 내의 텍스트(DBText/MText)를 선택하세요: ";
                
                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 객체가 선택되지 않았습니다.\n");
                    return;
                }

                SelectionSet ss = psr.Value;
                int circleCount = 0;

                // 2. 트랜잭션 시작 (실행 취소(Undo)를 고려한 안전한 데이터 변경 처리)
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 현재 활성화된 스페이스(ModelSpace 또는 Layout Space) 획득
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    foreach (SelectedObject so in ss)
                    {
                        if (so == null) continue;

                        Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        double textHeight = 0;
                        Point3d circleCenter = Point3d.Origin;
                        string textLayer = ent.Layer; // 원 생성 시 동일 레이어로 일치화하기 위해 레이어명 저장

                        // 3. DBText 분석
                        if (ent is DBText dbText)
                        {
                            textHeight = dbText.Height;

                            // 정렬(Justify) 상태 확인하여 원의 중심점 획득 (VBA의 TextAlignmentPoint 분석 로직)
                            if (dbText.Justify == AttachmentPoint.BaseLeft)
                            {
                                // 왼쪽 정렬(기본값)인 경우 Position 사용
                                circleCenter = dbText.Position;
                            }
                            else
                            {
                                // 그 외 정렬(정렬 기준점이 존재하는 경우)인 경우 AlignmentPoint 사용
                                circleCenter = dbText.AlignmentPoint;
                            }
                        }
                        // 4. MText 분석 (다중행 텍스트 지원 보강)
                        else if (ent is MText mText)
                        {
                            textHeight = mText.TextHeight;
                            circleCenter = mText.Location;
                        }
                        else
                        {
                            continue;
                        }

                        // 5. 원의 반지름 계산 (텍스트 높이의 1.3배 - VBA 일치)
                        double circleRadius = textHeight * 1.3;

                        // 6. 원(Circle) 객체 생성 및 속성 정의
                        Circle circle = new Circle();
                        circle.SetDatabaseDefaults();
                        circle.Center = circleCenter;
                        circle.Radius = circleRadius;
                        circle.Layer = textLayer; // 원이 생성될 레이어를 원본 텍스트 레이어로 지정

                        // 도면 스페이스에 원 추가 및 트랜잭션 등록
                        btr.AppendEntity(circle);
                        tr.AddNewlyCreatedDBObject(circle, true);
                        circleCount++;
                    }

                    // 변경사항 확정
                    tr.Commit();
                }

                ed.WriteMessage(string.Format("\n[완료] 선택한 텍스트 주위에 {0}개의 원을 정상적으로 생성했습니다.\n", circleCount));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] 원 생성 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
        }
    }
}
