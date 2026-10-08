using System.Net;

namespace AudioSwitch.Core.Sonar;

/// <summary>Why there is no Sonar to talk to, or <see cref="Running"/> when there is. Decides whether AudioSwitch manages
/// Sonar or only the Windows defaults (<see cref="Routing.SwitchingModeRule"/>).</summary>
public enum SonarPresence
{
    /// <summary>Sonar answered.</summary>
    Running,
    /// <summary>GG or Sonar didn't answer: not started yet, restarting, or closed. Ambiguous.</summary>
    NotReachable,
    /// <summary>GG answered and says Sonar is enabled but not running/ready yet.</summary>
    Starting,
    /// <summary>GG answered and says Sonar is turned off (or GG has no Sonar at all).</summary>
    Disabled,
    /// <summary>Neither GG's uninstall entry nor its coreProps.json exists.</summary>
    NotInstalled,
}

/// <summary>GG or its Sonar sub-app is not running/ready, so there is no Sonar web server to talk to.
/// <see cref="Presence"/> says why, as far as it is known.</summary>
public sealed class SonarUnavailableException(string message, Exception? inner = null, SonarPresence presence = SonarPresence.NotReachable)
    : Exception(message, inner)
{
    public SonarPresence Presence { get; } = presence;
}

/// <summary>Sonar answered, but rejected the request (e.g. HTTP 400 "no device found with id ...").</summary>
public sealed class SonarApiException(HttpStatusCode statusCode, string route, string body)
    : Exception($"Sonar returned {(int)statusCode} for {route}: {body}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Body { get; } = body;
}
