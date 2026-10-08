using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Routing;
using AudioSwitch.Core.Sonar;

namespace AudioSwitch.App;

/// <summary>
/// "X connected — switch to it?" Pre-filled from the rules; the user can untick parts or pick another mic.
/// Closes as declined when the countdown runs out (it never pauses, by request).
/// </summary>
sealed class SwitchPromptForm : Form
{
    readonly CheckBox _windowsDefault;
    readonly Dictionary<SonarChannel, CheckBox> _channels = [];
    readonly ComboBox _mic;
    readonly Button _decline;
    readonly System.Windows.Forms.Timer _countdown = new() { Interval = 1000 };
    int _secondsLeft;

    public SwitchPromptForm(PromptRequest request, int timeoutSeconds, Icon icon)
    {
        Request = request;
        _secondsLeft = timeoutSeconds;
        var decision = request.Decision;
        var plan = decision.Plan;

        Text = "AudioSwitch";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(10);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label
        {
            Text = char.ToUpper(decision.Reason[0]) + decision.Reason[1..],
            Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
            AutoSize = true,
            MaximumSize = new Size(420, 0),
        });
        layout.Controls.Add(new Label { Text = "Switch audio to this device?", AutoSize = true, Margin = new Padding(3, 4, 3, 8) });

        _windowsDefault = new CheckBox
        {
            Text = plan.WindowsDefaults is { } defaults ? $"Windows default → {defaults}" : "Windows default output",
            Checked = plan.WindowsDefaults is not null,
            Enabled = plan.WindowsDefaults is not null,
            AutoSize = true,
        };
        layout.Controls.Add(_windowsDefault);

        if (!plan.WindowsOnly) // without Sonar there are no channels to choose
        {
            var channelRow = Row();
            channelRow.Controls.Add(new Label { Text = "Sonar:", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
            foreach (var channel in SonarChannels.OutputSetOf(plan.Channels)) // four channels in classic mode, one mix in stream mode
            {
                var box = new CheckBox { Text = SonarChannels.DisplayName(channel), Checked = plan.Channels.Contains(channel), AutoSize = true };
                _channels[channel] = box;
                channelRow.Controls.Add(box);
            }
            layout.Controls.Add(channelRow);
        }

        // With Sonar this is Sonar's mic; in Windows-only mode the Windows default recording device.
        var micRow = Row();
        micRow.Controls.Add(new Label { Text = plan.WindowsOnly ? "Windows mic:" : "Mic:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        _mic = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        var choices = new[] { new MicChoice(null) }.Concat(request.AvailableMics.Select(m => new MicChoice(m))).ToArray();
        _mic.Items.AddRange(choices);
        _mic.SelectedItem = choices.FirstOrDefault(c => c.Mic?.Id == plan.Mic?.Id) ?? choices[0];
        micRow.Controls.Add(_mic);
        layout.Controls.Add(micRow);

        if (decision.DisconnectWhenDeclined)
            layout.Controls.Add(new Label
            {
                Text = "If you don't switch, it is disconnected from this PC.",
                ForeColor = SystemColors.GrayText,
                AutoSize = true,
                Margin = new Padding(3, 6, 3, 0),
            });

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        _decline = new Button { AutoSize = true };
        var accept = new Button { Text = "Switch", AutoSize = true };
        accept.Click += (_, _) => Accept();
        _decline.Click += (_, _) => Close();
        buttons.Controls.Add(_decline);
        buttons.Controls.Add(accept);
        layout.Controls.Add(buttons);
        AcceptButton = accept;
        CancelButton = _decline;

        Controls.Add(layout);
        UpdateCountdownText();
        _countdown.Tick += (_, _) => Tick();
    }

    public PromptRequest Request { get; }

    /// <summary>Set when the user clicked Switch.</summary>
    public SwitchPlan? AcceptedPlan { get; private set; }

    public bool TimedOut { get; private set; }

    /// <summary>Closed because the device went away; neither accepted nor declined.</summary>
    public bool Cancelled { get; private set; }

    public void CancelPrompt()
    {
        Cancelled = true;
        Close();
    }

    protected override bool ShowWithoutActivation => true; // don't steal focus from a game

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        _countdown.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _countdown.Dispose();
        base.OnFormClosed(e);
    }

    void Tick()
    {
        if (--_secondsLeft <= 0)
        {
            TimedOut = true;
            Close();
            return;
        }
        UpdateCountdownText();
    }

    void UpdateCountdownText() =>
        _decline.Text = $"{(Request.Decision.DisconnectWhenDeclined ? "Not now (disconnect)" : "Not now")} ({_secondsLeft})";

    void Accept()
    {
        AcceptedPlan = Request.Decision.Plan with
        {
            WindowsDefaults = _windowsDefault.Checked ? Request.Decision.Plan.WindowsDefaults : null,
            Channels = _channels.Where(c => c.Value.Checked).Select(c => c.Key).ToList(),
            Mic = (_mic.SelectedItem as MicChoice)?.Mic,
        };
        Close();
    }

    static FlowLayoutPanel Row() => new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };

    sealed record MicChoice(AudioEndpoint? Mic)
    {
        public override string ToString() => Mic?.Name ?? "(leave unchanged)";
    }
}
