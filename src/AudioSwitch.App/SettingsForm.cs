using System.Diagnostics;
using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Routing;
using AudioSwitch.Core.Settings;

namespace AudioSwitch.App;

/// <summary>
/// Edits settings.json: general options and the device profiles. Suggestions come from the devices Windows reports
/// right now; anything can still be typed. Saving applies the settings immediately (no restart).
/// </summary>
sealed class SettingsForm : Form
{
    const string ItselfText = "(the device itself)";
    const string SameAsDefaultText = "(same as Windows default)";

    readonly AudioSwitchSettings _original;
    readonly Func<AudioSwitchSettings, string?> _save;
    readonly string _logDir;
    readonly IReadOnlyList<AudioEndpoint> _activeEndpoints;
    readonly Func<SwitchingMode?> _mode;
    readonly List<DeviceProfile> _profiles;

    // General
    readonly CheckBox _startWithWindows;
    readonly CheckBox _checkForUpdates;
    readonly CheckBox _newDevicesOnSonar;
    readonly NumericUpDown _timeout;
    readonly ListEditor _defaultMics;
    readonly ListEditor _excluded;

    // Devices
    readonly ListBox _profileList;
    readonly Button _removeProfile, _moveUp, _moveDown;
    readonly TableLayoutPanel _details;
    readonly TextBox _name;
    readonly ComboBox _match;
    readonly Label _matchStatus;
    readonly ComboBox _windowsDefault, _communicationsDefault;
    readonly ListEditor _mics;
    readonly CheckBox _disconnect;

    int _selected = -1;
    bool _loading, _dirty, _closeWithoutAsking;

    /// <param name="save">Saves and applies the edited settings; returns an error message when that failed.</param>
    /// <param name="activeEndpoints">Every active Windows endpoint, usable or not (Sonar's virtual ones are valid
    /// Windows defaults), for the suggestions and the device picker.</param>
    /// <param name="mode">What AudioSwitch manages right now (null: not known yet), for whether Add… applies Sonar's
    /// Windows defaults.</param>
    public SettingsForm(AudioSwitchSettings settings, Func<AudioSwitchSettings, string?> save, string logDir,
        IReadOnlyList<AudioEndpoint> activeEndpoints, Func<SwitchingMode?> mode, Icon icon)
    {
        _original = settings;
        _save = save;
        _logDir = logDir;
        _activeEndpoints = activeEndpoints;
        _mode = mode;
        _profiles = [.. settings.Profiles];

        var outputNames = FragmentSuggestions.DeviceNames(activeEndpoints, EndpointFlow.Render);
        var micNames = FragmentSuggestions.DeviceNames(activeEndpoints, EndpointFlow.Capture);
        var outputLabels = FragmentSuggestions.EndpointLabels(activeEndpoints, EndpointFlow.Render);

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"{AppInfo.Product} settings";
        Icon = icon;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(780, 520);
        MinimumSize = new Size(760, 530);
        Padding = new Padding(10);

        // --- General
        _startWithWindows = new CheckBox { Text = "Start with Windows", AutoSize = true, Checked = StartupRegistration.IsEnabled };
        _checkForUpdates = new CheckBox
        {
            Text = "Check for updates automatically (you choose when to install)",
            AutoSize = true,
            Checked = settings.CheckForUpdatesAutomatically,
        };
        _newDevicesOnSonar = new CheckBox
        {
            Text = "New devices: keep Windows on Sonar's devices",
            AutoSize = true,
            Checked = settings.NewDevicesUseSonarDefaults,
            Margin = new Padding(3, 6, 3, 0),
        };
        _timeout = new NumericUpDown
        {
            Minimum = AudioSwitchSettings.MinPopupTimeoutSeconds,
            Maximum = AudioSwitchSettings.MaxPopupTimeoutSeconds,
            Value = Math.Clamp(settings.PopupTimeoutSeconds, AudioSwitchSettings.MinPopupTimeoutSeconds, AudioSwitchSettings.MaxPopupTimeoutSeconds),
            Width = 60,
        };
        _defaultMics = new ListEditor(micNames, reorderable: true) { Dock = DockStyle.Fill, Items = settings.DefaultMicPreference };
        _excluded = new ListEditor(FragmentSuggestions.DeviceNames(activeEndpoints, EndpointFlow.Render).Concat(micNames)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase), reorderable: false)
        { Dock = DockStyle.Fill, Items = settings.ExcludedNameFragments };

