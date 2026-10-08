using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Routing;
using AudioSwitch.Core.Sonar;
using Microsoft.Extensions.Logging.Abstractions;
using static AudioSwitch.Core.Tests.TestEndpoints;

namespace AudioSwitch.Core.Tests;

public class RoutingExecutorTests
{
    sealed class FakeWindowsAudio : IWindowsAudio
    {
        public List<string> Calls { get; } = [];
        public event EventHandler<string>? Changed { add { } remove { } }
        public IReadOnlyList<AudioEndpoint> GetActiveEndpoints() => [];
        public string? GetDefaultOutputId() => null;
        public void SetDefaultOutput(string endpointId, OutputRoles roles) => Calls.Add($"output {roles} {endpointId}");
        public string? GetDefaultInputId() => null;
        public void SetDefaultInput(string endpointId) => Calls.Add($"input {endpointId}");
        public IReadOnlyList<AudioEndpoint> DisconnectBluetooth(AudioEndpoint endpoint) => [];
        public void Dispose() { }
    }

    sealed class FakeSonar : ISonarClient
    {
        public List<string> Calls { get; } = [];
        public Task<SonarMode> GetModeAsync(CancellationToken ct = default) { Calls.Add("mode"); return Task.FromResult(SonarMode.Classic); }
        public Task<SonarRouting> GetRoutingAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetDeviceAsync(SonarMode mode, SonarChannel channel, string deviceId, CancellationToken ct = default)
        {
            Calls.Add($"{channel} {deviceId}");
            return Task.CompletedTask;
        }
        public Task<IReadOnlyDictionary<string, bool>> GetWirelessStatesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Windows_only_plan_sets_the_windows_playback_and_recording_defaults_and_never_contacts_sonar()
    {
        var windows = new FakeWindowsAudio();
        var sonar = new FakeSonar();
        var plan = new SwitchPlan(SoundCore, new WindowsDefaults(SoundCore, SoundCore), [], Brio, "test") { WindowsOnly = true };

        var failures = await new RoutingExecutor(windows, sonar, NullLogger<RoutingExecutor>.Instance).ApplyAsync(plan, CancellationToken.None);

        Assert.Empty(failures);
        Assert.Equal([$"output All {SoundCore.Id}", $"input {Brio.Id}"], windows.Calls);
        Assert.Empty(sonar.Calls);
    }

    [Fact]
    public async Task Sonar_plan_routes_the_mic_through_sonar_and_leaves_the_windows_recording_default_alone()
    {
        var windows = new FakeWindowsAudio();
        var sonar = new FakeSonar();
        var plan = new SwitchPlan(SoundCore, new WindowsDefaults(SoundCore, SoundCore), [SonarChannel.Game], Brio, "test");

        await new RoutingExecutor(windows, sonar, NullLogger<RoutingExecutor>.Instance).ApplyAsync(plan, CancellationToken.None);

        Assert.Equal([$"output All {SoundCore.Id}"], windows.Calls);
        Assert.Equal(["mode", $"Game {SoundCore.Id}", $"Mic {Brio.Id}"], sonar.Calls);
    }
}
