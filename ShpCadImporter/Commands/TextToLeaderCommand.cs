using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ShpCadImporter.Commands
{
    /// <summary>
    /// 기존 텍스트(DBText/MText)를 지시선(MLeader) 객체로 마이그레이션하는 AutoCAD 커맨드 클래스.
    /// VBA의 Text2Lead 매크로 기능을 C# .NET API로 변환하였습니다.
    /// </summary>
    public class TextToLeaderCommand
    {
        [CommandMethod("TEXT2LEAD")]
        [CommandMethod("T2L")]
        public void Text2Lead()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. 기존 스냅 설정(OSMODE) 저장 및 해제 (스냅 간섭 방지)
            int oldOsMode = 0;
            object osmodeVal = Application.GetSystemVariable("OSMODE");
            if (osmodeVal != null)
            {
                oldOsMode = Convert.ToInt32(osmodeVal);
            }

            try
            {
                // 스냅을 끄기 위해 OSMODE를 0으로 설정
                Application.SetSystemVariable("OSMODE", 0);

                // 2. 텍스트 객체 선택 프롬프트
                PromptEntityOptions peo = new PromptEntityOptions("\n텍스트 객체를 선택하세요: ");
                peo.SetRejectMessage("\n선택한 객체가 텍스트(DBText 또는 MText)가 아닙니다.");
                peo.AddAllowedClass(typeof(DBText), exactMatch: true);
                peo.AddAllowedClass(typeof(MText), exactMatch: true);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 객체가 선택되지 않았습니다.\n");
                    return;
                }

                ObjectId textId = per.ObjectId;

                // 트랜잭션 시작 (Undo 블록 자동 생성 역할 수행)
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Entity ent = tr.GetObject(textId, OpenMode.ForWrite) as Entity;
                    if (ent == null)
                    {
                        ed.WriteMessage("\n[오류] 객체를 읽을 수 없습니다.\n");
                        return;
                    }

                    // 선택한 객체 강조 표시
                    ent.Highlight();

                    string textContents = string.Empty;
                    Point3d startPt = Point3d.Origin;
                    double textHeight = db.Textsize;
                    string layerName = ent.Layer;
                    double textWidth = 0.0;

                    // DBText 또는 MText 구조 해제 및 텍스트/위치 추출
                    if (ent is DBText dbText)
                    {
                        // VBA txtEnt.Rotation = 0 동작 일치
                        dbText.Rotation = 0;
                        textContents = dbText.TextString;
                        textHeight = dbText.Height;

                        // 회전 변경사항을 그래픽스 엔진에 강제 반영하여 정확한 GeometricExtents 획득 유도
                        tr.TransactionManager.QueueForGraphicsFlush();

                        try
                        {
                            Extents3d ext = dbText.GeometricExtents;
                            startPt = new Point3d(
                                (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                                (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                                (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0
                            );
                            textWidth = ext.MaxPoint.X - ext.MinPoint.X;
                        }
                        catch
                        {
                            if (dbText.Justify == AttachmentPoint.BaseLeft)
                            {
                                startPt = dbText.Position;
                            }
                            else
                            {
                                startPt = dbText.AlignmentPoint;
                            }
                            textWidth = textContents.Length * textHeight * 0.7; // 폴백 대략 계산
                        }
                    }
                    else if (ent is MText mText)
                    {
                        mText.Rotation = 0;
                        textContents = mText.Contents;
                        textHeight = mText.TextHeight;

                        // 회전 변경사항을 그래픽스 엔진에 강제 반영하여 정확한 GeometricExtents 획득 유도
                        tr.TransactionManager.QueueForGraphicsFlush();

                        try
                        {
                            Extents3d ext = mText.GeometricExtents;
                            startPt = new Point3d(
                                (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                                (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                                (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0
                            );
                            textWidth = ext.MaxPoint.X - ext.MinPoint.X;
                        }
                        catch
                        {
                            startPt = mText.Location;
                            textWidth = textContents.Length * textHeight * 0.7; // 폴백 대략 계산
                        }
                    }

                    // 텍스트 회전 변화를 화면에 플러시하여 반영
                    tr.TransactionManager.QueueForGraphicsFlush();

                    // 3. 지시선의 반대쪽 끝점 지정 프롬프트 (VBA PickPoint 일치)
                    PromptPointOptions ppo = new PromptPointOptions("\n지시선 끝점을 지정하세요: ");
                    ppo.UseBasePoint = true;
                    ppo.BasePoint = startPt;

                    PromptPointResult ppr = ed.GetPoint(ppo);
                    if (ppr.Status != PromptStatus.OK)
                    {
                        ent.Unhighlight();
                        // 트랜잭션을 어보트(Abort)하여 텍스트 회전 상태를 원복
                        tr.Abort();
                        ed.WriteMessage("\n[취소됨] 지시선 끝점이 지정되지 않았습니다.\n");
                        return;
                    }

                    Point3d endPt = ppr.Value;

                    // 객체 강조 해제
                    ent.Unhighlight();

                    // 4. 기존 텍스트 삭제 (VBA txtEnt.Delete 일치)
                    ent.Erase(true);

                    // 5. 다중 지시선(MLeader) 신규 생성 (VBA DrawMLeader 일치)
                    MLeader mld = new MLeader();
                    mld.SetDatabaseDefaults();
                    mld.ContentType = ContentType.MTextContent;
                    mld.Layer = layerName;

                    // 사용자 요청에 따른 지시선 형태 수정 (화살촉: 2, 지시선 길이: 2, 자리맞춤: 중간)
                    mld.ArrowSize = 2.0;            // 1. 화살촉의 크기: 2
                    mld.DoglegLength = 2.0;         // 2. 지시선(Dogleg/Landing)의 길이: 2
                    mld.EnableDogleg = true;
                    mld.TextAlignmentType = TextAlignmentType.CenterAlignment;

                    // MLeader 내부에 들어갈 MText 설정 구성
                    MText mLeaderText = new MText();
                    mLeaderText.SetDatabaseDefaults();
                    mLeaderText.Contents = textContents;
                    mLeaderText.TextHeight = textHeight;
                    mLeaderText.Layer = layerName;
                    mLeaderText.Attachment = AttachmentPoint.MiddleCenter; // 3. 문자 자리맞추기: 중간
                    
                    mld.MText = mLeaderText;

                    // 지시선 라인 추가 (startPt를 화살표 팁, endPt를 랜딩 기점으로 설정)
                    int leaderIndex = mld.AddLeader();
                    int lineIndex = mld.AddLeaderLine(leaderIndex);
                    
                    mld.AddFirstVertex(lineIndex, startPt);
                    mld.AddLastVertex(lineIndex, endPt);

                    // 랜딩(Dogleg) 방향을 항상 완벽한 수평(Horizontal)으로 지정 (그림의 빨간색 지시선 구현)
                    Vector3d landingDir = Vector3d.XAxis; // 기본 우측 수평
                    if (endPt.X < startPt.X)
                    {
                        landingDir = -Vector3d.XAxis; // 좌측 수평
                    }
                    
                    mld.SetDogleg(leaderIndex, landingDir);

                    // 텍스트 위치 설정 (사선 형태가 아닌 수평 dogleg가 나타나도록 텍스트 위치를 랜딩 끝점으로 이동)
                    // 1) X축 보정: 자리맞춤이 중간(MiddleCenter)이므로 텍스트 가로 길이의 절반(textWidth / 2.0)을 X축 오프셋에 합산하여 랜딩선과 텍스트가 겹치지 않게 합니다.
                    // 2) Y축 보정: AutoCAD MLeader 내부 기하 렌더링 시 endPt의 Y좌표가 클릭점보다 텍스트 높이의 절반만큼 아래로 강제 시프트되는 현상을 상쇄하기 위해 Y축에 (textHeight / 2.0)을 더해줍니다.
                    double doglegLength = mld.DoglegLength;
                    double landingGap = mld.LandingGap;
                    Vector3d xOffset = landingDir * (doglegLength + landingGap + (textWidth / 2.0));
                    Vector3d yOffset = Vector3d.YAxis * (textHeight / 2.0);
                    mld.TextLocation = endPt + xOffset + yOffset;

                    // 현재 스페이스(ModelSpace 또는 Active Layout)에 추가
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    btr.AppendEntity(mld);
                    tr.AddNewlyCreatedDBObject(mld, true);

                    // 모든 작업 성공 시 Commit하여 영구 반영 및 Undo 등록
                    tr.Commit();
                    ed.WriteMessage("\n[성공] 텍스트가 지시선(MLeader) 객체로 정상 변환되었습니다.\n");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] 변환 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
            finally
            {
                // 6. 스냅 설정(OSMODE) 원상태 복구 (VBA Sys_RestoreOSMODE 일치)
                Application.SetSystemVariable("OSMODE", oldOsMode);
            }
        }
    }
}
