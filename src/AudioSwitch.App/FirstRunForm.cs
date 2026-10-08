using System.Security;
using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Routing;
using AudioSwitch.Core.Settings;

namespace AudioSwitch.App;

/// <summary>
/// The first-run setup, shown once when settings.json did not exist at startup: welcome (and whether Sonar was found),
/// "Use monitor/TV speakers?" (only when one is connected), the devices to give a profile, and the Sonar (only when
/// Sonar is found or GG is starting), start-up and update options. Finish saves through the same path as the settings
/// window; it never switches audio. Closing it any other way leaves <see cref="Finished"/> false and the caller saves
/// the defaults.
/// </summary>
sealed class FirstRunForm : Form
{
    const int TextWidth = 520;

    readonly AudioSwitchSettings _defaults;
    readonly Func<AudioSwitchSettings, string?> _save;
    readonly IReadOnlyList<AudioEndpoint> _activeEndpoints;
    readonly IReadOnlyList<AudioEndpoint> _monitors;
    readonly List<(string Title, Control Content)> _pages = [];
    readonly Panel _host;
    readonly Label _step, _title;
    readonly Button _back, _next;

    // Welcome
    readonly Label _detection, _detectionHint;
    // Monitors
    readonly RadioButton _useMonitors;
    // Devices
    readonly CheckedListBox _devices;
    readonly Label _noDevices;
    readonly Button _moveUp, _moveDown;
    readonly int _devicesPage;
    bool? _devicesListedForMonitors;
    // Options
    readonly CheckBox _keepOnSonar, _startWithWindows, _checkForUpdates;
    readonly Label _keepOnSonarHint;
    bool _sonarFound;

    int _page;

    /// <summary>True once Finish saved the settings.</summary>
    public bool Finished { get; private set; }

