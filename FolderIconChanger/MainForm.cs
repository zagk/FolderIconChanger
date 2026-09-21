using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FolderIconChanger;

public sealed class MainForm : Form
{
    private readonly TextBox _txtFolder = new();
    private readonly TextBox _txtIcon = new();
    private readonly PictureBox _picPreview = new();
    private readonly Label _lblIconState = new();
    private readonly Label _lblDropHint = new();
    private readonly Button _btnApply = new();
    private readonly Button _btnReset = new();
    private readonly CheckBox _chkContextMenu = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _lblStatus = new();

    private bool _loading = true;

    // 산출물 버전과 맞춤 (v6 → "v6", 다음 수정 시 함께 올림)
    private const string AppVersionLabel = "v7";

    public MainForm(string initialFolder = "")
    {
        Text = $"폴더 아이콘 변환 - {AppVersionLabel}";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(540, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        AllowDrop = true;

        // 실행 아이콘을 창 타이틀바에도 적용
        try
        {
            using var s = typeof(MainForm).Assembly.GetManifestResourceStream("FolderIconChanger.app.ico");
            if (s != null)
                Icon = new Icon(s);
        }
        catch { /* 실패해도 기본 아이콘 사용 */ }

        BuildUi();

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        // 우클릭 메뉴 등록 상태 반영 (이벤트 발사 방지)
        _chkContextMenu.Checked = ContextMenuManager.IsRegistered();
        _loading = false;

        // 우클릭 실행 or 드래그와 동일한 상황: 폴더 위치 표시
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
            SetFolder(initialFolder);
    }

    private void BuildUi()
    {
        int pad = 12, y = 12, w = ClientSize.Width - pad * 2;

        // 드래그 영역
        var pnlDrop = new Panel
        {
            Location = new Point(pad, y),
            Size = new Size(w, 110),
            BorderStyle = BorderStyle.FixedSingle,
            AllowDrop = true,
        };
        _lblDropHint.Text = "여기에 폴더를 드래그하세요";
        _lblDropHint.Font = new Font(Font.FontFamily, 13, FontStyle.Bold);
        _lblDropHint.ForeColor = Color.DimGray;
        _lblDropHint.TextAlign = ContentAlignment.MiddleCenter;
        _lblDropHint.Dock = DockStyle.Fill;
        _lblDropHint.AllowDrop = true;
        pnlDrop.Controls.Add(_lblDropHint);
        pnlDrop.DragEnter += OnDragEnter;
        pnlDrop.DragDrop += OnDragDrop;
        _lblDropHint.DragEnter += OnDragEnter;
        _lblDropHint.DragDrop += OnDragDrop;
        Controls.Add(pnlDrop);
        y += 122;

        // 폴더 위치
        var lblFolder = new Label { Text = "폴더 위치:", Location = new Point(pad, y), AutoSize = true };
        Controls.Add(lblFolder);
        y += 22;
        _txtFolder.Location = new Point(pad, y);
        _txtFolder.Size = new Size(w - 100, 28);
        _txtFolder.ReadOnly = true;
        _txtFolder.AllowDrop = true;
        _txtFolder.DragEnter += OnDragEnter;
        _txtFolder.DragDrop += OnDragDrop;
        Controls.Add(_txtFolder);
        var btnBrowse = new Button { Text = "찾아보기...", Location = new Point(pad + w - 90, y - 2), Size = new Size(90, 30) };
        btnBrowse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "아이콘을 변경할 폴더 선택" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                SetFolder(dlg.SelectedPath);
        };
        Controls.Add(btnBrowse);
        y += 40;

        // 아이콘 선택
        var lblIcon = new Label { Text = "폴더 아이콘 (.ico):", Location = new Point(pad, y), AutoSize = true };
        Controls.Add(lblIcon);
        y += 22;
        _txtIcon.Location = new Point(pad, y);
        _txtIcon.Size = new Size(w - 100, 28);
        _txtIcon.ReadOnly = true;
        Controls.Add(_txtIcon);
        var btnSelectIcon = new Button { Text = "아이콘 선택...", Location = new Point(pad + w - 90, y - 2), Size = new Size(90, 30) };
        btnSelectIcon.Click += OnSelectIcon;
        Controls.Add(btnSelectIcon);
        y += 40;

        // 미리보기
        var grpPreview = new GroupBox { Text = "선택한 아이콘 미리보기", Location = new Point(pad, y), Size = new Size(w, 130) };
        _picPreview.Location = new Point(16, 24);
        _picPreview.Size = new Size(96, 96);
        _picPreview.SizeMode = PictureBoxSizeMode.CenterImage;
        _picPreview.BorderStyle = BorderStyle.FixedSingle;
        grpPreview.Controls.Add(_picPreview);
        _lblIconState.Location = new Point(124, 24);
        _lblIconState.Size = new Size(w - 140, 96);
        _lblIconState.Text = "선택된 아이콘 없음";
        _lblIconState.TextAlign = ContentAlignment.MiddleLeft;
        grpPreview.Controls.Add(_lblIconState);
        Controls.Add(grpPreview);
        y += 142;

        // 적용 / 초기화
        _btnApply.Text = "적용";
        _btnApply.Font = new Font(Font.FontFamily, 11, FontStyle.Bold);
        _btnApply.Location = new Point(pad, y);
        _btnApply.Size = new Size((w - 10) / 2, 44);
        _btnApply.Click += OnApply;
        Controls.Add(_btnApply);

        _btnReset.Text = "초기화";
        _btnReset.Location = new Point(pad + (w - 10) / 2 + 10, y);
        _btnReset.Size = new Size((w - 10) / 2, 44);
        _btnReset.Click += OnReset;
        Controls.Add(_btnReset);
        y += 56;

        // 우클릭 메뉴 등록
        _chkContextMenu.Text = "윈도우 우클릭 메뉴에 등록";
        _chkContextMenu.Location = new Point(pad, y);
        _chkContextMenu.AutoSize = true;
        _chkContextMenu.CheckedChanged += OnContextMenuChecked;
        Controls.Add(_chkContextMenu);
        y += 30;

        var lblHelp = new Label
        {
            Text = "※ 적용 시 ico가 폴더 안에 복사되어 다른 PC로 이동해도 유지됩니다.",
            Location = new Point(pad, y),
            Size = new Size(w, 30),
            ForeColor = Color.DimGray,
        };
        Controls.Add(lblHelp);

        _lblStatus.Text = "준비";
        _statusStrip.Items.Add(_lblStatus);
        Controls.Add(_statusStrip);
    }

