using System.ComponentModel;

namespace AudioSwitch.App;

/// <summary>
/// Edits a list of name fragments: type one or pick a suggestion (from the devices connected right now), Add, Remove
/// and, for preference lists, reorder.
/// </summary>
sealed class ListEditor : TableLayoutPanel
{
    readonly ComboBox _input;
    readonly ListBox _list;
    readonly Button _remove;
    readonly Button? _up, _down;

    public ListEditor(IEnumerable<string> suggestions, bool reorderable)
    {
        ColumnCount = 2;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Margin = new Padding(0);

        _input = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
        _input.Items.AddRange([.. suggestions]);
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            AddInput();
        };
        var add = SideButton("Add");
        add.Click += (_, _) => AddInput();

        _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Height = 80 };
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
        _remove = SideButton("Remove");
        _remove.Click += (_, _) => RemoveSelected();

        var side = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        side.Controls.Add(_remove);
        if (reorderable)
        {
            _up = SideButton("Move up");
            _up.Click += (_, _) => MoveSelected(-1);
            _down = SideButton("Move down");
            _down.Click += (_, _) => MoveSelected(+1);
            side.Controls.Add(_up);
            side.Controls.Add(_down);
        }

        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        Controls.Add(_input, 0, 0);
        Controls.Add(add, 1, 0);
        Controls.Add(_list, 0, 1);
        Controls.Add(side, 1, 1);
        UpdateButtons();
    }

    /// <summary>Raised when the user added, removed or moved an entry (not when <see cref="Items"/> is set).</summary>
    public event EventHandler? Changed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<string> Items
    {
        get => _list.Items.Cast<string>().ToList();
        set
        {
            _list.Items.Clear();
            _list.Items.AddRange([.. value]);
            UpdateButtons();
        }
    }

    /// <summary>Text typed but not added yet; the settings window adds it on Save rather than silently dropping it.</summary>
    public void CommitPendingInput()
    {
        if (_input.Text.Trim().Length > 0) AddInput();
    }

    void AddInput()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0) return;
        if (!_list.Items.Cast<string>().Any(x => x.Equals(text, StringComparison.OrdinalIgnoreCase)))
        {
            _list.Items.Add(text);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        _input.Text = "";
        UpdateButtons();
    }

    void RemoveSelected()
    {
        var index = _list.SelectedIndex;
        if (index < 0) return;
        _list.Items.RemoveAt(index);
        if (_list.Items.Count > 0) _list.SelectedIndex = Math.Min(index, _list.Items.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
        UpdateButtons();
    }

    void MoveSelected(int delta)
    {
        var index = _list.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _list.Items.Count) return;
        var item = _list.Items[index];
        _list.Items.RemoveAt(index);
        _list.Items.Insert(target, item);
        _list.SelectedIndex = target;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void UpdateButtons()
    {
        var index = _list.SelectedIndex;
        _remove.Enabled = index >= 0;
        if (_up is not null) _up.Enabled = index > 0;
        if (_down is not null) _down.Enabled = index >= 0 && index < _list.Items.Count - 1;
    }

    static Button SideButton(string text) => new() { Text = text, AutoSize = true, MinimumSize = new Size(90, 0), Margin = new Padding(6, 0, 0, 4) };
}
