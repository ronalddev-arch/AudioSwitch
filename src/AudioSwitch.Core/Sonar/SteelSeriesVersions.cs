using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace AudioSwitch.Core.Sonar;

/// <summary>GG and Sonar versions, either installed or the ones a build was verified against.</summary>
public sealed record SteelSeriesVersions(string? Gg, string? Sonar)
{
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SteelSeries GG";

    public override string ToString() => $"GG {Gg ?? "unknown"} / Sonar {Sonar ?? "unknown"}";

    /// <summary>True when both versions are known and differ from <paramref name="other"/>'s; unknown never counts
    /// as a mismatch, so a failed detection doesn't nag.</summary>
    public bool DiffersFrom(SteelSeriesVersions other) =>
        (Gg is not null && other.Gg is not null && Gg != other.Gg) ||
        (Sonar is not null && other.Sonar is not null && Sonar != other.Sonar);

    /// <summary>The versions recorded in Directory.Build.props (VerifiedGgVersion/VerifiedSonarVersion).</summary>
    public static SteelSeriesVersions Verified(Assembly assembly)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
        return new SteelSeriesVersions(metadata.GetValueOrDefault("VerifiedGgVersion"), metadata.GetValueOrDefault("VerifiedSonarVersion"));
    }

    /// <summary>True when GG's uninstall entry exists.</summary>
    public static bool IsGgInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
        return key is not null;
    }

    /// <summary>Reads GG's version from its uninstall entry and Sonar's from its executable. Both are null when GG isn't
    /// installed, which <see cref="DiffersFrom"/> never counts as a mismatch (no "GG was updated" balloon).</summary>
    public static SteelSeriesVersions DetectInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
        var gg = key?.GetValue("DisplayVersion") as string;

        string? sonar = null;
        if (key?.GetValue("DisplayIcon") is string ggExe && Path.GetDirectoryName(ggExe.Trim('"')) is { } installDir)
        {
            var sonarExe = Path.Combine(installDir, "apps", "sonar", "SteelSeriesSonar.exe");
            if (File.Exists(sonarExe)) sonar = FileVersionInfo.GetVersionInfo(sonarExe).FileVersion;
        }
        return new SteelSeriesVersions(gg, sonar);
    }
}
