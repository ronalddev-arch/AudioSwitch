using System.Runtime.InteropServices;
using AudioSwitch.Core.Sonar;

namespace AudioSwitch.App;

/// <summary>Version, authors, the SteelSeries versions this build was verified with versus what is installed, and
/// updates.</summary>
sealed class AboutForm : Form
{
    readonly string _details;
    readonly UpdateChecker _updates;
    readonly Action _installUpdate;
    readonly Label _updateStatus;
    readonly Button _updateButton;

    /// <param name="installUpdate">Installs the available update (the app exits and restarts).</param>
    public AboutForm(Icon windowIcon, UpdateChecker updates, Action installUpdate)
    {
        _updates = updates;
        _installUpdate = installUpdate;
        var verified = AppInfo.VerifiedSteelSeries;
        var installed = SteelSeriesVersions.DetectInstalled();
        var authors = AppInfo.Authors is { Count: > 0 } a ? string.Join(", ", a) : "unknown";
        // For bug reports: everything someone needs to know about this install, as plain text.
        _details = string.Join(Environment.NewLine,
            $"{AppInfo.Product} {AppInfo.FullVersion}",
            $"Verified with {verified}",
            $"Installed {installed}",
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription);

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"About {AppInfo.Product}";
        Icon = windowIcon;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var logo = AppInfo.LoadIcon(48);
        layout.Controls.Add(new PictureBox
        {
            Image = logo?.ToBitmap(),
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 12, 0),
        });
        logo?.Dispose();

        var heading = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        heading.Controls.Add(new Label { Text = AppInfo.Product, Font = new Font(Font.FontFamily, 14f, FontStyle.Bold), AutoSize = true });
        heading.Controls.Add(new Label { Text = $"Version {AppInfo.Version}", AutoSize = true });
        if (AppInfo.Description is { Length: > 0 } description)
            heading.Controls.Add(new Label { Text = description, AutoSize = true, MaximumSize = new Size(380, 0), Margin = new Padding(3, 6, 3, 0) });
        layout.Controls.Add(heading);

        var facts = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 14, 0, 0) };
        void Fact(string name, string value, Color? color = null)
        {
            facts.Controls.Add(new Label { Text = name, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 3, 12, 3) });
            facts.Controls.Add(new Label { Text = value, AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = color ?? SystemColors.ControlText });
        }
        Fact(AppInfo.Authors.Count == 1 ? "Author" : "Authors", authors);
        Fact("Verified with", verified.ToString());
        Fact("Installed", installed.ToString());
        if (installed.DiffersFrom(verified))
            Fact("", "Differs from the verified versions: if switching stops working, Sonar's unofficial API may have changed.", Color.DarkOrange);
        Fact("Runtime", RuntimeInformation.FrameworkDescription);
        Fact("Built with", "NAudio, Serilog, Velopack");
        Fact("Updates", "");
        _updateStatus = (Label)facts.Controls[^1];
        layout.Controls.Add(facts);
        layout.SetColumnSpan(facts, 2);

        var reportHint = new Label
        {
            Text = "Reporting a bug? Click Copy details and paste them into the report, with the log (Settings → Open log folder). " +
                   "The log contains the names of your audio devices; remove them first if you'd rather not share them.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 12, 3, 0),
        };
        layout.Controls.Add(reportHint);
        layout.SetColumnSpan(reportHint, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0) };
        // Shown modeless (a modal loop would disable a switch popup that appears meanwhile), so close explicitly.
        var ok = new Button { Text = "OK", AutoSize = true };
        ok.Click += (_, _) => Close();
        var copy = new Button { Text = "Copy details", AutoSize = true };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_details + Environment.NewLine + $"Updates: {_updates.StatusText}"); }
            catch (ExternalException) { } // clipboard held by another app; the user can simply retry
        };
        _updateButton = new Button { AutoSize = true };
        _updateButton.Click += (_, _) =>
        {
            if (_updates.CanInstall) _installUpdate();
            else _ = _updates.CheckAsync();
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(copy);
        buttons.Controls.Add(_updateButton);
        layout.Controls.Add(buttons);
        layout.SetColumnSpan(buttons, 2);
        AcceptButton = CancelButton = ok;

        Controls.Add(layout);
        ResumeLayout();

        ShowUpdateState();
        _updates.StateChanged += OnUpdateStateChanged;
    }

    void OnUpdateStateChanged(object? sender, EventArgs e) => ShowUpdateState();

    void ShowUpdateState()
    {
        _updateStatus.Text = _updates.StatusText;
        _updateStatus.ForeColor = _updates.State == UpdateState.Available ? Color.DarkGreen : SystemColors.ControlText;
        _updateButton.Text = _updates.CanInstall ? $"Install {_updates.AvailableVersion} (restarts)" : "Check for updates";
        _updateButton.Enabled = _updates.CanInstall || _updates.CanCheck;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _updates.StateChanged -= OnUpdateStateChanged;
        base.OnFormClosed(e);
    }
}
