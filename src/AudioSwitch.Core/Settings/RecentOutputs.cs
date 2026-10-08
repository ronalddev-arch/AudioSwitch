namespace AudioSwitch.Core.Settings;

/// <summary>Most-recently-used output keys (profile name, or device name for unknown devices), newest first.</summary>
public sealed class RecentOutputs(IReadOnlyList<string> initial, Action<IReadOnlyList<string>>? persist = null)
{
    const int Capacity = 20;
    readonly List<string> _keys = initial.Distinct().Take(Capacity).ToList();
    readonly Lock _lock = new();

    public IReadOnlyList<string> Keys
    {
        get { lock (_lock) return _keys.ToArray(); }
    }

    public void Touch(string key)
    {
        string[] snapshot;
        lock (_lock)
        {
            if (_keys.Count > 0 && _keys[0] == key) return;
            _keys.Remove(key);
            _keys.Insert(0, key);
            if (_keys.Count > Capacity) _keys.RemoveAt(_keys.Count - 1);
            snapshot = _keys.ToArray();
        }
        persist?.Invoke(snapshot);
    }
}
