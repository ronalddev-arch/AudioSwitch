using Microsoft.Win32;
using Velopack.Locators;

namespace AudioSwitch.App;

/// <summary>"Start with Windows" via the per-user Run key (no admin needed).</summary>
static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "AudioSwitch";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{ExecutablePath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// The exe to start at login. Installed by Setup.exe (Velopack), the running exe lives in
    /// <c>%LocalAppData%\AudioSwitch\current\</c>, a folder that every update replaces; the stub with the same name in
    /// the install root stays put and launches the current version, so point at that. Otherwise (dev runs,
    /// publish.ps1) the running exe itself.
    /// </summary>
    static string ExecutablePath =>
        VelopackRoot() is { } root && Path.Combine(root, Path.GetFileName(Application.ExecutablePath)) is var stub && File.Exists(stub)
            ? stub
            : Application.ExecutablePath;

    /// <summary>Velopack uninstall hook: removes the Run value if it points into this installation, and leaves one
    /// that points at another copy (such as a publish.ps1 build) alone.</summary>
    public static void RemoveForUninstall()
    {
        // During uninstall this exe runs from <root>\current\, so its grandparent folder is the root either way.
        var root = VelopackRoot() ?? Path.GetDirectoryName(Path.GetDirectoryName(Environment.ProcessPath));
        if (string.IsNullOrEmpty(root)) return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string command
            && command.Trim().Trim('"').StartsWith(root.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase))
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>The Velopack install root (<c>%LocalAppData%\AudioSwitch</c>), or null when not installed by Setup.exe.</summary>
    static string? VelopackRoot()
    {
        try
        {
            var locator = VelopackLocator.Current;
            return locator.CurrentlyInstalledVersion is not null && !locator.IsPortable ? locator.RootAppDir : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
