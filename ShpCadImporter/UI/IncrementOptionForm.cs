using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShpCadImporter.UI
{
    /// <summary>
    /// 일련번호 연속 입력(INCNUM) 명령어 실행 시 글자 크기, 접두사, 접미사, 시작 번호를 
    /// 사용자로부터 직관적으로 사전 입력받는 옵션 설정 사용자 폼.
    /// </summary>
    public class IncrementOptionForm : Form
    {
        private TextBox txtStartNum;
        private TextBox txtTextHeight;
        private TextBox txtPrefix;
        private TextBox txtPostfix;
        private Button btnOK;
        private Button btnCancel;

        public int StartNumber { get; private set; }
        public double TextHeight { get; private set; }
        public string Prefix { get; private set; }
        public string Postfix { get; private set; }

        public IncrementOptionForm(int defaultStartNum, double defaultHeight, string defaultPrefix, string defaultPostfix)
        {
            this.StartNumber = defaultStartNum;
            this.TextHeight = defaultHeight;
            this.Prefix = defaultPrefix;
            this.Postfix = defaultPostfix;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "일련번호 연속 입력 옵션 설정";
            this.Size = new Size(340, 270);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowIcon = false;

            // 폰트 설정
            Font systemFont = new Font("Malgun Gothic", 9.0F, FontStyle.Regular);
            this.Font = systemFont;

            int labelX = 20;
            int inputX = 140;
            int inputWidth = 150;
            int startY = 20;
            int gapY = 40;

            // 1. 시작 번호
            Label lblStartNum = new Label();
            lblStartNum.Text = "시작 번호:";
            lblStartNum.Location = new Point(labelX, startY);
            lblStartNum.Size = new Size(110, 20);
            lblStartNum.TextAlign = ContentAlignment.MiddleLeft;

            txtStartNum = new TextBox();
            txtStartNum.Text = StartNumber.ToString();
            txtStartNum.Location = new Point(inputX, startY);
            txtStartNum.Size = new Size(inputWidth, 20);
            txtStartNum.KeyPress += TxtNumeric_KeyPress; // 정수 필터

            // 2. 글자 크기
            Label lblHeight = new Label();
            lblHeight.Text = "글자 크기 (높이):";
            lblHeight.Location = new Point(labelX, startY + gapY);
            lblHeight.Size = new Size(110, 20);
            lblHeight.TextAlign = ContentAlignment.MiddleLeft;

            txtTextHeight = new TextBox();
            txtTextHeight.Text = TextHeight.ToString("F2");
            txtTextHeight.Location = new Point(inputX, startY + gapY);
            txtTextHeight.Size = new Size(inputWidth, 20);
            txtTextHeight.KeyPress += TxtDecimal_KeyPress; // 실수 필터

            // 3. 접두사
            Label lblPrefix = new Label();
            lblPrefix.Text = "접두사 (Prefix):";
            lblPrefix.Location = new Point(labelX, startY + gapY * 2);
            lblPrefix.Size = new Size(110, 20);
            lblPrefix.TextAlign = ContentAlignment.MiddleLeft;

            txtPrefix = new TextBox();
            txtPrefix.Text = Prefix;
            txtPrefix.Location = new Point(inputX, startY + gapY * 2);
            txtPrefix.Size = new Size(inputWidth, 20);

            // 4. 접미사
            Label lblPostfix = new Label();
            lblPostfix.Text = "접미사 (Postfix):";
            lblPostfix.Location = new Point(labelX, startY + gapY * 3);
            lblPostfix.Size = new Size(110, 20);
            lblPostfix.TextAlign = ContentAlignment.MiddleLeft;

            txtPostfix = new TextBox();
            txtPostfix.Text = Postfix;
            txtPostfix.Location = new Point(inputX, startY + gapY * 3);
            txtPostfix.Size = new Size(inputWidth, 20);

            // 5. 확인 버튼
            btnOK = new Button();
            btnOK.Text = "확인";
            btnOK.Location = new Point(110, startY + gapY * 4 + 10);
            btnOK.Size = new Size(90, 30);
            btnOK.DialogResult = DialogResult.OK;
            btnOK.Click += BtnOK_Click;

            // 6. 취소 버튼
            btnCancel = new Button();
            btnCancel.Text = "취소";
            btnCancel.Location = new Point(210, startY + gapY * 4 + 10);
            btnCancel.Size = new Size(80, 30);
            btnCancel.DialogResult = DialogResult.Cancel;

            // 컨트롤 폼에 추가
            this.Controls.Add(lblStartNum);
            this.Controls.Add(txtStartNum);
            this.Controls.Add(lblHeight);
            this.Controls.Add(txtTextHeight);
            this.Controls.Add(lblPrefix);
            this.Controls.Add(txtPrefix);
            this.Controls.Add(lblPostfix);
            this.Controls.Add(txtPostfix);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }

        // 정수 입력 필터링 (숫자 및 백스페이스만 수용)
        private void TxtNumeric_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }

        // 실수 입력 필터링 (숫자, 소수점 하나, 백스페이스 수용)
        private void TxtDecimal_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && (e.KeyChar != '.'))
            {
                e.Handled = true;
            }

            // 소수점은 단 하나만 허용
            if ((e.KeyChar == '.') && ((sender as TextBox).Text.IndexOf('.') > -1))
            {
                e.Handled = true;
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            // 검증 및 변수 대입
            int sNum;
            if (int.TryParse(txtStartNum.Text, out sNum))
            {
                StartNumber = sNum;
            }
            else
            {
                MessageBox.Show(this, "시작 번호가 정수 형식이 아닙니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None; // 닫기 취소
                return;
            }

            double height;
            if (double.TryParse(txtTextHeight.Text, out height) && height > 0)
            {
                TextHeight = height;
            }
            else
            {
                MessageBox.Show(this, "글자 크기는 0보다 큰 숫자여야 합니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None; // 닫기 취소
                return;
            }

            Prefix = txtPrefix.Text;
            Postfix = txtPostfix.Text;

            this.Close();
        }
    }
}
