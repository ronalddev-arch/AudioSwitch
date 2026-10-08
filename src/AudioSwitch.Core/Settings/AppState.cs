namespace AudioSwitch.Core.Settings;

/// <summary>Machine-written state (state.json), kept apart from the hand-editable settings.json.</summary>
public sealed record AppState
{
    public IReadOnlyList<string> RecentOutputs { get; init; } = [];
}
