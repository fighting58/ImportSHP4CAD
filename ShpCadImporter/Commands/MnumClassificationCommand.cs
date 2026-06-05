using System;
using System.IO;
using System.Reflection;
using System.Data.OleDb;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ShpCadImporter.Commands
{
    /// <summary>
    /// 지정한 레이어의 MNUM 텍스트를 분류하여, 파싱 결과와 MDB(Access DB) 연동 명칭을 
    /// 지정된 스택 형태 레이어("72", "73", "74", "76")에 수직 배치하는 AutoCAD 커맨드 클래스.
    /// VBA의 MNUMClassification 매크로 기능을 C# .NET API로 변환하였습니다.
    /// </summary>
    public class MnumClassificationCommand
    {
        // AutoCAD 예약 단축어와 겹치지 않는 3~4글자 추천 단축 명령어:
        // MNC (MNUM Classification)
        [CommandMethod("MNC")]
        public void MnumClassification()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. MNUM값 레이어명 입력 요청 (VBA AskString 매핑)
            PromptStringOptions pso = new PromptStringOptions("\nMNUM값 레이어명 입력: ");
            pso.AllowSpaces = true;

            PromptResult pr = ed.GetString(pso);
            if (pr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[취소됨] 레이어 입력이 취소되었습니다.\n");
                return;
            }

            string layerName = pr.StringResult.Trim();
            if (string.IsNullOrEmpty(layerName))
            {
                ed.WriteMessage("\n[취소됨] 입력한 레이어 이름이 비어있습니다.\n");
                return;
            }

            try
            {
                // 2. 트랜잭션 시작 (Undo 유닛 바인딩)
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 대상 MNUM 스택용 레이어("72", "73", "74", "76") 사전 확보 (VBA EnsureMNUMLayers 매핑)
                    EnsureMnumLayers(db, tr);

                    // 3. 해당 레이어상의 TEXT / MTEXT 객체 전체 필터링 선택 (VBA SelectAllOnLayer 매핑)
                    TypedValue[] tv = new TypedValue[]
                    {
                        new TypedValue((int)DxfCode.Operator, "<and"),
                        new TypedValue((int)DxfCode.LayerName, layerName),
                        new TypedValue((int)DxfCode.Operator, "<or"),
                        new TypedValue((int)DxfCode.Start, "TEXT"),
                        new TypedValue((int)DxfCode.Start, "MTEXT"),
                        new TypedValue((int)DxfCode.Operator, "or>"),
                        new TypedValue((int)DxfCode.Operator, "and>")
                    };
                    SelectionFilter filter = new SelectionFilter(tv);

                    PromptSelectionResult psr = ed.SelectAll(filter);
                    if (psr.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage(string.Format("\n[안내] 레이어 '{0}'에서 텍스트 객체를 찾을 수 없습니다.\n", layerName));
                        return;
                    }

                    SelectionSet ss = psr.Value;
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    int processCount = 0;

                    // 4. 선택 객체 루프 순회
                    foreach (SelectedObject so in ss)
                    {
                        if (so == null) continue;

                        Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        string textString = string.Empty;
                        Point3d basePt = Point3d.Origin;
                        double rotation = 0;

                        // 문자 정보 분석
                        DBText dbText = ent as DBText;
                        MText mText;
                        if (dbText != null)
                        {
                            textString = dbText.TextString;
                            rotation = dbText.Rotation;

                            // 정렬 기준점 획득 (VBA의 TextAlignmentPoint / insertionPoint 대체 로직)
                            if (dbText.Justify == AttachmentPoint.BaseLeft)
                            {
                                basePt = dbText.Position;
                            }
                            else
                            {
                                basePt = dbText.AlignmentPoint;
                            }
                        }
                        else if ((mText = ent as MText) != null)
                        {
                            textString = mText.Text; // 포맷 제어 텍스트 획득
                            rotation = mText.Rotation;
                            basePt = mText.Location;
                        }
                        else
                        {
                            continue;
                        }

                        // 5. MNUM 유효성 검사 (VBA IsValidMNUM 매핑)
                        if (IsValidMnum(textString))
                        {
                            // MNUM 데이터 파싱 및 Access MDB 매핑 검색 (VBA ParseMNUM 매핑)
                            string[] parts = ParseMnum(textString, ed);

                            // 6. 스택 텍스트 도면에 신규 생성 (VBA AddStackedText 매핑)
                            AddStackedText(parts, basePt, 0.2, rotation, 72, btr, tr);
                            processCount++;
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage(string.Format("\n[완료] 총 {0}개의 MNUM 텍스트를 분류하여 도면에 스택 형태로 배치하였습니다.\n", processCount));
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[오류 발생] MNUM 분류 중 예외 발생: {0}\n{1}\n", ex.Message, ex.StackTrace));
            }
        }

        /// <summary>
        /// MNUM 문자열의 길이가 트림 후 33글자인지 확인합니다.
        /// </summary>
        private static bool IsValidMnum(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.Trim().Length == 33;
        }

        /// <summary>
        /// MNUM 문자열을 규칙에 따라 파싱하고 용도지역코드명을 MDB로부터 조회하여 가져옵니다. (1-based Substring 변환 준수)
        /// </summary>
        private static string[] ParseMnum(string mnum, Editor ed)
        {
            string[] parts = new string[5];
            mnum = mnum.Trim();

            if (mnum.Length < 33) return parts;

            // VBA Mid$ 매핑 (C# 0-based Index 보정)
            parts[0] = mnum.Substring(0, 12);  // VBA: Mid$(mnum, 1, 12) (지정권자)
            parts[4] = mnum.Substring(12, 8);  // VBA: Mid$(mnum, 13, 8) (고시번호)
            parts[2] = mnum.Substring(26);     // VBA: Mid$(mnum, 27)    (관리번호)

            // 구분 코드 데이터 조회 및 연동 (VBA: valueFromMDB(Mid$(mnum, 21, 6)))
            string gubun = mnum.Substring(20, 6);
            parts[1] = ValueFromMdb(gubun, ed);

            // parts[3] 은 미할당 상태로 유지 (VBA StackedText 예외처리 반영)
            return parts;
        }

        /// <summary>
        /// 용도지역지구_분류.mdb 데이터베이스로부터 코드를 바탕으로 코드명을 획득합니다. (64비트 호환성 완벽 확보)
        /// </summary>
        private static string ValueFromMdb(string fieldValue, Editor ed)
        {
            string ret = string.Empty;

            // MDB 파일 위치 추적 (libs 폴더 내부의 용도지역지구_분류.mdb를 지정)
            string dllPath = Assembly.GetExecutingAssembly().Location;
            string dir = Path.GetDirectoryName(dllPath);

            string mdbPath = Path.Combine(dir, "libs", "용도지역지구_분류.mdb");
            if (!File.Exists(mdbPath)) mdbPath = Path.Combine(dir, "용도지역지구_분류.mdb"); // 배포 시 같은 폴더에 위치할 때
            if (!File.Exists(mdbPath)) mdbPath = Path.Combine(Path.GetDirectoryName(dir), "libs", "용도지역지구_분류.mdb");
            if (!File.Exists(mdbPath)) mdbPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(dir)), "libs", "용도지역지구_분류.mdb");
            if (!File.Exists(mdbPath)) mdbPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dir))), "libs", "용도지역지구_분류.mdb");
            if (!File.Exists(mdbPath)) mdbPath = @"c:\Users\Kim\Documents\AntiGravity\Import_SHP\libs\용도지역지구_분류.mdb";

            if (!File.Exists(mdbPath))
            {
                ed.WriteMessage(string.Format("\n[경고] 용도지역지구_분류.mdb 데이터베이스 파일을 찾을 수 없습니다. 경로: {0}\n", mdbPath));
                return ret;
            }

            // 64비트 AutoCAD 런타임 호환을 위해 ACE.OLEDB 12.0 또는 16.0 프로바이더 순차적 구동
            string[] providers = { "Microsoft.ACE.OLEDB.12.0", "Microsoft.ACE.OLEDB.16.0" };
            bool success = false;

            foreach (var provider in providers)
            {
                string connStr = string.Format("Provider={0};Data Source={1};", provider, mdbPath);
                try
                {
                    using (OleDbConnection conn = new OleDbConnection(connStr))
                    {
                        conn.Open();
                        string sql = "SELECT CODENAME FROM 용도지역지구_분류 WHERE CODE = @code";
                        using (OleDbCommand cmd = new OleDbCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@code", fieldValue);
                            object val = cmd.ExecuteScalar();
                            if (val != null)
                            {
                                ret = val.ToString().Trim();
                            }
                        }
                    }
                    success = true;
                    break;
                }
                catch
                {
                    // 다음 프로바이더(16.0) 재시도
                }
            }

            if (!success)
            {
                ed.WriteMessage("\n[오류] 64비트 Microsoft Access Database Engine (OLEDB) 드라이버가 로컬 컴퓨터에 설치되어 있지 않습니다.\n(Microsoft.ACE.OLEDB.12.0 또는 Microsoft.ACE.OLEDB.16.0 필요)\n");
            }

            return ret;
        }

        /// <summary>
        /// MNUM 분류 결과를 도면에 스택 행으로 정렬 배치합니다. (i == 3 생략 및 오프셋 수식 반영)
        /// </summary>
        private static void AddStackedText(string[] values, Point3d basePt, double h, double rot, int baseLayer, BlockTableRecord btr, Transaction tr)
        {
            for (int i = 0; i < values.Length; i++)
            {
                // i == 3 인 항목은 스키마 구조상 건너뜀 (VBA: If i <> 3 Then 일치)
                if (i != 3)
                {
                    // 수직 쌓기 Y좌표 오프셋 공식: basePt.Y - (i - 2) * h
                    double x = basePt.X;
                    double y = basePt.Y - ((i - 2) * h);
                    double z = basePt.Z;

                    Point3d pt = new Point3d(x, y, z);
                    string targetLayer = (baseLayer + i).ToString();

                    AddCenteredText(values[i], pt, h, rot, targetLayer, btr, tr);
                }
            }
        }

        /// <summary>
        /// MiddleCenter 기준 문자열 객체를 도면 공간에 생성합니다. (VBA AddCenteredText 매핑)
        /// </summary>
        private static void AddCenteredText(string txt, Point3d insPt, double h, double rot, string layerName, BlockTableRecord btr, Transaction tr)
        {
            if (string.IsNullOrEmpty(txt)) return;

            DBText text = new DBText();
            text.SetDatabaseDefaults();
            text.Position = insPt;
            text.Height = h;
            text.TextString = txt;
            text.HorizontalMode = TextHorizontalMode.TextCenter;
            text.VerticalMode = TextVerticalMode.TextVerticalMid;
            text.AlignmentPoint = insPt; // 정렬 중심 활성화를 위한 필수 대입
            text.Rotation = rot;
            text.Layer = layerName;
            
            // 한글 깨짐 방지를 위해 "SHP_한글" 텍스트 스타일 적용
            text.TextStyleId = CAD.CadEntityBuilder.GetOrCreateKoreanTextStyle(btr.Database, tr);

            btr.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
        }

        /// <summary>
        /// MNUM 저장용 대상 레이어("72", "73", "74", "76")의 생성을 확보합니다. (VBA EnsureMNUMLayers 매핑)
        /// </summary>
        private static void EnsureMnumLayers(Database db, Transaction tr)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            string[] layersToCreate = { "72", "73", "74", "76" };

            foreach (string layerName in layersToCreate)
            {
                if (!lt.Has(layerName))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord ltr = new LayerTableRecord();
                    ltr.Name = layerName;
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
            }
        }
    }
}
