using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Watching;

namespace AudioSwitch.Core.Routing;

/// <summary>
/// Turns availability changes into decisions. Pure logic (no I/O), so every rule is unit-tested:
/// <list type="bullet">
/// <item>An output in use goes away → automatically fall back to the best remaining output (and its preferred mic).</item>
/// <item>The mic in use goes away → pick a mic by the preference of the device currently playing.</item>
/// <item>A new output becomes available (incl. headset powered on) → ask, unless switching would change nothing.</item>
/// </list>
/// With Sonar, "routing" is Sonar's channels + mic; in Windows-only mode (<see cref="CurrentRouting.WindowsOnly"/>) it is
/// the Windows default playback and recording devices. When neither is known (Sonar not reachable, mode undetermined),
/// nothing is switched or asked.
/// </summary>
public sealed class RoutingEngine(RoutingRules rules)
{
    public IReadOnlyList<RoutingDecision> Decide(
        IReadOnlyList<DeviceChange> changes, AvailabilitySnapshot snapshot, CurrentRouting current, IReadOnlyList<string> recent)
    {
        var decisions = new List<RoutingDecision>();
        AudioEndpoint? fallbackOutput = null;

        // An output went away and either it was in use, or Sonar's routing is now broken: when a device disappears from
        // Windows (Bluetooth off) Sonar has already cleared its channels by the time we look, so "in use" can't be seen.
        var departedOutputs = changes.Where(c => c is { Kind: DeviceChangeKind.Departed, Endpoint.Flow: EndpointFlow.Render }).ToList();
        var lostInUse = departedOutputs.Where(c => current.Known && current.UsesOutput(c.Endpoint.Id)).ToList();
        var lostOutputs = lostInUse.Count > 0 ? lostInUse
            : departedOutputs.Count > 0 && current.OutputBroken(snapshot) ? departedOutputs
            : [];
        if (lostOutputs.Count > 0)
        {
            var cause = string.Join(", ", lostOutputs.Select(c => $"{rules.DisplayName(c.Endpoint)} {c.Reason}"));
            var (fallback, why) = rules.PickFallbackOutput(snapshot, recent);
            if (fallback is null)
            {
                decisions.Add(new NoActionDecision($"{cause} while in use, but no usable output is left; routing unchanged"));
            }
            else
            {
                fallbackOutput = fallback;
                var reason = $"{cause} while in use → fall back to {rules.DisplayName(fallback)} ({why})";
                decisions.Add(new AutoSwitchDecision(rules.PlanSwitchTo(fallback, snapshot, reason, current), cause, reason));
            }
        }

        var departedMics = changes.Where(c => c is { Kind: DeviceChangeKind.Departed, Endpoint.Flow: EndpointFlow.Capture }).ToList();
        var lostMic = departedMics.FirstOrDefault(c => current.UsesMic(c.Endpoint.Id))
                      ?? (current.MicBroken(snapshot) ? departedMics.FirstOrDefault() : null);
        if (lostMic is not null && fallbackOutput is null) // an output fallback already picks a mic; otherwise fix the mic alone
            decisions.Add(DecideMicFallback(lostMic, snapshot, current));

        foreach (var arrived in changes.Where(c => c is { Kind: DeviceChangeKind.Arrived, Endpoint.Flow: EndpointFlow.Render }))
        {
            var device = arrived.Endpoint;
            var name = rules.DisplayName(device);
            if (device.Id == fallbackOutput?.Id)
                continue; // already switching there automatically
            var reason = $"{name} {arrived.Reason}";
            if (!current.Known)
            {
                decisions.Add(new NoActionDecision($"{reason}, but GG/Sonar isn't reachable, so it's unknown whether Sonar is in use; not asking"));
                continue;
            }
            var plan = rules.PlanSwitchTo(device, snapshot, reason, current);
            if (current.AlreadyMatches(plan))
            {
                decisions.Add(new NoActionDecision($"{reason}, but audio and mic already go there"));
                continue;
            }
            decisions.Add(new PromptDecision(device, name, plan, rules.ProfileFor(device)?.DisconnectWhenDeclined ?? false, reason));
        }
        return decisions;
    }

