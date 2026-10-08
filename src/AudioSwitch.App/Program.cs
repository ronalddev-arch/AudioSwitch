using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using Velopack;

namespace AudioSwitch.App;

static class Program
{
    [STAThread]
    static void Main()
    {
        // First, before anything else: when Setup.exe or the updater starts the exe with a hook argument, this handles
        // it and exits. It must run before the single-instance mutex, because the app may still be running then.
        // Downloaded updates are only applied when the user chooses to (UpdateChecker), never at startup.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .OnBeforeUninstallFastCallback(_ => StartupRegistration.RemoveForUninstall())
            .Run();

        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\AudioSwitch.SingleInstance", out var isFirstInstance);
        // A second start asks the running copy to show that it's there; quitting silently looked like nothing happened.
        using var showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AudioSwitch.ShowRequested");
        if (!isFirstInstance)
        {
            showRequested.Set();
            return;
        }

        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioSwitch");
        var logDir = Path.Combine(dataDir, "logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(logDir, "audioswitch-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
            var log = loggerFactory.CreateLogger("AudioSwitch");
            Application.ThreadException += (_, e) => log.LogError(e.Exception, "Unhandled UI exception");
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");

            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApplicationContext(loggerFactory, dataDir, logDir, showRequested));
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
