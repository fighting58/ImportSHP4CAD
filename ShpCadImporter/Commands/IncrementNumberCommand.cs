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
        private static string _incnumPostfix = "";

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

                double size = _incnumSize;
                string prefix = _incnumPrefix;
                string postfix = _incnumPostfix;

                // 1. 설정 다이얼로그 대체용 대화식 키워드 루프 (Size/Prefix/Postfix)
                bool continueSettings = true;
                while (continueSettings)
                {
                    PromptKeywordOptions pko = new PromptKeywordOptions(
                        string.Format("\n[설정 현황 - 크기: {0:F2}, 접두어: \"{1}\", 접미어: \"{2}\"]\n옵션을 선택하세요 [크기(Size)/접두어(Prefix)/접미어(pOstfix)] (종료하고 계속하려면 Enter): ", 
                        size, prefix, postfix)
                    );
                    pko.Keywords.Add("Size", "S", "크기(Size)");
                    pko.Keywords.Add("Prefix", "P", "접두어(Prefix)");
                    pko.Keywords.Add("pOstfix", "O", "접미어(pOstfix)");
                    pko.AllowNone = true;

                    PromptResult pr = ed.GetKeywords(pko);
                    if (pr.Status == PromptStatus.None || pr.Status == PromptStatus.Cancel)
                    {
                        break;
                    }

                    if (pr.Status == PromptStatus.OK)
                    {
                        string key = pr.StringResult;
                        if (key == "Size")
                        {
                            PromptDoubleOptions pdo = new PromptDoubleOptions(string.Format("\n문자 높이(크기) 입력 <{0:F2}>: ", size));
                            pdo.AllowNegative = false;
                            pdo.AllowZero = false;
                            
                            PromptDoubleResult pdr = ed.GetDouble(pdo);
                            if (pdr.Status == PromptStatus.OK)
                            {
                                size = pdr.Value;
                            }
                        }
                        else if (key == "Prefix")
                        {
                            PromptStringOptions pso = new PromptStringOptions(string.Format("\n접두어 문자열 입력 (현재: \"{0}\") [Enter 입력시 변경없음]: ", prefix));
                            pso.AllowSpaces = true;
                            
                            PromptResult psr = ed.GetString(pso);
                            if (psr.Status == PromptStatus.OK)
                            {
                                prefix = psr.StringResult;
                            }
                        }
                        else if (key == "pOstfix")
                        {
                            PromptStringOptions pso = new PromptStringOptions(string.Format("\n접미어 문자열 입력 (현재: \"{0}\") [Enter 입력시 변경없음]: ", postfix));
                            pso.AllowSpaces = true;
                            
                            PromptResult psr = ed.GetString(pso);
                            if (psr.Status == PromptStatus.OK)
                            {
                                postfix = psr.StringResult;
                            }
                        }
                    }
                }

                // 다음 실행을 위해 설정 저장
                _incnumSize = size;
                _incnumPrefix = prefix;
                _incnumPostfix = postfix;

                // 2. 시작 정수번호 입력 요청 (기본값: 1)
                PromptIntegerOptions pio = new PromptIntegerOptions("\n시작할 정수번호를 입력하세요 <1>: ");
                pio.DefaultValue = 1;
                pio.UseDefaultValue = true;

                PromptIntegerResult pir = ed.GetInteger(pio);
                if (pir.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 일련번호 입력이 취소되었습니다.\n");
                    return;
                }

                int num = pir.Value;

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
                    string txtVal = prefix + num + postfix;

                    // 개별 텍스트 생성을 트랜잭션 단위로 즉시 반영하여 실시간 화면 플러시 보장
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                        DBText dbText = new DBText();
                        dbText.SetDatabaseDefaults();
                        dbText.TextString = txtVal;
                        dbText.Height = size;

                        // Standard 텍스트 스타일 획득
                        TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                        if (tst.Has("Standard"))
                        {
                            dbText.TextStyleId = tst["Standard"];
                        }
                        else
                        {
                            dbText.TextStyleId = db.Textstyle;
                        }

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

                ed.WriteMessage("\n[완료] 일련번호 연속 배치를 안전하게 종료하였습니다.\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] 일련번호 입력 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
        }
    }
}