    // ---------- 드래그 & 우클릭 공통 진입점 ----------

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            var paths = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (paths is { Length: > 0 } && Directory.Exists(paths[0]))
            {
                e.Effect = DragDropEffects.Copy;
                return;
            }
        }
        e.Effect = DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        var paths = (string[]?)e.Data?.GetData(DataFormats.FileDrop);
        if (paths is { Length: > 0 } && Directory.Exists(paths[0]))
            SetFolder(paths[0]);
    }

    private void SetFolder(string folderPath)
    {
        try { folderPath = Path.GetFullPath(folderPath.Trim('"')); } catch { }
        _txtFolder.Text = folderPath;
        SetStatus($"폴더 선택됨: {folderPath}");

        // 이미 적용된 아이콘이 있으면 표시 (드래그한 것과 같은 상황)
        var applied = FolderIconManager.GetAppliedIconPath(folderPath);
        if (applied != null)
        {
            SetIcon(applied, silent: true);
            SetStatus($"폴더 선택됨 (현재 커스텀 아이콘 적용 중): {folderPath}");
        }
    }

    // ---------- 아이콘 선택 ----------

    private void OnSelectIcon(object? sender, EventArgs e)
    {
        // 실행 파일이 있는 위치의 icon 폴더를 염
        string iconDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon");
        try { Directory.CreateDirectory(iconDir); } catch { }

        using var dlg = new OpenFileDialog
        {
            Title = "폴더 아이콘 선택 (.ico)",
            Filter = "아이콘 파일 (*.ico)|*.ico",
            InitialDirectory = iconDir,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SetIcon(dlg.FileName);
    }

    private void SetIcon(string icoPath, bool silent = false)
    {
        _txtIcon.Text = icoPath;
        try
        {
            // 파일을 직접 열면 핸들이 남아 잠금 원인이 되므로 바이트로 읽어 미리보기 (파일 잠금 없음)
            byte[] bytes = File.ReadAllBytes(icoPath);
            using var ms = new MemoryStream(bytes);
            using var icon = new Icon(ms, 64, 64);
            _picPreview.Image?.Dispose();
            _picPreview.Image = icon.ToBitmap();
            _lblIconState.Text = $"선택됨:\n{icoPath}";
            if (!silent) SetStatus("아이콘 선택됨");
        }
        catch (Exception ex)
        {
            _picPreview.Image = null;
            _lblIconState.Text = "아이콘 미리보기 실패:\n" + ex.Message;
        }
    }

    // ---------- 적용 / 초기화 ----------

    private void OnApply(object? sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_txtFolder.Text) || !Directory.Exists(_txtFolder.Text))
            {
                MessageBox.Show(this, "먼저 폴더를 드래그하거나 찾아보기로 선택하세요.",
                    "폴더 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(_txtIcon.Text) || !File.Exists(_txtIcon.Text))
            {
                MessageBox.Show(this, "먼저 [아이콘 선택] 버튼으로 .ico 파일을 선택하세요.",
                    "아이콘 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 미리보기가 폴더 안 ico를 물고 있을 수 있으니 먼저 해제 (파일 잠금 방지)
            _picPreview.Image?.Dispose();
            _picPreview.Image = null;

            FolderIconManager.ApplyIcon(_txtFolder.Text, _txtIcon.Text);
            var applied = FolderIconManager.GetAppliedIconPath(_txtFolder.Text);
            if (applied != null)
                SetIcon(applied, silent: true);
            SetStatus("폴더 아이콘 변환 완료");
            MessageBox.Show(this, "폴더 아이콘 변환 완료!\n다른 컴퓨터로 이동해도 유지됩니다.",
                "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "적용 실패:\n" + ex.Message,
                "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnReset(object? sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_txtFolder.Text) || !Directory.Exists(_txtFolder.Text))
            {
                MessageBox.Show(this, "먼저 폴더를 선택하세요.",
                    "폴더 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            FolderIconManager.ResetIcon(_txtFolder.Text);
            _txtIcon.Text = "";
            _picPreview.Image?.Dispose();
            _picPreview.Image = null;
            _lblIconState.Text = "선택된 아이콘 없음 (윈도우 기본 아이콘으로 초기화됨)";
            SetStatus("윈도우 기본 아이콘으로 초기화 완료");
            MessageBox.Show(this, "윈도우 기본 아이콘으로 초기화했습니다.\n(숨겨진 ico · desktop.ini 찌꺼기 삭제됨)",
                "초기화 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "초기화 실패:\n" + ex.Message,
                "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------- 우클릭 메뉴 ----------

    private void OnContextMenuChecked(object? sender, EventArgs e)
    {
        if (_loading) return;
        try
        {
            if (_chkContextMenu.Checked)
            {
                ContextMenuManager.Register();
                SetStatus("우클릭 메뉴에 등록됨");
            }
            else
            {
                ContextMenuManager.Unregister();
                SetStatus("우클릭 메뉴에서 제거됨");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "우클릭 메뉴 변경 실패:\n" + ex.Message,
                "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _loading = true;
            _chkContextMenu.Checked = ContextMenuManager.IsRegistered();
            _loading = false;
        }
    }

    private void SetStatus(string msg) => _lblStatus.Text = msg;
}
