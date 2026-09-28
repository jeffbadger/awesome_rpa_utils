using System.Collections.Generic;
using ServiceAutomation;

namespace Exerciser.Menus
{
    /// <summary>
    /// ServiceUtils' menu. Point every entry at the disposable "ZZTestSvc"
    /// service created by the Setup/Cleanup menu's ServiceUtils entry - never
    /// at a real system service (TESTING.md's explicit warning). Simple-suffixed
    /// and *Delimited wrapper overloads are omitted; their fuller siblings cover
    /// the same ground with more output.
    /// </summary>
    internal static class ServiceMenu
    {
        internal static MenuItem[] Build(ServiceUtils service)
        {
            return new[]
            {
                new MenuItem("IsServiceInstalled", "true for the test service; false + message for a made-up name.", () =>
                {
                    string name = Prompt.String("Service name", "ZZTestSvc");
                    bool ok = service.IsServiceInstalled(name, out bool querySucceeded, out string message);
                    Report.Result(ok, message, ("querySucceeded", querySucceeded));
                }),
                new MenuItem("IsRunning", "true after StartService; false after StopService.", () =>
                {
                    string name = Prompt.String("Service name", "ZZTestSvc");
                    bool ok = service.IsRunning(name, out bool querySucceeded, out string message);
                    Report.Result(ok, message, ("querySucceeded", querySucceeded));
                }),
                new MenuItem("TryGetStatus", "Assert against the test service's actual state after each Start/Stop/Pause/Resume below.", () =>
                {
                    string name = Prompt.String("Service name", "ZZTestSvc");
                    bool ok = service.TryGetStatus(name, out ServiceStatus status, out string message);
                    Report.Result(ok, message, ("status", status));
                }),
                new MenuItem("TryGetStartType", "Assert against each type set via SetStartType below, especially AutomaticDelayedStart.", () =>
                {
                    string name = Prompt.String("Service name", "ZZTestSvc");
                    bool ok = service.TryGetStartType(name, out ServiceStartType startType, out string message);
                    Report.Result(ok, message, ("startType", startType));
                }),
                new MenuItem("ListServiceNames", "No input. Assert the test service's name appears.", () =>
                {
                    List<string> names = service.ListServiceNames();
                    Report.Result(true, null, ("names (count)", names.Count), ("names", names));
                }),
                new MenuItem("FindServiceNamesByDisplayName", "Exact vs substring cases; empty filter returns an empty list, not every service.", () =>
                {
                    string displayName = Prompt.String("Display name filter", "");
                    bool exactMatch = Prompt.Bool("Exact match", true);
                    List<string> names = service.FindServiceNamesByDisplayName(displayName, exactMatch);
                    Report.Result(true, null, ("names", names));
                }),
                new MenuItem("TryFindFirstServiceNameByDisplayName", "Exact vs substring cases.", () =>
                {
                    string displayName = Prompt.String("Display name", "");
                    bool exactMatch = Prompt.Bool("Exact match", true);
                    bool ok = service.TryFindFirstServiceNameByDisplayName(displayName, out string serviceName, out string message, exactMatch);
                    Report.Result(ok, message, ("serviceName", serviceName));
                }),
                new MenuItem("StartService", "Idempotent case: run this again on an already-running service and confirm wasAlreadyRunning + an 'already' note.", () =>
                {
                    string name = Prompt.String("Service name", "ZZTestSvc");
                    int timeoutMs = Prompt.Int("Timeout ms", 10000);
                    bool ok = service.StartService(name, timeoutMs, out bool wasAlreadyRunning, out string message);
                    Report.Result(ok, message, ("wasAlreadyRunning", wasAlreadyRunning));
                }),
                new MenuItem("StopService", "Idempotent case: run this again on an already-stopped service and confirm wasAlreadyStopped + an 'already' note.", () =>
                {
                    string name = Prompt.String("Service name", "ZZTestSvc");
                    int timeoutMs = Prompt.Int("Timeout ms", 10000);
                    bool ok = service.StopService(name, timeoutMs, out bool wasAlreadyStopped, out string message);
                    Report.Result(ok, message, ("wasAlreadyStopped", wasAlreadyStopped));
                }),
                new MenuItem("RestartService", "A stop-phase failure must skip the start phase and report that failure in the message.", () =>
                {
                    string name = Prompt.String("Service name", "ZZTestSvc");
                    int timeoutMs = Prompt.Int("Timeout ms", 10000);
                    bool ok = service.RestartService(name, timeoutMs, out bool wasAlreadyStopped, out string message);
                    Report.Result(ok, message, ("wasAlreadyStopped", wasAlreadyStopped));
                }),
                new MenuItem("PauseService", "Only works if the service supports SERVICE_ACCEPT_PAUSE_CONTINUE - NullService does (CanPauseAndContinue = true). Also covers the idempotent already-paused case.", () =>
                {
                    string name = Prompt.String("Service name", "ZZTestSvc");
                    bool ok = service.PauseService(name, out bool wasAlreadyPaused, out string message);
                    Report.Result(ok, message, ("wasAlreadyPaused", wasAlreadyPaused));
                }),
                new MenuItem("ResumeService", "Also covers the idempotent already-running case.", () =>
                {
                    string name = Prompt.String("Service name", "ZZTestSvc");
                    bool ok = service.ResumeService(name, out bool wasAlreadyRunning, out string message);
                    Report.Result(ok, message, ("wasAlreadyRunning", wasAlreadyRunning));
                }),
                new MenuItem("WaitForServiceStatus", "Found-in-time and timeout cases; negative timeoutMs returns false + message immediately.", () =>
                {
                    string name = Prompt.String("Service name", "ZZTestSvc");
                    ServiceStatus expected = Prompt.Enum<ServiceStatus>("Expected status");
                    int timeoutMs = Prompt.Int("Timeout ms", 10000);
                    bool ok = service.WaitForServiceStatus(name, expected, timeoutMs, out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("SetStartType", "All 6 ServiceStartType values; verify AutomaticDelayedStart -> Automatic actually clears the delayed flag via 'sc qc <name>', not just via TryGetStartType (same component under test).", () =>
                {
                    string name = Prompt.String("Service name", "ZZTestSvc");
                    ServiceStartType startType = Prompt.Enum<ServiceStartType>("Start type");
                    bool ok = service.SetStartType(name, startType, out string message);
                    Report.Result(ok, message);
                })
            };
        }
    }
}