        var timeoutRow = Row();
        timeoutRow.Controls.Add(new Label { Text = "Close the \"switch to this device?\" popup after", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
        timeoutRow.Controls.Add(_timeout);
        timeoutRow.Controls.Add(new Label { Text = "seconds (counts as \"Not now\")", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });

        var lists = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 8, 0, 0) };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        lists.Controls.Add(Group("Default microphone order",
            "For devices without their own microphone list. The first one that is connected is used.", _defaultMics), 0, 0);
        lists.Controls.Add(Group("Ignored devices",
            "Outputs and microphones whose name contains any of these are never switched to.", _excluded), 1, 0);

        var general = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
        for (var i = 0; i < 5; i++) general.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        general.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        general.Controls.Add(_startWithWindows);
        general.Controls.Add(_checkForUpdates);
        general.Controls.Add(_newDevicesOnSonar);
        var onSonarHint = Hint("Add… sets a new device's Windows defaults to Sonar's Gaming and Chat devices, so Sonar's EQ and " +
            "mixer keep working. Only while SteelSeries Sonar is in use; otherwise the device itself.", 600);
        onSonarHint.Margin = new Padding(21, 0, 3, 0);
        general.Controls.Add(onSonarHint);
        general.Controls.Add(timeoutRow);
        general.Controls.Add(lists);