    /// <summary>
    /// At startup (no popups): fix routing that points at something unusable (e.g. the headset is off), and disconnect
    /// devices that are connected but not in use and whose profile disconnects them when declined (e.g. a shared headset).
    /// </summary>
    public IReadOnlyList<RoutingDecision> DecideAtStartup(AvailabilitySnapshot snapshot, CurrentRouting current, IReadOnlyList<string> recent)
    {
        var decisions = new List<RoutingDecision>();
        var repairTarget = DecideRepair(decisions, snapshot, current, recent, "at startup");

        foreach (var output in snapshot.Outputs)
        {
            if (rules.ProfileFor(output) is not { DisconnectWhenDeclined: true } profile) continue;
            if (output.Id == repairTarget?.Id || (repairTarget is null && current.UsesOutput(output.Id))) continue;
            decisions.Add(new DisconnectDecision(output, profile.Name, $"{profile.Name} is connected at startup but not in use"));
        }
        return decisions;
    }

    /// <summary>
    /// After Sonar switched between classic and stream mode: each mode keeps its own devices, so the new mode may route
    /// to a cleared or unusable device (stream mode that was never set up has none at all). Repair it like at startup,
    /// but disconnect nothing: what is "in use" just changed under the user's hands.
    /// </summary>
    /// <param name="when">For the log, e.g. "when Sonar came back"; by default "after switching to … mode".</param>
    public IReadOnlyList<RoutingDecision> DecideAfterModeChange(AvailabilitySnapshot snapshot, CurrentRouting current, IReadOnlyList<string> recent,
        string? when = null)
    {
        var decisions = new List<RoutingDecision>();
        DecideRepair(decisions, snapshot, current, recent, when ?? $"after switching to {current.Mode.ToString().ToLowerInvariant()} mode");
        return decisions;
    }

    /// <summary>Adds the repair for routing that points at an unusable output or mic; returns the new output, if any.</summary>
    AudioEndpoint? DecideRepair(List<RoutingDecision> decisions, AvailabilitySnapshot snapshot, CurrentRouting current,
        IReadOnlyList<string> recent, string when)
    {
        if (current.OutputBroken(snapshot))
        {
            var problem = current.WindowsOnly ? "Windows has no default output" : "Sonar routes to an unusable output";
            var (fallback, why) = rules.PickFallbackOutput(snapshot, recent);
            if (fallback is null)
            {
                decisions.Add(new NoActionDecision($"{when} {problem}, but no usable output exists"));
                return null;
            }
            const string cause = "the previous device is unavailable";
            var reason = $"{when} {problem} → {rules.DisplayName(fallback)} ({why})";
            decisions.Add(new AutoSwitchDecision(rules.PlanSwitchTo(fallback, snapshot, reason, current), cause, reason));
            return fallback;
        }
        if (current.MicBroken(snapshot))
        {
            var micId = current.Mic ?? "";
            decisions.Add(DecideMicFallback(new DeviceChange(DeviceChangeKind.Departed,
                new AudioEndpoint(micId, "the previous mic", micId, EndpointFlow.Capture, ""), $"is unavailable {when}"), snapshot, current));
        }
        return null;
    }

    RoutingDecision DecideMicFallback(DeviceChange lostMic, AvailabilitySnapshot snapshot, CurrentRouting current)
    {
        var playingOn = snapshot.Outputs.FirstOrDefault(o => o.Id == current.Output);
        var (mic, why) = playingOn is not null ? rules.PickMic(playingOn, snapshot)
            : snapshot.Mics.FirstOrDefault() is { } first ? (first, "first usable mic")
            : (null, "no usable mic is left");
        var cause = $"{lostMic.Endpoint.Name} {lostMic.Reason}";
        if (mic is null) return new NoActionDecision($"{cause} while in use, but {why}");
        var reason = $"{cause} while in use → {mic.Name} ({why})";
        return new AutoSwitchDecision(new SwitchPlan(null, null, [], mic, reason) { WindowsOnly = current.WindowsOnly }, cause, reason);
    }
}
