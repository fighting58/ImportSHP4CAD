using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ShpCadImporter.Commands
{
    /// <summary>
    /// 클릭한 위치에 증가하는 일련번호(접두어 + 숫자 + 접미어) 텍스트를 대화식으로 연속 입력하는 AutoCAD 커맨드 클래스.
    /// 문자 크기, 접두어, 접미어 설정을 정적으로 저장하여 재실행 시 세션을 유지하며, 
    /// LISP의 C:INCNUM 기능을 C# .NET API로 완벽하게 마이그레이션하였습니다.
    /// </summary>
    public class IncrementNumberCommand
    {
        // 도면 세션 간 설정을 유지하기 위한 정적(Static) 변수 정의 (LISP의 글로벌 변수와 동일한 역할)
        private static double _incnumSize = 1.0;
        private static string _incnumPrefix = "";
        private static string _incnumSuffix = "";
        private static int _incnumStart = 1;

        [CommandMethod("INCREMENT_NUMBER")]
        [CommandMethod("INCNUM")]
        public void RunIncrementNumber()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                ed.WriteMessage("\n[증가하는 일련번호 입력 작업을 시작합니다: INCNUM]\n");

                // UI 옵션 설정 창 호출
                double size;
                string prefix;
                string suffix;
                int num;

                using (var form = new ShpCadImporter.UI.IncrementOptionForm(_incnumStart, _incnumSize, _incnumPrefix, _incnumSuffix))
                {
                    var result = Application.ShowModalDialog(Application.MainWindow.Handle, form);
                    if (result != System.Windows.Forms.DialogResult.OK)
                    {
                        ed.WriteMessage("\n[취소됨] 일련번호 입력이 취소되었습니다.\n");
                        return;
                    }

                    // 설정 업데이트 및 세션 유지
                    _incnumSize = form.TextHeight;
                    _incnumPrefix = form.Prefix;
                    _incnumSuffix = form.Suffix;
                    _incnumStart = form.StartNumber;

                    size = _incnumSize;
                    prefix = _incnumPrefix;
                    suffix = _incnumSuffix;
                    num = _incnumStart;
                }

                // 3. 대화형 지점 클릭 및 일련번호 배치 루프
                PromptPointOptions ppo = new PromptPointOptions("\n번호를 배치할 지점을 클릭하세요 (종료하려면 Enter 또는 Esc): ");
                ppo.AllowNone = true;

                ed.WriteMessage("\n배치 루프를 가동합니다. 마우스 왼쪽 버튼으로 도면을 연속해서 클릭하세요.");

                while (true)
                {
                    PromptPointResult ppr = ed.GetPoint(ppo);
                    if (ppr.Status == PromptStatus.None) // Enter 누름
                    {
                        break;
                    }
                    if (ppr.Status != PromptStatus.OK)
                    {
                        break;
                    }

                    Point3d pt = ppr.Value;
                    string txtVal = prefix + num + suffix;

                    // 개별 텍스트 생성을 트랜잭션 단위로 즉시 반영하여 실시간 화면 플러시 보장
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                        DBText dbText = new DBText();
                        dbText.SetDatabaseDefaults();
                        dbText.TextString = txtVal;
                        dbText.Height = size;

                        // 한글 깨짐 방지를 위해 "SHP_한글" 텍스트 스타일 적용
                        dbText.TextStyleId = ShpCadImporter.CAD.CadEntityBuilder.GetOrCreateKoreanTextStyle(db, tr);

                        // 가로 1(Center), 세로 2(Middle) => MiddleCenter (중앙 정렬) 설정
                        dbText.Justify = AttachmentPoint.MiddleCenter;
                        dbText.AlignmentPoint = pt;
                        dbText.Position = pt;

                        btr.AppendEntity(dbText);
                        tr.AddNewlyCreatedDBObject(dbText, true);

                        tr.Commit();
                    }

                    ed.WriteMessage(string.Format("\n배치완료: \"{0}\" (다음 정수: {1})", txtVal, num + 1));
                    num++;

                    doc.TransactionManager.QueueForGraphicsFlush();
                    ed.UpdateScreen();
                }

                // 다음 실행 시 바로 이어서 입력될 수 있도록 기본 시작 번호를 갱신
                _incnumStart = num;

                ed.WriteMessage("\n[완료] 일련번호 연속 배치를 안전하게 종료하였습니다.\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] 일련번호 입력 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
        }
    }
}
