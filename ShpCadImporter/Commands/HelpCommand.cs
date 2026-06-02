using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;

namespace ShpCadImporter.Commands
{
    /// <summary>
    /// 플러그인 내의 모든 사용 가능한 명령어 목록을 
    /// 한글/영문 자폭을 고려한 고정폭(함수명: 20, 단축명령: 10, 설명: 100) 테이블 형식으로 
    /// AutoCAD 커맨드 창에 줄맞춤하여 출력하는 도움말 클래스.
    /// </summary>
    public class HelpCommand
    {
        [CommandMethod("APPHELP")]
        public void ShowHelp()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            ed.WriteMessage("\n\n=== [ShpCadImporter 플러그인 명령어 도움말] ===");
            
            // 헤더 및 구분선 생성
            string header = string.Format("\n|{0}|{1}|{2}|", 
                PadRightVisual("함수명", 20), 
                PadRightVisual("단축명령어", 10), 
                PadRightVisual("함수설명(간단히)", 100));
            
            string divider = string.Format("\n|{0}|{1}|{2}|", 
                new string('-', 20), 
                new string('-', 10), 
                new string('-', 100));

            ed.WriteMessage(header);
            ed.WriteMessage(divider);

            // 명령어 리스트 출력
            WriteHelpRow(ed, "IMPORT_SHP", "ISH", "공간정보 SHP 파일 가져오기 및 링/레이블 그리기");
            WriteHelpRow(ed, "TEXT2LEAD", "T2L", "문자 객체와 연계된 지시선(Leader) 생성 정렬");
            WriteHelpRow(ed, "TXTC", "없음", "텍스트(DBText/MText) 주위에 1.3배 반지름 원 생성");
            WriteHelpRow(ed, "IDXO", "없음", "폴리라인 정점 인덱스 순서 변경 및 회전/역순 재배치");
            WriteHelpRow(ed, "GAGU", "없음", "개별 기하학적 커브 조각을 묶어 독립 블록으로 구성");
            WriteHelpRow(ed, "MNC", "없음", "지적 속성에 맞춘 필지 일괄 레이어 분류 체계 설정");
            WriteHelpRow(ed, "OUTERBOUND", "OB", "선택한 복수 객체의 최외곽 경계선(실루엣) 추출");
            WriteHelpRow(ed, "REFPOLY_CW", "F2P", "참조 실루엣 구간으로 선형 교체 치환 (시계 정규화)");
            WriteHelpRow(ed, "REFPOLY_CCW", "F3P", "참조 실루엣 구간으로 선형 교체 치환 (반시계 정규화)");
            WriteHelpRow(ed, "AREA_ADJUST", "AFO", "변 이동 오프셋 연산을 통한 다각형 면적 정밀 수렴");
            WriteHelpRow(ed, "AREA_ADJUST_FIXED", "AFO2", "양 끝점 고정 상태에서 변 정밀 오프셋 면적 수렴");
            WriteHelpRow(ed, "ARCTOPOLYLINE", "A2PL", "호를 옵션(등분할/중앙종거/고정거리)별 직선 폴리라인으로 변환");
            WriteHelpRow(ed, "COORDROUND_OSA2", "OSA2", "선택 객체의 모든 좌표를 소수점 아래 2자리로 오사오입(초록색 복제)");
            WriteHelpRow(ed, "COORDROUND_OSA3", "OSA3", "선택 객체의 모든 좌표를 소수점 아래 3자리로 오사오입(초록색 복제)");
            WriteHelpRow(ed, "VERTEX_ADD", "VAD", "폴리라인/선에 대화식으로 새로운 정점 삽입 추가");
            WriteHelpRow(ed, "VERTEX_DEL", "VDE", "폴리라인 정점 중 마우스 클릭 지점과 가장 가까운 정점 제거");
            WriteHelpRow(ed, "PARCEL_SPLIT", "PNC", "지번지목 텍스트를 파싱하여 지번(10), 지목(11), 대장구분(20) 레이어로 분할 복제");
            WriteHelpRow(ed, "INCREMENT_NUMBER", "INCNUM", "클릭한 지점에 증가하는 정수 일련번호(접두어/접미어 지원) 텍스트를 대화식 연속 입력");
            WriteHelpRow(ed, "MAPINDEX_500", "WD5", "지적도곽 원점 기준 1:500 scale 도곽 격자 자동 생성 (200m * 150m)");
            WriteHelpRow(ed, "MAPINDEX_1000", "WD10", "지적도곽 원점 기준 1:1000 scale 도곽 격자 자동 생성 (400m * 300m)");
            WriteHelpRow(ed, "ODLABEL", "ODL", "폴리라인에 부착된 객체데이터(OD)를 추출하여 시각적 중심에 텍스트 생성");
            WriteHelpRow(ed, "APPHELP", "없음", "플러그인 내장 명령어 및 도움말 테이블 표시");

            ed.WriteMessage("\n=====================================================================================================================================\n");
        }

        /// <summary>
        /// 고정폭 규격에 맞추어 개별 명령어 행을 출력합니다.
        /// </summary>
        private void WriteHelpRow(Editor ed, string cmdName, string shortcut, string description)
        {
            string row = string.Format("\n|{0}|{1}|{2}|", 
                PadRightVisual(cmdName, 20), 
                PadRightVisual(shortcut, 10), 
                PadRightVisual(description, 100));
            ed.WriteMessage(row);
        }

        /// <summary>
        /// 한글(2칸)과 영문/숫자(1칸)의 시각적 너비를 고려하여 문자열의 우측 공백 패딩을 추가합니다.
        /// </summary>
        private static string PadRightVisual(string str, int totalWidth)
        {
            if (str == null) str = "";
            int currentWidth = 0;
            foreach (char c in str)
            {
                if (IsDoubleWidth(c))
                {
                    currentWidth += 2;
                }
                else
                {
                    currentWidth += 1;
                }
            }

            int paddingNeeded = totalWidth - currentWidth;
            if (paddingNeeded <= 0) return str;
            return str + new string(' ', paddingNeeded);
        }

        /// <summary>
        /// CJK 광동형 문자(한글, 한자 등) 여부를 판단하여 고정폭 글꼴에서의 2열 너비를 인식합니다.
        /// </summary>
        private static bool IsDoubleWidth(char c)
        {
            return (c >= 0xAC00 && c <= 0xD7A3) || // 완성형 한글
                   (c >= 0x1100 && c <= 0x11FF) || // 조합형 한글 자모
                   (c >= 0x3130 && c <= 0x318F) || // 호환용 한글 자모
                   (c >= 0x3000 && c <= 0x303F) || // 한중일 기호 및 문장 부호
                   (c >= 0x4E00 && c <= 0x9FFF);   // 한중일 통합 한자
        }
    }
}
