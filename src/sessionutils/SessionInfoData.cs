using System.Text.Json;

namespace SessionAutomation
{
    /// <summary>Shared serializer options for <see cref="SessionInfoData"/>.</summary>
    internal static class SessionJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions();
    }

    /// <summary>
    /// A single Windows session, flattened into Pega-mappable properties. Produced
    /// internally by <see cref="SessionUtils.EnumerateSessionsJson"/> - not itself a public
    /// method return type, since Pega Robot Studio prefers scalars/JSON over complex objects.
    /// </summary>
    public class SessionInfoData
    {
        /// <summary>The session ID.</summary>
        public int SessionId { get; internal set; }

        /// <summary>The session's window station name, e.g. "Console", "RDP-Tcp#0", or "Services".</summary>
        public string WinStationName { get; internal set; }

        /// <summary>The session's connection state.</summary>
        public SessionConnectState ConnectState { get; internal set; }

        /// <summary>The logged-on user name, or an empty string if none (e.g. the Session 0 service session).</summary>
        public string UserName { get; internal set; }

        /// <summary>The logged-on user's domain, or an empty string if none.</summary>
        public string DomainName { get; internal set; }

        /// <summary>Console, RDP, Service, or Other.</summary>
        public SessionKind Kind { get; internal set; }
    }
}
