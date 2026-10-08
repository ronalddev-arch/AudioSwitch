namespace AudioSwitch.App;

/// <summary>
/// Small unintrusive acknowledgement ("Switched to SoundCore 2 — SteelSeries Arctis headset powered off"): bottom-right,
/// never takes focus, fades out after a few seconds, click to dismiss.
/// </summary>
sealed class ToastForm : Form
{
    static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(5);
    const int FadeSteps = 10;

    readonly System.Windows.Forms.Timer _timer = new();
    readonly int _bottomOffset;
    int _fadeStep;

    public ToastForm(string title, string text, Icon icon, int bottomOffset)
    {
        _bottomOffset = bottomOffset;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = SystemColors.Window;
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(1); // room for the border painted in OnPaint

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(10, 8, 14, 10) };
        layout.Controls.Add(new PictureBox { Image = icon.ToBitmap(), SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 2, 10, 0) }, 0, 0);
        layout.SetRowSpan(layout.GetControlFromPosition(0, 0)!, 2);
        layout.Controls.Add(new Label { Text = title, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), AutoSize = true, MaximumSize = new Size(360, 0) }, 1, 0);
        layout.Controls.Add(new Label { Text = text, ForeColor = SystemColors.GrayText, AutoSize = true, MaximumSize = new Size(360, 0) }, 1, 1);
        Controls.Add(layout);

        foreach (Control c in layout.Controls) c.Click += (_, _) => Close();
        layout.Click += (_, _) => Close();
        Click += (_, _) => Close();

        _timer.Interval = (int)ShownFor.TotalMilliseconds;
        _timer.Tick += (_, _) => Fade();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
            var p = base.CreateParams;
            p.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW; // never steal focus, never in Alt+Tab
            return p;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12 - _bottomOffset);
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(TrayIcons.Active);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    void Fade()
    {
        if (_fadeStep == 0) _timer.Interval = 40;
        if (++_fadeStep >= FadeSteps) { Close(); return; }
        Opacity = 1.0 - (double)_fadeStep / FadeSteps;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}