        // --- Devices: list on the left, the selected device's details on the right
        _profileList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _profileList.SelectedIndexChanged += (_, _) => { if (!_loading) SelectProfile(_profileList.SelectedIndex); };
        var addProfile = ListButton("Add…");
        addProfile.Click += (_, _) => AddProfile();
        _removeProfile = ListButton("Remove");
        _removeProfile.Click += (_, _) => RemoveProfile();
        _moveUp = ListButton("Move up");
        _moveUp.Click += (_, _) => MoveProfile(-1);
        _moveDown = ListButton("Move down");
        _moveDown.Click += (_, _) => MoveProfile(+1);
        var listButtons = new TableLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 4, 0, 0) };
        listButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        listButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        listButtons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        listButtons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        listButtons.Controls.AddRange([addProfile, _removeProfile, _moveUp, _moveDown]);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0, 0, 10, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(_profileList);
        left.Controls.Add(listButtons);
        left.Controls.Add(Hint("When the current device goes away, the most recently used one is picked, then this order.", 230));

        _name = new TextBox { Dock = DockStyle.Fill };
        _match = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
        _match.Items.AddRange([.. outputNames]);
        _matchStatus = Hint("");
        _windowsDefault = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
        _windowsDefault.Items.AddRange([ItselfText, .. outputLabels]);
        _communicationsDefault = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
        _communicationsDefault.Items.AddRange([SameAsDefaultText, .. outputLabels]);
        _mics = new ListEditor(micNames, reorderable: true) { Dock = DockStyle.Fill };
        _disconnect = new CheckBox { Text = "Disconnect it from this PC when I answer \"Not now\"", AutoSize = true };

        _details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        _details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        AddDetail("Name", _name);
        AddDetail("Device name contains", _match);
        AddDetail("", _matchStatus);
        AddDetail("Windows default", _windowsDefault);
        AddDetail("Communications default", _communicationsDefault);
        AddDetail("", Hint("Windows defaults are set together with Sonar, e.g. to Sonar's own Gaming and Chat devices."));
        AddDetail("Microphones", _mics, fill: true);
        AddDetail("", Hint("The first one that is connected is used. Empty: the default microphone order (General)."));
        AddDetail("", _disconnect);
        AddDetail("", Hint("For Bluetooth devices, e.g. someone else's headset. A popup that times out counts as \"Not now\"."));

        foreach (var c in new Control[] { _name, _match, _windowsDefault, _communicationsDefault })
            c.TextChanged += (_, _) => DetailsChanged();
        _mics.Changed += (_, _) => DetailsChanged();
        _disconnect.CheckedChanged += (_, _) => DetailsChanged();

        var devices = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8) };
        devices.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250f));
        devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        devices.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        devices.Controls.Add(left, 0, 0);
        devices.Controls.Add(_details, 1, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page("Devices", devices));
        tabs.TabPages.Add(Page("General", general));

        // --- Bottom bar
        var logs = new Button { Text = "Open log folder", AutoSize = true };
        logs.Click += (_, _) => OpenLogFolder();
        var saveButton = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(80, 0) };
        saveButton.Click += (_, _) => Save();
        var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(80, 0) };
        cancel.Click += (_, _) => Close();
        CancelButton = cancel; // no AcceptButton: Enter adds the typed entry in a list editor
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 4, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bottom.Controls.Add(logs, 0, 0);
        bottom.Controls.Add(new Label { Text = "", AutoSize = true }, 1, 0);
        bottom.Controls.Add(saveButton, 2, 0);
        bottom.Controls.Add(cancel, 3, 0);

        Controls.Add(tabs);
        Controls.Add(bottom);
        ResumeLayout();

        _timeout.ValueChanged += (_, _) => _dirty = true;
        _startWithWindows.CheckedChanged += (_, _) => _dirty = true;
        _checkForUpdates.CheckedChanged += (_, _) => _dirty = true;
        _newDevicesOnSonar.CheckedChanged += (_, _) => _dirty = true;
        _defaultMics.Changed += (_, _) => _dirty = true;
        _excluded.Changed += (_, _) => _dirty = true;

        RefillProfileList();
        SelectProfile(_profiles.Count > 0 ? 0 : -1);
    }

    // --- Device list

    void RefillProfileList()
    {
        _loading = true;
        _profileList.Items.Clear();
        _profileList.Items.AddRange([.. _profiles.Select(ListText)]);
        _loading = false;
    }

    static string ListText(DeviceProfile p) => p.Name.Trim().Length > 0 ? p.Name : "(unnamed device)";

    void SelectProfile(int index)
    {
        _selected = index;
        _loading = true;
        _profileList.SelectedIndex = index;
        var profile = index >= 0 ? _profiles[index] : null;
        _name.Text = profile?.Name ?? "";
        _match.Text = profile?.Match ?? "";
        _windowsDefault.Text = profile?.WindowsDefault ?? ItselfText;
        _communicationsDefault.Text = profile?.WindowsCommunicationsDefault ?? SameAsDefaultText;
        _mics.Items = profile?.MicPreference ?? [];
        _disconnect.Checked = profile?.DisconnectWhenDeclined ?? false;
        _details.Enabled = profile is not null;
        _loading = false;

        _removeProfile.Enabled = index >= 0;
        _moveUp.Enabled = index > 0;
        _moveDown.Enabled = index >= 0 && index < _profiles.Count - 1;
        UpdateMatchStatus();
    }

    void DetailsChanged()
    {
        if (_loading || _selected < 0) return;
        _dirty = true;
        _profiles[_selected] = _profiles[_selected] with
        {
            Name = _name.Text,
            Match = _match.Text,
            WindowsDefault = OrNull(_windowsDefault.Text, ItselfText),
            WindowsCommunicationsDefault = OrNull(_communicationsDefault.Text, SameAsDefaultText),
            MicPreference = _mics.Items,
            DisconnectWhenDeclined = _disconnect.Checked,
        };

        var text = ListText(_profiles[_selected]);
        if (!Equals(_profileList.Items[_selected], text))
        {
            _loading = true;
            _profileList.Items[_selected] = text;
            _profileList.SelectedIndex = _selected;
            _loading = false;
        }
        UpdateMatchStatus();
    }

    static string? OrNull(string text, string placeholder) =>
        string.IsNullOrWhiteSpace(text) || text == placeholder ? null : text;

    /// <summary>Shows which connected output the Match fragment picks up, so a typo is noticed before saving.</summary>
    void UpdateMatchStatus()
    {
        var match = _match.Text.Trim();
        var matches = match.Length == 0 ? [] : _activeEndpoints
            .Where(e => e.Flow == EndpointFlow.Render && e.Name.Contains(match, StringComparison.OrdinalIgnoreCase)).ToList();
        _matchStatus.Text = _selected < 0 ? ""
            : match.Length == 0 ? "Type part of the device's name, or pick a connected device."
            : matches.Count > 0 ? $"Connected now: {string.Join(", ", matches.Select(m => m.Name))}"
            : "Not connected right now; it is recognised when it connects.";
    }

    /// <summary>Lets the user pick a connected output and adds a profile filled in from it (or an empty one).</summary>
    void AddProfile()
    {
        using var picker = new DevicePickerForm(_activeEndpoints, _profiles, Icon!);
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        _profiles.Add(picker.Picked is { } output
            ? ProfileSuggestions.FromEndpoint(output, _activeEndpoints,
                ProfileSuggestions.UseSonarDefaults(_newDevicesOnSonar.Checked, _mode()))
            : new DeviceProfile { Name = "", Match = "" });
        _dirty = true;
        RefillProfileList();
        SelectProfile(_profiles.Count - 1);
        _name.Focus();
        _name.SelectAll();
    }

    void RemoveProfile()
    {
        if (_selected < 0) return;
        _profiles.RemoveAt(_selected);
        _dirty = true;
        RefillProfileList();
        SelectProfile(Math.Min(_selected, _profiles.Count - 1));
    }

    void MoveProfile(int delta)
    {
        var target = _selected + delta;
        if (_selected < 0 || target < 0 || target >= _profiles.Count) return;
        (_profiles[_selected], _profiles[target]) = (_profiles[target], _profiles[_selected]);
        _dirty = true;
        RefillProfileList();
        SelectProfile(target);
    }

    // --- Save / close

    void Save()
    {
        _defaultMics.CommitPendingInput();
        _excluded.CommitPendingInput();
        _mics.CommitPendingInput();

        var edited = (_original with
        {
            PopupTimeoutSeconds = (int)_timeout.Value,
            CheckForUpdatesAutomatically = _checkForUpdates.Checked,
            NewDevicesUseSonarDefaults = _newDevicesOnSonar.Checked,
            DefaultMicPreference = _defaultMics.Items,
            ExcludedNameFragments = _excluded.Items,
            Profiles = [.. _profiles],
        }).Normalize();

        if (edited.Validate() is { Count: > 0 } problems)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, problems), "Settings not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (edited.ExcludedNameFragments.FirstOrDefault(f => edited.Profiles.Any(p => p.Match.Contains(f, StringComparison.OrdinalIgnoreCase))) is { } hides
            && MessageBox.Show(this, $"'{hides}' is ignored, so a device that contains it in its name will never be switched to. Save anyway?",
                "Ignored device", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        if (_startWithWindows.Checked != StartupRegistration.IsEnabled)
            StartupRegistration.Set(_startWithWindows.Checked);

        if (_save(edited) is { } error)
        {
            MessageBox.Show(this, error, "Settings not saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        CloseWithoutAsking();
    }

    /// <summary>Closes without the "discard changes?" question (after saving, or when the app exits).</summary>
    public void CloseWithoutAsking()
    {
        _closeWithoutAsking = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty && !_closeWithoutAsking && e.CloseReason == CloseReason.UserClosing
            && MessageBox.Show(this, "Discard your changes?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            e.Cancel = true;
        base.OnFormClosing(e);
    }

    void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(_logDir);
            Process.Start(new ProcessStartInfo(_logDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open {_logDir}: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // --- Layout helpers

    void AddDetail(string label, Control control, bool fill = false)
    {
        var row = _details.RowCount++;
        _details.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100f) : new RowStyle(SizeType.AutoSize));
        if (label.Length > 0) _details.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 10, 3) }, 0, row);
        _details.Controls.Add(control, 1, row);
    }

    static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { UseVisualStyleBackColor = true };
        page.Controls.Add(content);
        return page;
    }

    static GroupBox Group(string title, string hint, Control content)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.Controls.Add(Hint(hint, 310));
        layout.Controls.Add(content);
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8), Margin = new Padding(0, 0, 8, 0) };
        group.Controls.Add(layout);
        return group;
    }

    /// <param name="maxWidth">Wrap width; a wider label would push its table column past the window edge.</param>
    static Label Hint(string text, int maxWidth = 330) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(maxWidth, 0),
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(3, 2, 3, 6),
    };

    static Button ListButton(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 4, 4) };

    static FlowLayoutPanel Row() => new() { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
}