    /// <param name="defaults">The settings to start from (the built-in defaults).</param>
    /// <param name="save">Saves and applies the settings, as for the settings window; returns an error message on failure.</param>
    /// <param name="activeEndpoints">Every active Windows endpoint.</param>
    /// <param name="status">The routing status known so far, or null; later ones arrive through <see cref="ShowStatus"/>.</param>
    public FirstRunForm(AudioSwitchSettings defaults, Func<AudioSwitchSettings, string?> save,
        IReadOnlyList<AudioEndpoint> activeEndpoints, RoutingStatus? status, Icon icon)
    {
        _defaults = defaults;
        _save = save;
        _activeEndpoints = activeEndpoints;
        _monitors = FirstRunSetup.Monitors(activeEndpoints);

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"Set up {AppInfo.Product}";
        Icon = icon;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(580, 440);
        Padding = new Padding(16, 12, 16, 12);

        // --- Header: step and page title
        _step = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Dock = DockStyle.Top };
        _title = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 13f, FontStyle.Bold), Dock = DockStyle.Top, Padding = new Padding(0, 2, 0, 10) };
        _host = new Panel { Dock = DockStyle.Fill };

        // --- Welcome
        var welcome = Page();
        var logo = AppInfo.LoadIcon(48);
        var intro = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        intro.Controls.Add(new PictureBox { Image = logo?.ToBitmap(), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 12, 0) });
        logo?.Dispose();
        intro.Controls.Add(Paragraph(
            "AudioSwitch moves your sound to the right device for you. When headphones, a headset or speakers connect, " +
            "it asks whether to switch to them; when the device you are listening on turns off or disconnects, it " +
            "switches to the next one by itself.", TextWidth - 60));
        welcome.Controls.Add(intro);
        _detection = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 10, 3, 2) };
        _detectionHint = Paragraph("", margin: new Padding(3, 2, 3, 3));
        welcome.Controls.Add(_detection);
        welcome.Controls.Add(_detectionHint);
        welcome.Controls.Add(Paragraph("The next steps take about a minute. You can change everything later: right-click the " +
            "AudioSwitch icon next to the clock and choose Settings.", margin: new Padding(3, 14, 3, 3)));
        _pages.Add(("Welcome to AudioSwitch", welcome));

        // --- Monitor/TV speakers: only when one is connected
        _useMonitors = new RadioButton { Text = "Yes, use them like any other device", AutoSize = true, Margin = new Padding(3, 2, 3, 3) };
        if (_monitors.Count > 0)
        {
            var monitorsPage = Page();
            monitorsPage.Controls.Add(Paragraph("Your PC can also play sound through these monitors or TVs:"));
            foreach (var monitor in _monitors)
                monitorsPage.Controls.Add(new Label { Text = "•  " + monitor.Name, AutoSize = true, Margin = new Padding(12, 2, 3, 2) });
            monitorsPage.Controls.Add(Paragraph("Many monitors have no speakers, so switching to one would leave you without sound. " +
                "Should AudioSwitch use them?", margin: new Padding(3, 10, 3, 8)));
            monitorsPage.Controls.Add(new RadioButton
            {
                Text = "No, never switch to monitor or TV speakers (recommended)",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(3, 2, 3, 3),
            });
            monitorsPage.Controls.Add(_useMonitors);
            monitorsPage.Controls.Add(Hint("You can change this later in Settings → General → Ignored devices.", margin: new Padding(3, 10, 3, 3)));
            _pages.Add(("Monitor and TV speakers", monitorsPage));
        }

        // --- Your devices
        var devicesPage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        devicesPage.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        devicesPage.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        devicesPage.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        devicesPage.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        devicesPage.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var devicesIntro = Paragraph("Tick the devices you listen on. When the one you are using goes away, AudioSwitch switches to " +
            "the one you used most recently, otherwise it follows this order, top first.");
        devicesPage.Controls.Add(devicesIntro, 0, 0);
        devicesPage.SetColumnSpan(devicesIntro, 2);
        _devices = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, Margin = new Padding(3, 6, 3, 3) };
        _devices.SelectedIndexChanged += (_, _) => UpdateMoveButtons();
        _noDevices = new Label
        {
            Text = "No speakers or headphones are connected right now. That's fine: AudioSwitch asks about each device when " +
                   "it connects, and you can add devices later in Settings.",
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Visible = false,
        };
        var listArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        listArea.Controls.Add(_devices);
        listArea.Controls.Add(_noDevices);
        devicesPage.Controls.Add(listArea, 0, 1);
        _moveUp = SideButton("Move up");
        _moveUp.Click += (_, _) => MoveDevice(-1);
        _moveDown = SideButton("Move down");
        _moveDown.Click += (_, _) => MoveDevice(+1);
        var side = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        side.Controls.Add(_moveUp);
        side.Controls.Add(_moveDown);
        devicesPage.Controls.Add(side, 1, 1);
        var devicesHint = Hint("Devices you leave unticked, and new ones you connect later, still work: AudioSwitch asks " +
            "\"Switch to it?\" when they connect. A ticked device also brings its own microphone along, if it has one.", margin: new Padding(3, 6, 3, 0));
        devicesPage.Controls.Add(devicesHint, 0, 2);
        devicesPage.SetColumnSpan(devicesHint, 2);
        _devicesPage = _pages.Count;
        _pages.Add(("Your devices", devicesPage));

        // --- Options
        var options = Page();
        _keepOnSonar = new CheckBox
        {
            Text = "Keep sound going through SteelSeries Sonar (recommended)",
            AutoSize = true,
            Checked = defaults.NewDevicesUseSonarDefaults,
        };
        options.Controls.Add(_keepOnSonar);
        _keepOnSonarHint = Hint("Your Sonar EQ and mixer keep working; AudioSwitch tells Sonar which device to play on.",
            margin: new Padding(21, 0, 3, 10));
        options.Controls.Add(_keepOnSonarHint);
        _startWithWindows = new CheckBox { Text = "Start AudioSwitch when Windows starts", AutoSize = true, Checked = true };
        options.Controls.Add(_startWithWindows);
        options.Controls.Add(Hint("It runs quietly in the tray, next to the clock.", margin: new Padding(21, 0, 3, 10)));
        _checkForUpdates = new CheckBox { Text = "Check for updates automatically", AutoSize = true, Checked = defaults.CheckForUpdatesAutomatically };
        options.Controls.Add(_checkForUpdates);
        options.Controls.Add(Hint("AudioSwitch tells you when a new version is out. Nothing is installed until you choose to.", margin: new Padding(21, 0, 3, 10)));
        options.Controls.Add(Paragraph("Click Finish to save. AudioSwitch doesn't change your sound now; it acts the next time a " +
            "device connects or turns off.", margin: new Padding(3, 14, 3, 3)));
        _pages.Add(("Almost done", options));

        // --- Bottom bar
        var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(80, 0) };
        cancel.Click += (_, _) => Close();
        _back = new Button { Text = "< Back", AutoSize = true, MinimumSize = new Size(80, 0) };
        _back.Click += (_, _) => ShowPage(_page - 1);
        _next = new Button { AutoSize = true, MinimumSize = new Size(80, 0) };
        _next.Click += (_, _) => { if (_page < _pages.Count - 1) ShowPage(_page + 1); else Finish(); };
        CancelButton = cancel;
        AcceptButton = _next;
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 4, Margin = new Padding(0), Padding = new Padding(0, 10, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bottom.Controls.Add(cancel, 0, 0);
        bottom.Controls.Add(new Label { Text = "", AutoSize = true }, 1, 0);
        bottom.Controls.Add(_back, 2, 0);
        bottom.Controls.Add(_next, 3, 0);

        Controls.Add(_host);
        Controls.Add(_title);
        Controls.Add(_step);
        Controls.Add(bottom);
        ResumeLayout();

        ShowStatus(status);
        ShowPage(0);
    }

    /// <summary>Shows what was detected about Sonar; called again whenever the app's status changes, because GG may
    /// still be starting when the setup opens.</summary>
    public void ShowStatus(RoutingStatus? status)
    {
        (_detection.Text, _detectionHint.Text) = status switch
        {
            { Mode: SwitchingMode.Sonar } => ("SteelSeries Sonar found",
                "AudioSwitch will switch Sonar and Windows together, so you never have to set the device in two places."),
            { Mode: SwitchingMode.WindowsOnly } => ("SteelSeries Sonar not found",
                "AudioSwitch will switch the Windows default devices. If you use SteelSeries GG with Sonar, AudioSwitch " +
                "switches Sonar as well as soon as it is running."),
            { Mode: SwitchingMode.Undetermined } => ("SteelSeries GG is starting",
                "Sonar isn't answering yet. AudioSwitch adapts by itself: when Sonar answers, it switches Sonar and Windows " +
                "together; without Sonar, it switches the Windows default devices."),
            _ => ("Looking for SteelSeries Sonar…", ""),
        };
        // The Sonar question only makes sense with Sonar; its answer is kept either way (it is the setting's value).
        _sonarFound = ProfileSuggestions.SonarMayBeInUse(status?.Mode);
        _keepOnSonar.Visible = _keepOnSonarHint.Visible = _sonarFound;
    }

    void ShowPage(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        _page = index;
        if (index == _devicesPage) FillDevices(); // follows the monitor answer
        _host.Controls.Clear();
        _host.Controls.Add(_pages[index].Content);
        _title.Text = _pages[index].Title;
        _step.Text = $"Step {index + 1} of {_pages.Count}";
        _back.Enabled = index > 0;
        _next.Text = index == _pages.Count - 1 ? "Finish" : "Next >";
        ActiveControl = _next;
    }

    /// <summary>Lists the outputs with the suggested ticks and order. Kept as the user left it unless the monitor answer
    /// changed (monitors join or leave the list).</summary>
    void FillDevices()
    {
        var useMonitors = _useMonitors.Checked;
        if (_devicesListedForMonitors == useMonitors) return;
        _devicesListedForMonitors = useMonitors;

        _devices.Items.Clear();
        foreach (var device in FirstRunSetup.Devices(_activeEndpoints, useMonitors))
            _devices.Items.Add(new DeviceItem(device), device.Ticked);
        _noDevices.Visible = _devices.Items.Count == 0;
        _devices.Visible = _moveUp.Visible = _moveDown.Visible = !_noDevices.Visible;
        if (_devices.Items.Count > 0) _devices.SelectedIndex = 0;
        UpdateMoveButtons();
    }

    void MoveDevice(int delta)
    {
        var index = _devices.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _devices.Items.Count) return;
        var item = _devices.Items[index];
        var ticked = _devices.GetItemChecked(index);
        _devices.Items.RemoveAt(index);
        _devices.Items.Insert(target, item);
        _devices.SetItemChecked(target, ticked);
        _devices.SelectedIndex = target;
    }

    void UpdateMoveButtons()
    {
        var index = _devices.SelectedIndex;
        _moveUp.Enabled = index > 0;
        _moveDown.Enabled = index >= 0 && index < _devices.Items.Count - 1;
    }

    void Finish()
    {
        FillDevices();
        var ticked = Enumerable.Range(0, _devices.Items.Count).Where(_devices.GetItemChecked)
            .Select(i => ((DeviceItem)_devices.Items[i]).Device.Output).ToList();
        var settings = FirstRunSetup.Build(_defaults, new FirstRunChoices(ticked, _useMonitors.Checked, _checkForUpdates.Checked,
            _keepOnSonar.Checked, _sonarFound), _activeEndpoints);
        if (settings.Validate() is { Count: > 0 } problems)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, problems), "Setup not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            if (_startWithWindows.Checked != StartupRegistration.IsEnabled) StartupRegistration.Set(_startWithWindows.Checked);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            MessageBox.Show(this, $"\"Start with Windows\" could not be set: {ex.Message}\nYou can try again in Settings → General.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (_save(settings) is { } error)
        {
            MessageBox.Show(this, error, "Setup not saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Finished = true;
        Close();
    }

    sealed class DeviceItem(FirstRunDevice device)
    {
        public FirstRunDevice Device { get; } = device;
        public override string ToString() => $"{Device.Name}   ({Device.Connection})";
    }

    // --- Layout helpers

    static FlowLayoutPanel Page() => new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

    static Label Paragraph(string text, int width = TextWidth, Padding? margin = null) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(width, 0),
        Margin = margin ?? new Padding(3),
    };

    static Label Hint(string text, Padding? margin = null) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(TextWidth, 0),
        ForeColor = SystemColors.GrayText,
        Margin = margin ?? new Padding(3, 2, 3, 3),
    };

    static Button SideButton(string text) => new() { Text = text, AutoSize = true, MinimumSize = new Size(90, 0), Margin = new Padding(6, 0, 0, 4) };
}
