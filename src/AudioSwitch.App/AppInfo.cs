using System.Reflection;
using AudioSwitch.Core.Sonar;

namespace AudioSwitch.App;

/// <summary>Product facts from the assembly attributes that Directory.Build.props sets, so they live in one place.</summary>
static class AppInfo
{
    static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    public static string Product => Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "AudioSwitch";

    /// <summary>Full informational version, including the commit hash when the SDK adds one ("0.4.0+abc123").</summary>
    public static string FullVersion => Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

    public static string Version => FullVersion.Split('+')[0];

    public static string? Description => Assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;

    /// <summary>The Authors property, ';'-separated.</summary>
    public static IReadOnlyList<string> Authors =>
        (Metadata("Authors") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The GitHub repository whose Releases hold the updates (RepositoryUrl in Directory.Build.props); null when
    /// not set, which means updates are not configured.</summary>
    public static string? RepositoryUrl => Metadata("RepositoryUrl") is { Length: > 0 } url ? url : null;

    public static SteelSeriesVersions VerifiedSteelSeries => SteelSeriesVersions.Verified(Assembly);

    /// <summary>The app icon at <paramref name="size"/> pixels, from the exe's resources.</summary>
    public static Icon? LoadIcon(int size)
    {
        try { return Icon.ExtractIcon(Application.ExecutablePath, 0, size); }
        catch (Exception ex) when (ex is IOException or ArgumentException) { return null; }
    }

    static string? Metadata(string key) =>
        Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;
}
