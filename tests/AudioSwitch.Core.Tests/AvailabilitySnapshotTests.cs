using AudioSwitch.Core.Watching;
using static AudioSwitch.Core.Tests.TestEndpoints;

namespace AudioSwitch.Core.Tests;

public class AvailabilitySnapshotTests
{
    static readonly DeviceFilter Filter = new(ExampleSettings.Sample.ToFilterOptions());

    static AvailabilitySnapshot Snapshot(bool? headsetOn, params Audio.AudioEndpoint[] active) =>
        AvailabilitySnapshot.Build(active,
            headsetOn is { } on ? new Dictionary<string, bool> { [Arctis.Id] = on, [ArctisMic.Id] = on } : null,
            Filter);

    [Fact]
    public void Splits_usable_outputs_and_mics_and_records_exclusions()
    {
        var snapshot = Snapshot(headsetOn: true, All);

        Assert.Equal([Arctis, SoundCore], snapshot.Outputs.OrderBy(e => e.Name));
        Assert.Equal([ArctisMic, Brio], snapshot.Mics.OrderBy(e => e.Name));
        Assert.Equal(4, snapshot.Excluded.Count); // monitor, Sonar virtual, 2x hands-free
    }

    [Fact]
    public void Headset_off_makes_arctis_endpoints_unusable_even_though_windows_reports_them_active()
    {
        var snapshot = Snapshot(headsetOn: false, All);

        Assert.DoesNotContain(Arctis, snapshot.Outputs);
        Assert.DoesNotContain(ArctisMic, snapshot.Mics);
        Assert.Contains(snapshot.Excluded, x => x.Endpoint == Arctis && x.IsHeadsetOff);
    }

    [Fact]
    public void Unknown_headset_state_treats_arctis_as_usable() =>
        Assert.Contains(Arctis, Snapshot(headsetOn: null, All).Outputs);

    [Fact]
    public void Headset_power_on_is_reported_as_arrival_with_reason()
    {
        var changes = Snapshot(true, All).DiffFrom(Snapshot(false, All));

        Assert.Equal(2, changes.Count);
        Assert.All(changes, c => Assert.Equal((DeviceChangeKind.Arrived, "headset powered on"), (c.Kind, c.Reason)));
    }

    [Fact]
    public void Headset_power_off_is_reported_as_departure_with_reason()
    {
        var changes = Snapshot(false, All).DiffFrom(Snapshot(true, All));

        Assert.Contains(changes, c => c is { Kind: DeviceChangeKind.Departed, Reason: "headset powered off" } && c.Endpoint == Arctis);
    }

    [Fact]
    public void Bluetooth_connect_reports_only_the_stereo_endpoint()
    {
        var changes = Snapshot(true, All).DiffFrom(Snapshot(true, Arctis, ArctisMic, MonitorHdmi, SonarGaming, Brio));

        var change = Assert.Single(changes);
        Assert.Equal((DeviceChangeKind.Arrived, SoundCore, "connected"), (change.Kind, change.Endpoint, change.Reason));
    }

    [Fact]
    public void Hands_free_endpoints_arriving_first_cause_no_change()
    {
        var before = Snapshot(true, Arctis, ArctisMic, Brio);
        var handsFreeOnly = Snapshot(true, Arctis, ArctisMic, Brio, SoundCoreHandsFree, SoundCoreHandsFreeMic);

        Assert.Empty(handsFreeOnly.DiffFrom(before));
    }

    [Fact]
    public void Bluetooth_disconnect_is_reported_as_departure()
    {
        var change = Assert.Single(Snapshot(true, Arctis, Brio).DiffFrom(Snapshot(true, Arctis, Brio, SoundCore)));

        Assert.Equal((DeviceChangeKind.Departed, SoundCore, "disconnected"), (change.Kind, change.Endpoint, change.Reason));
    }
}
