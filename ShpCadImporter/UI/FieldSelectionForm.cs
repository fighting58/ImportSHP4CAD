using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ShpCadImporter.UI
{
    /// <summary>
    /// SHP 파일의 DBF 헤더로부터 파싱한 속성 필드 목록을 사용자에게 체크박스 리스트로 보여주고
    /// 도면에 표출할 필드들을 직접 선택할 수 있게 돕는 UI Form.
    /// </summary>
    public class FieldSelectionForm : Form
    {
        private CheckedListBox clbFields;
        private Button btnSelectAll;
        private Button btnDeselectAll;
        private Button btnOK;
        private Button btnCancel;
        
        public List<string> SelectedFields { get; private set; }

        public FieldSelectionForm(List<string> availableFields)
        {
            SelectedFields = new List<string>();
            InitializeComponent(availableFields);
        }

        private void InitializeComponent(List<string> availableFields)
        {
            this.Text = "SHP 속성 필드 선택";
            this.Size = new Size(350, 460);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowIcon = false;

            // 폰트 설정
            Font systemFont = new Font("Malgun Gothic", 9.0F, FontStyle.Regular);
            this.Font = systemFont;

            // 상단 안내 라벨
            Label lblInfo = new Label();
            lblInfo.Text = "도면에 표출할 속성 필드를 선택하세요.\n(각 필드는 '필드명_DATA' 레이어로 자동 분류 생성)";
            lblInfo.Location = new Point(15, 15);
            lblInfo.Size = new Size(310, 35);

            // CheckedListBox (체크 리스트 박스)
            clbFields = new CheckedListBox();
            clbFields.Location = new Point(15, 60);
            clbFields.Size = new Size(300, 260);
            clbFields.CheckOnClick = true;

            // 기본 추천 필드 (MNUM, ALIAS, NTFDATE)는 파일 내에 존재할 시 사전 체크
            var defaultFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MNUM", "ALIAS", "NTFDATE" };

            foreach (var field in availableFields)
            {
                bool isChecked = defaultFields.Contains(field);
                clbFields.Items.Add(field, isChecked);
            }

            // 전체 선택 버튼
            btnSelectAll = new Button();
            btnSelectAll.Text = "전체 선택";
            btnSelectAll.Location = new Point(15, 330);
            btnSelectAll.Size = new Size(95, 28);
            btnSelectAll.Click += BtnSelectAll_Click;

            // 전체 해제 버튼
            btnDeselectAll = new Button();
            btnDeselectAll.Text = "전체 해제";
            btnDeselectAll.Location = new Point(120, 330);
            btnDeselectAll.Size = new Size(95, 28);
            btnDeselectAll.Click += BtnDeselectAll_Click;

            // 확인 버튼
            btnOK = new Button();
            btnOK.Text = "확인 (Import)";
            btnOK.Location = new Point(120, 375);
            btnOK.Size = new Size(100, 32);
            btnOK.DialogResult = DialogResult.OK;
            btnOK.Click += BtnOK_Click;

            // 취소 버튼
            btnCancel = new Button();
            btnCancel.Text = "취소";
            btnCancel.Location = new Point(225, 375);
            btnCancel.Size = new Size(90, 32);
            btnCancel.DialogResult = DialogResult.Cancel;

            // 컨트롤 폼에 추가
            this.Controls.Add(lblInfo);
            this.Controls.Add(clbFields);
            this.Controls.Add(btnSelectAll);
            this.Controls.Add(btnDeselectAll);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }

        private void BtnSelectAll_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < clbFields.Items.Count; i++)
            {
                clbFields.SetItemChecked(i, true);
            }
        }

        private void BtnDeselectAll_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < clbFields.Items.Count; i++)
            {
                clbFields.SetItemChecked(i, false);
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            SelectedFields.Clear();
            foreach (var item in clbFields.CheckedItems)
            {
                SelectedFields.Add(item.ToString());
            }
            this.Close();
        }
    }
}
