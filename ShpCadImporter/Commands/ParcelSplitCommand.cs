using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;

namespace ShpCadImporter.Commands
{
    /// <summary>
    /// 지번지목 텍스트(예: "산12-3대", "104-2임")를 지번(10 레이어), 지목(11 레이어), 대장구분(20 레이어)으로
    /// 파싱하여 비파괴 방식으로 분할 복제해 주는 AutoCAD 커맨드 클래스.
    /// 기존 LISP의 C:PARCEL_SPLIT 기능을 C# .NET API로 마이그레이션 하였으며, 단축명령어는 PNC로 지정합니다.
    /// </summary>
    public class ParcelSplitCommand
    {
        // 28가지 지적법 표준 지목 부호 리스트 (해시셋을 이용해 빠른 조회 지원)
        private static readonly HashSet<string> JimokList = new HashSet<string>
        {
            "전", "답", "과", "목", "임", "광", "염", "대", "장", "학", "차", "주", "창", "도", "철", "제", "천", "구", "유", "양", "수", "공", "체", "원", "종", "사", "묘", "잡"
        };

        [CommandMethod("PARCEL_SPLIT")]
        [CommandMethod("PNC")]
        public void RunParcelSplit()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                ed.WriteMessage("\n[지번/지목/대장구분 텍스트 분할 작업을 시작합니다: PARCEL_SPLIT/PNC]\n");

                // 1. 대상 레이어 입력 요청 (기본값: 19)
                PromptStringOptions pso = new PromptStringOptions("\n대상 레이어 이름 입력 (기본값: 19): ");
                pso.AllowSpaces = false;
                pso.DefaultValue = "19";
                pso.UseDefaultValue = true;

                PromptResult pr = ed.GetString(pso);
                if (pr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n[취소됨] 지번 분할 명령이 취소되었습니다.\n");
                    return;
                }

                string sourceLayer = pr.StringResult.Trim();

                // 2. 트랜잭션 기동 및 타겟 레이어 확보
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 레이어 생성 (10: 하늘색, 11: 하늘색, 20: 초록색)
                    EnsureLayer(db, tr, "10", 4); // Cyan
                    EnsureLayer(db, tr, "11", 4); // Cyan
                    EnsureLayer(db, tr, "20", 3); // Green

                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    // 3. 대상 레이어 상의 모든 TEXT, MTEXT 자동 선택 (ssget "X" 대응)
                    TypedValue[] tv = new TypedValue[]
                    {
                        new TypedValue((int)DxfCode.Operator, "<and"),
                        new TypedValue((int)DxfCode.Operator, "<or"),
                        new TypedValue((int)DxfCode.Start, "TEXT"),
                        new TypedValue((int)DxfCode.Start, "MTEXT"),
                        new TypedValue((int)DxfCode.Operator, "or>"),
                        new TypedValue((int)DxfCode.LayerName, sourceLayer),
                        new TypedValue((int)DxfCode.Operator, "and>")
                    };
                    SelectionFilter filter = new SelectionFilter(tv);

                    PromptSelectionResult psr = ed.SelectAll(filter);
                    if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
                    {
                        ed.WriteMessage(string.Format("\n에러: '{0}' 레이어에 텍스트 객체가 없습니다.\n", sourceLayer));
                        return;
                    }

                    SelectionSet ss = psr.Value;
                    int processedCount = 0;

                    // 4. 순회 분석 및 분할 복제 생성
                    foreach (SelectedObject so in ss)
                    {
                        if (so == null) continue;

                        Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        string rawText = "";
                        DBText dbText = ent as DBText;
                        if (dbText != null)
                        {
                            rawText = dbText.TextString;
                        }
                        else
                        {
                            MText mText = ent as MText;
                            if (mText != null)
                            {
                                rawText = mText.Contents;
                            }
                            else
                            {
                                continue;
                            }
                        }

                        // 지번지목 문자열 분석
                        string[] parsed = ParseParcelString(rawText);
                        string jibeon = parsed[0];
                        string jimok = parsed[1];
                        string reg = parsed[2];

                        // 각 레이어별로 비파괴 복제본 신설 등록
                        CreateTextClone(ent, btr, tr, "10", jibeon);
                        CreateTextClone(ent, btr, tr, "11", jimok);
                        CreateTextClone(ent, btr, tr, "20", reg);

                        processedCount++;
                    }

                    tr.Commit();
                    ed.WriteMessage(string.Format("\n[완료] 총 {0}개 지번 문자열을 분할하여 복제 생성하였습니다.\n", processedCount));
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] 지번 분할 실패: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
        }

        /// <summary>
        /// 지번 문자열을 공백 제거 후 지번, 지목, 대장구분으로 분할 파싱합니다.
        /// </summary>
        private static string[] ParseParcelString(string str)
        {
            // 모든 종류의 공백 문자 제거 (스페이스, 탭, 줄바꿈)
            str = str.Replace(" ", "").Replace("\t", "").Replace("\n", "").Replace("\r", "");

            string resReg = "1";
            string resJimok = "";
            string resJibeon = "";

            // A. 대장 구분 (산* 일 경우 임야대장 2번, 아니면 일반대장 1번)
            if (str.StartsWith("산"))
            {
                resReg = "2";
                str = str.Substring(1); // 앞의 "산" 문자 제거
            }

            // B. 지목 구분 (끝 글자가 28가지 지목 리스트에 매칭될 경우 지목 추출)
            if (str.Length > 0)
            {
                string lastChar = str.Substring(str.Length - 1);
                if (JimokList.Contains(lastChar))
                {
                    resJimok = lastChar;
                    str = str.Substring(0, str.Length - 1); // 맨 뒤 지목 제거
                }
            }

            // C. 지번 번호 정리
            resJibeon = str.Trim();
            if (resJibeon.EndsWith("-0"))
            {
                resJibeon = resJibeon.Substring(0, resJibeon.Length - 2);
            }
            else if (resJibeon.EndsWith("-"))
            {
                resJibeon = resJibeon.Substring(0, resJibeon.Length - 1);
            }

            // 임야대장(2)인 경우 지적 표기법 일관성을 위해 지번 앞에 다시 "산" 표기를 수복합니다.
            if (resReg == "2")
            {
                resJibeon = "산" + resJibeon;
            }

            return new string[] { resJibeon, resJimok, resReg };
        }

        /// <summary>
        /// 원본 텍스트 속성을 복제하여 특정 레이어에 원하는 문구로 신설 추가합니다.
        /// </summary>
        private static void CreateTextClone(Entity original, BlockTableRecord btr, Transaction tr, string targetLayer, string textValue)
        {
            if (string.IsNullOrEmpty(textValue)) return;

            Entity clone = original.Clone() as Entity;
            if (clone == null) return;

            clone.Layer = targetLayer;

            DBText dbText = clone as DBText;
            if (dbText != null)
            {
                dbText.TextString = textValue;
            }
            else
            {
                MText mText = clone as MText;
                if (mText != null)
                {
                    mText.Contents = textValue;
                }
            }

            btr.AppendEntity(clone);
            tr.AddNewlyCreatedDBObject(clone, true);
        }

        /// <summary>
        /// 지정한 색상의 레이어를 생성하고 보장합니다.
        /// </summary>
        private static void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = layerName;
                ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);

                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
                lt.DowngradeOpen();
            }
        }
    }
}
