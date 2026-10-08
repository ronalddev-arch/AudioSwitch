using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Settings;

namespace AudioSwitch.App;

/// <summary>
/// Settings → Devices → Add: lists the connected outputs so a new device profile starts from a real device. Modal over
/// the settings window, which owns it. <see cref="Form.DialogResult"/> OK with <see cref="Picked"/> null means
/// "enter manually" (a device that is not connected right now).
/// </summary>
sealed class DevicePickerForm : Form
{
    readonly ListView _list;
    readonly Button _ok;

    /// <summary>The chosen output, or null for "enter manually".</summary>
    public AudioEndpoint? Picked { get; private set; }

    /// <param name="activeEndpoints">Every active Windows endpoint; the pickable outputs are chosen from them.</param>
    /// <param name="profiles">The profiles as currently edited, to show which devices already have one.</param>
    public DevicePickerForm(IReadOnlyList<AudioEndpoint> activeEndpoints, IReadOnlyList<DeviceProfile> profiles, Icon icon)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Add a device";
        Icon = icon;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(720, 320);
        MinimumSize = new Size(480, 260);
        Padding = new Padding(10);

        var intro = new Label
        {
            Text = "Pick the output device to add. Its name and microphone are filled in for you; you can change them afterwards.",
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 8),
        };

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        _list.Columns.Add("Device", 170);
        _list.Columns.Add("Windows name", 290);
        _list.Columns.Add("Connection", 95);
        _list.Columns.Add("Device profile", 135);

        ListViewItem? firstNew = null;
        foreach (var output in ProfileSuggestions.PickableOutputs(activeEndpoints))
        {
            var existing = ProfileSuggestions.MatchingProfile(output, profiles);
            var item = new ListViewItem([
                ProfileSuggestions.FriendlyName(output, activeEndpoints),
                output.Name,
                ProfileSuggestions.ConnectionType(output),
                existing is null ? "" : $"Yes: {existing.Name}",
            ]) { Tag = output };
            if (existing is not null) item.ForeColor = SystemColors.GrayText;
            else firstNew ??= item;
            _list.Items.Add(item);
        }
        if (_list.Items.Count == 0)
            _list.Items.Add(new ListViewItem("No audio outputs are connected right now.") { ForeColor = SystemColors.GrayText });

        var manual = new Button { Text = "Enter manually…", AutoSize = true };
        manual.Click += (_, _) => Pick(null, manual: true);
        _ok = new Button { Text = "Add", AutoSize = true, MinimumSize = new Size(80, 0), Enabled = false };
        _ok.Click += (_, _) => Pick(SelectedOutput());
        _list.SelectedIndexChanged += (_, _) => _ok.Enabled = SelectedOutput() is not null;
        _list.ItemActivate += (_, _) => Pick(SelectedOutput()); // double-click or Enter
        var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(80, 0), DialogResult = DialogResult.Cancel };
        CancelButton = cancel;
        AcceptButton = _ok;

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 4, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bottom.Controls.Add(manual, 0, 0);
        bottom.Controls.Add(new Label { Text = "", AutoSize = true }, 1, 0);
        bottom.Controls.Add(_ok, 2, 0);
        bottom.Controls.Add(cancel, 3, 0);

        var hint = new Label
        {
            Text = "\"Enter manually\" adds an empty device, e.g. for one that is not connected right now.",
            AutoSize = true,
            Dock = DockStyle.Bottom,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(0, 6, 0, 0),
        };

        Controls.Add(_list);
        Controls.Add(intro);
        Controls.Add(hint);
        Controls.Add(bottom);
        ResumeLayout();

        // Preselect the first device without a profile, the likely reason for adding one.
        if (firstNew is not null)
        {
            firstNew.Selected = firstNew.Focused = true;
            firstNew.EnsureVisible();
        }
        _ok.Enabled = SelectedOutput() is not null;
        ActiveControl = _list;
    }

    AudioEndpoint? SelectedOutput() => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as AudioEndpoint : null;

    void Pick(AudioEndpoint? output, bool manual = false)
    {
        if (output is null && !manual) return;
        Picked = output;
        DialogResult = DialogResult.OK;
    }
}
