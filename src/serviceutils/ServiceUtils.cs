using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace ServiceAutomation
{
    /// <summary>
    /// A Windows service startup type, unifying <see cref="ServiceStartMode"/> with the
    /// separate delayed-auto-start flag that mode doesn't expose.
    /// </summary>
    public enum ServiceStartType
    {
        /// <summary>Started by the boot loader (SERVICE_BOOT_START). Driver services only.</summary>
        Boot,
        /// <summary>Started by the I/O subsystem during kernel init (SERVICE_SYSTEM_START). Driver services only.</summary>
        System,
        /// <summary>Started automatically at system startup (SERVICE_AUTO_START).</summary>
        Automatic,
        /// <summary>Started automatically, shortly after other auto-start services (SERVICE_AUTO_START + delayed-auto-start flag).</summary>
        AutomaticDelayedStart,
        /// <summary>Started only on demand (SERVICE_DEMAND_START).</summary>
        Manual,
        /// <summary>Cannot be started (SERVICE_DISABLED).</summary>
        Disabled
    }

    /// <summary>
    /// Pega Robot Studio-ready component that queries, starts, stops, restarts, pauses/
    /// resumes, and configures the startup type of Windows services.
    /// <para>
    /// Like every component in this suite, all methods honor the never-throws contract:
    /// invalid input (a null/empty name, a negative timeout, an undefined
    /// <see cref="ServiceStartType"/>) and runtime failures (a missing service, a
    /// non-elevated runtime, a request the service rejects) return <c>false</c> with a
    /// descriptive message instead of throwing. Timeouts are likewise
    /// <c>false</c> returns - a slow service is a normal, checkable outcome.
    /// </para>
    /// </summary>
    [Description("Queries, starts, stops, restarts, and configures Windows services. " +
                 "All methods return True/False with a failure message instead of throwing. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class ServiceUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public ServiceUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public ServiceUtils(IContainer container)
        {
            container?.Add(this);
        }

        /// <summary>Win32 ERROR_SERVICE_DOES_NOT_EXIST, so a missing service reads as a plain-language message
        /// rather than ServiceController's generic "Cannot open service ... on computer '.'.".</summary>
        private const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;

        #region Query

        /// <summary>
        /// Returns <c>true</c> if a service with the given name is installed. Never throws.
        /// </summary>
        /// <param name="serviceName">The service name (not display name) to check.</param>
        /// <param name="message"><c>null</c> when the service is installed; otherwise a human-readable reason it
        /// isn't - the "not installed" answer, an invalid name, or an SCM query failure.</param>
        /// <returns><c>true</c> if the service is installed; <c>false</c> if it isn't, the name is null/empty,
        /// or the Service Control Manager couldn't be queried. Never throws.</returns>
        [Category("Service - Query")]
        [Description("Returns True if a service with the given name is installed. Never throws.")]
        public bool IsServiceInstalled(string serviceName, out string message)
        {
            bool installed = TryGetStatus(serviceName, out _, out string statusMessage);
            // A missing service is the *answer* here, not a failure - only surface the
            // message for genuinely broken cases (invalid name, SCM unavailable).
            message = installed ? null : statusMessage;
            return installed;
        }

        /// <summary>
        /// Returns <c>true</c> if a service with the given name is installed and currently running.
        /// <para>
        /// A <c>false</c> return with a null <paramref name="message"/> means the service is installed
        /// but not running; a <c>false</c> with a non-null message means the check itself failed
        /// (invalid name, service doesn't exist). Unlike <see cref="TryGetStatus"/>, this does not
        /// throw for a service that isn't installed.
        /// </para>
        /// Never throws.
        /// </summary>
        /// <param name="serviceName">The service name (not display name) to check.</param>
        /// <param name="message"><c>null</c> when the check succeeded; otherwise a human-readable reason it failed.</param>
        /// <returns><c>true</c> if the service is installed and running; <c>false</c> if it's stopped, the name
        /// is null/empty, or no such service is installed. Never throws.</returns>
        [Category("Service - Query")]
        [Description("Returns True if a service with the given name is installed and currently running. Never throws.")]
        public bool IsRunning(string serviceName, out string message)
        {
            if (IsNullOrEmpty(serviceName))
            {
                message = "A service name is required.";
                return false;
            }

            if (!TryGetStatus(serviceName, out ServiceControllerStatus status, out message))
                return false; // message already set (missing service, SCM failure)

            message = null; // an installed-but-stopped service is a valid answer, not an error
            return status == ServiceControllerStatus.Running;
        }

        /// <summary>Gets a service's current status. Never throws.</summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="status">The service's current status when returning <c>true</c>; <c>Stopped</c> (the default) otherwise.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> if the status was read; <c>false</c> if the name is null/empty, no service with
        /// that name is installed, or the Service Control Manager couldn't be queried. Never throws.</returns>
        [Category("Service - Query")]
        [Description("Gets a service's current status. Returns False with a message (not an exception) on failure.")]
        public bool TryGetStatus(string serviceName, out ServiceControllerStatus status, out string message)
        {
            status = ServiceControllerStatus.Stopped;
            if (IsNullOrEmpty(serviceName))
            {
                message = "A service name is required.";
                return false;
            }

            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    status = sc.Status;
                    message = null;
                    return true;
                }
            }
            catch (InvalidOperationException ex)
            {
                message = DescribeServiceException(ex, serviceName, "Reading the service status");
                return false;
            }
            catch (Win32Exception ex)
            {
                // sc.Status is documented to throw Win32Exception (e.g. access denied
                // querying a protected service) - surfaces as false + message instead
                // of an exception (never-throws contract).
                message = $"Reading the service status failed for service '{serviceName}': {ex.Message}";
                return false;
            }
        }

        /// <summary>Gets a service's configured startup type. Never throws.</summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="startType">The service's configured startup type when returning <c>true</c>; <c>ServiceStartType.Manual</c> (the default) otherwise.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the query failed.</param>
        /// <returns><c>true</c> if the startup type was read; <c>false</c> if the name is null/empty, no service with
        /// that name is installed, or the underlying query failed. Never throws.</returns>
        [Category("Service - Query")]
        [Description("Gets a service's configured startup type. Returns False with a message (not an exception) on failure.")]
        public bool TryGetStartType(string serviceName, out ServiceStartType startType, out string message)
        {
            startType = ServiceStartType.Manual;
            if (IsNullOrEmpty(serviceName))
            {
                message = "A service name is required.";
                return false;
            }

            try
            {
                ServiceStartMode mode;
                using (var sc = new ServiceController(serviceName))
                {
                    mode = sc.StartType;
                }

                if (mode == ServiceStartMode.Automatic)
                {
                    if (!TryGetDelayedAutoStart(serviceName, out bool delayed, out message))
                        return false;
                    if (delayed)
                    {
                        startType = ServiceStartType.AutomaticDelayedStart;
                        message = null;
                        return true;
                    }
                }

                switch (mode)
                {
                    case ServiceStartMode.Boot: startType = ServiceStartType.Boot; break;
                    case ServiceStartMode.System: startType = ServiceStartType.System; break;
                    case ServiceStartMode.Automatic: startType = ServiceStartType.Automatic; break;
                    case ServiceStartMode.Manual: startType = ServiceStartType.Manual; break;
                    case ServiceStartMode.Disabled: startType = ServiceStartType.Disabled; break;
                    default:
                        message = $"Service '{serviceName}' reports an unrecognized start mode: {mode}.";
                        return false;
                }

                message = null;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                message = DescribeServiceException(ex, serviceName, "Reading the service startup type");
                return false;
            }
            catch (Win32Exception ex)
            {
                message = $"Reading the service startup type failed for service '{serviceName}': {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Gets the service names of every installed service. Returns an empty list if the
        /// Service Control Manager couldn't be queried (matching <c>GetTopLevelWindows</c>'s
        /// empty-on-failure convention). Never throws.
        /// </summary>
        [Category("Service - Query")]
        [Description("Gets the service names of every installed service. Returns an empty list on failure; never throws.")]
        public List<string> ListServiceNames()
        {
            var results = new List<string>();
            try
            {
                foreach (ServiceController sc in ServiceController.GetServices())
                {
                    using (sc)
                    {
                        results.Add(sc.ServiceName);
                    }
                }
            }
            catch (Exception)
            {
                // Enumeration is best-effort; an empty list is the "couldn't enumerate" answer.
            }
            return results;
        }

        /// <summary>
        /// Finds the service name(s) of installed services matching a display name. A null or
        /// empty <paramref name="displayName"/> matches nothing (returns an empty list), never
        /// throwing and never matching every service. Returns an empty list on enumeration
        /// failure too. Never throws.
        /// </summary>
        /// <param name="displayName">The display name to match.</param>
        /// <param name="exactMatch">If <c>true</c> (default), requires an exact match. If <c>false</c>, matches any service whose display name contains <paramref name="displayName"/> (case-insensitive).</param>
        /// <returns>The matching service name(s) (empty if none match or the query failed).</returns>
        [Category("Service - Query")]
        [Description("Finds the service name(s) of installed services matching a display name (exact or substring match). Never throws.")]
        public List<string> FindServiceNamesByDisplayName(string displayName, bool exactMatch = true)
        {
            var results = new List<string>();
            if (IsNullOrEmpty(displayName))
                return results; // a null/empty filter matches nothing - not every service

            try
            {
                foreach (ServiceController sc in ServiceController.GetServices())
                {
                    using (sc)
                    {
                        bool matches = exactMatch
                            ? string.Equals(sc.DisplayName, displayName, StringComparison.Ordinal)
                            : sc.DisplayName.IndexOf(displayName, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (matches)
                            results.Add(sc.ServiceName);
                    }
                }
            }
            catch (Exception)
            {
                // Enumeration is best-effort; an empty list is the "couldn't enumerate" answer.
            }
            return results;
        }

        #endregion

        #region Control

        /// <summary>
        /// Starts a service and waits for it to reach <see cref="ServiceControllerStatus.Running"/>.
        /// Idempotent: starting an already-running (or already starting) service reports success.
        /// Never throws.
        /// </summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="timeoutMs">Maximum time to wait for it to reach Running, in milliseconds. 0 means "don't wait".</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the start failed.
        /// May also be a non-null note with a <c>true</c> return, e.g. reporting the service was already running.</param>
        /// <returns><c>true</c> if the service is Running (or reached Running within the timeout); <c>false</c> if
        /// the name is null/empty, <paramref name="timeoutMs"/> is negative, no such service is installed, the
        /// runtime isn't elevated, or it didn't reach Running within the timeout. Never throws.</returns>
        [Category("Service - Control")]
        [Description("Starts a service and waits for it to reach Running. Idempotent; returns False with a message (not an exception) on failure or timeout.")]
        public bool StartService(string serviceName, int timeoutMs, out string message)
        {
            if (IsNullOrEmpty(serviceName))
            {
                message = "A service name is required.";
                return false;
            }
            if (timeoutMs < 0)
            {
                message = "timeoutMs must be non-negative.";
                return false;
            }

            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    return StartAndWait(sc, serviceName, timeoutMs, out message);
                }
            }
            catch (InvalidOperationException ex)
            {
                message = DescribeServiceException(ex, serviceName, "Starting the service");
                return false;
            }
            catch (Win32Exception ex)
            {
                message = $"Starting the service failed for service '{serviceName}': {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Stops a service and waits for it to reach <see cref="ServiceControllerStatus.Stopped"/>.
        /// Idempotent: stopping an already-stopped (or already stopping) service reports success.
        /// Never throws.
        /// </summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="timeoutMs">Maximum time to wait for it to reach Stopped, in milliseconds. 0 means "don't wait".</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the stop failed.
        /// May also be a non-null note with a <c>true</c> return, e.g. reporting the service was already stopped.</param>
        /// <returns><c>true</c> if the service is Stopped (or reached Stopped within the timeout); <c>false</c> if
        /// the name is null/empty, <paramref name="timeoutMs"/> is negative, no such service is installed, the
        /// runtime isn't elevated, the service doesn't support being stopped, or it didn't reach Stopped within
        /// the timeout. Never throws.</returns>
        [Category("Service - Control")]
        [Description("Stops a service and waits for it to reach Stopped. Idempotent; returns False with a message (not an exception) on failure or timeout.")]
        public bool StopService(string serviceName, int timeoutMs, out string message)
        {
            if (IsNullOrEmpty(serviceName))
            {
                message = "A service name is required.";
                return false;
            }
            if (timeoutMs < 0)
            {
                message = "timeoutMs must be non-negative.";
                return false;
            }

            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    return StopAndWait(sc, serviceName, timeoutMs, out message);
                }
            }
            catch (InvalidOperationException ex)
            {
                message = DescribeServiceException(ex, serviceName, "Stopping the service");
                return false;
            }
            catch (Win32Exception ex)
            {
                message = $"Stopping the service failed for service '{serviceName}': {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Stops then starts a service. Each phase gets its own <paramref name="timeoutMs"/>
        /// budget, so a full restart can take up to roughly twice that long. If the stop phase
        /// fails or times out, the start phase is skipped (starting a service that never stopped
        /// would throw) and the failure is reported. Already-stopped and already-running services
        /// make the corresponding phase a no-op. Never throws.
        /// </summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="timeoutMs">Maximum time to wait for each phase (stop, then start) to complete, in milliseconds. 0 means "don't wait".</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the restart failed
        /// (from whichever phase failed).</param>
        /// <returns><c>true</c> only if the service is Running after both phases; <c>false</c> if the name is
        /// null/empty, <paramref name="timeoutMs"/> is negative, no such service is installed, the runtime isn't
        /// elevated, or either phase failed or timed out. Never throws.</returns>
        [Category("Service - Control")]
        [Description("Stops then starts a service, skipping the start phase if the stop phase fails. Each phase gets its own timeoutMs budget. Never throws.")]
        public bool RestartService(string serviceName, int timeoutMs, out string message)
        {
            if (IsNullOrEmpty(serviceName))
            {
                message = "A service name is required.";
                return false;
            }
            if (timeoutMs < 0)
            {
                message = "timeoutMs must be non-negative.";
                return false;
            }

            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    if (!StopAndWait(sc, serviceName, timeoutMs, out string stopMessage))
                    {
                        // Skip the start phase when the stop phase failed - Start() on a
                        // never-stopped service is the documented throw we're contracted
                        // not to produce.
                        message = stopMessage;
                        return false;
                    }

                    return StartAndWait(sc, serviceName, timeoutMs, out message);
                }
            }
            catch (InvalidOperationException ex)
            {
                message = DescribeServiceException(ex, serviceName, "Restarting the service");
                return false;
            }
            catch (Win32Exception ex)
            {
                message = $"Restarting the service failed for service '{serviceName}': {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Pauses a running service. Idempotent: pausing an already-paused service reports success.
        /// Returns as soon as the pause is requested (it does not wait for the service to reach
        /// Paused - pair with <see cref="WaitForServiceStatus"/> for that). Never throws.
        /// </summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the pause failed.
        /// May also be a non-null note with a <c>true</c> return, e.g. reporting the service was already paused.</param>
        /// <returns><c>true</c> if the service is paused (or the pause was accepted); <c>false</c> if the name is
        /// null/empty, no such service is installed, the service doesn't support pause/continue, or the runtime
        /// isn't elevated. Never throws.</returns>
        [Category("Service - Control")]
        [Description("Pauses a running service. Idempotent; returns False with a message (not an exception) on failure.")]
        public bool PauseService(string serviceName, out string message)
        {
            if (IsNullOrEmpty(serviceName))
            {
                message = "A service name is required.";
                return false;
            }

            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    if (sc.Status == ServiceControllerStatus.Paused)
                    {
                        message = $"Service '{serviceName}' is already paused.";
                        return true;
                    }

                    sc.Pause();
                    message = null;
                    return true;
                }
            }
            catch (InvalidOperationException ex)
            {
                message = DescribeServiceException(ex, serviceName, "Pausing the service");
                return false;
            }
            catch (Win32Exception ex)
            {
                message = $"Pausing the service failed for service '{serviceName}': {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Resumes a paused service. Idempotent: resuming an already-running service reports
        /// success. Returns as soon as the resume is requested (it does not wait for the service
        /// to reach Running - pair with <see cref="WaitForServiceStatus"/> for that). Never throws.
        /// </summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the resume failed.
        /// May also be a non-null note with a <c>true</c> return, e.g. reporting the service was already running.</param>
        /// <returns><c>true</c> if the service is running (or the resume was accepted); <c>false</c> if the name is
        /// null/empty, no such service is installed, the service doesn't support pause/continue, or the runtime
        /// isn't elevated. Never throws.</returns>
        [Category("Service - Control")]
        [Description("Resumes a paused service. Idempotent; returns False with a message (not an exception) on failure.")]
        public bool ResumeService(string serviceName, out string message)
        {
            if (IsNullOrEmpty(serviceName))
            {
                message = "A service name is required.";
                return false;
            }

            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        message = $"Service '{serviceName}' is already running.";
                        return true;
                    }

                    sc.Continue();
                    message = null;
                    return true;
                }
            }
            catch (InvalidOperationException ex)
            {
                message = DescribeServiceException(ex, serviceName, "Resuming the service");
                return false;
            }
            catch (Win32Exception ex)
            {
                message = $"Resuming the service failed for service '{serviceName}': {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Polls for a service to reach the given status until it does, or the timeout
        /// elapses. Unlike this repo's other <c>WaitForX</c> methods, there is no poll
        /// interval parameter - the underlying <c>ServiceController.WaitForStatus</c> blocks
        /// natively against the Service Control Manager rather than requiring a hand-rolled
        /// poll loop. Never throws.
        /// </summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="expectedStatus">The status to wait for.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds. 0 returns immediately (success only if the service is already in the expected status).</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the wait failed or timed out.</param>
        /// <returns><c>true</c> if the service reached <paramref name="expectedStatus"/> within the timeout; <c>false</c> if the name is null/empty, <paramref name="timeoutMs"/> is negative, no such service is installed, or it timed out. Never throws.</returns>
        [Category("Service - Control")]
        [Description("Polls for a service to reach the given status until it does, or the timeout elapses. Returns False with a message (not an exception) on timeout.")]
        public bool WaitForServiceStatus(string serviceName, ServiceControllerStatus expectedStatus, int timeoutMs, out string message)
        {
            if (IsNullOrEmpty(serviceName))
            {
                message = "A service name is required.";
                return false;
            }
            if (timeoutMs < 0)
            {
                message = "timeoutMs must be non-negative.";
                return false;
            }

            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    return WaitForStatusInternal(sc, expectedStatus, timeoutMs, out message);
                }
            }
            catch (InvalidOperationException ex)
            {
                message = DescribeServiceException(ex, serviceName, "Waiting for the service status");
                return false;
            }
            catch (Win32Exception ex)
            {
                // WaitForStatus polls via Refresh()/Status internally, both of which can
                // throw Win32Exception - surfaces as false + message (never-throws contract).
                message = $"Waiting for the service status failed for service '{serviceName}': {ex.Message}";
                return false;
            }
        }

        #endregion

        #region Configuration

        /// <summary>
        /// Sets a service's startup type. Unlike every other method in this component, this
        /// uses direct Win32 (<c>advapi32.dll</c>) calls rather than <c>ServiceController</c>,
        /// because <c>ServiceController</c> has no method to change startup type at all.
        /// Never throws.
        /// </summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="startType">The startup type to set.</param>
        /// <param name="message"><c>null</c> on success; otherwise a human-readable reason the change failed
        /// (invalid name, undefined <paramref name="startType"/>, access denied, or no such service).</param>
        /// <returns><c>true</c> if the startup type was changed; <c>false</c> if the name is null/empty,
        /// <paramref name="startType"/> is not a defined value, the runtime isn't elevated, or the underlying
        /// Win32 call failed. Never throws.</returns>
        [Category("Service - Configuration")]
        [Description("Sets a service's startup type, including delayed-auto-start. Returns False with a message (not an exception) on failure.")]
        public bool SetStartType(string serviceName, ServiceStartType startType, out string message)
        {
            if (IsNullOrEmpty(serviceName))
            {
                message = "A service name is required.";
                return false;
            }
            if (!TryToWin32StartType(startType, out uint dwStartType, out string typeMessage))
            {
                message = typeMessage;
                return false;
            }

            bool delayed = startType == ServiceStartType.AutomaticDelayedStart;

            IntPtr hScm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (hScm == IntPtr.Zero)
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "OpenSCManager failed.").Message;
                return false;
            }

            try
            {
                IntPtr hService = OpenService(hScm, serviceName, SERVICE_CHANGE_CONFIG);
                if (hService == IntPtr.Zero)
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), $"OpenService failed for '{serviceName}'.").Message;
                    return false;
                }

                try
                {
                    if (!ChangeServiceConfig(hService, SERVICE_NO_CHANGE, dwStartType, SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null))
                    {
                        message = new Win32Exception(Marshal.GetLastWin32Error(), "ChangeServiceConfig failed.").Message;
                        return false;
                    }

                    // Explicitly set the delayed-auto-start flag either way - not calling
                    // ChangeServiceConfig2 at all for the "normal" cases would leave a
                    // previously-set delayed flag stale when switching away from
                    // AutomaticDelayedStart back to plain Automatic (or to Manual/Disabled).
                    var info = new SERVICE_DELAYED_AUTO_START_INFO { fDelayedAutostart = delayed };
                    if (!ChangeServiceConfig2(hService, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref info))
                    {
                        message = new Win32Exception(Marshal.GetLastWin32Error(), "ChangeServiceConfig2 failed.").Message;
                        return false;
                    }

                    message = null;
                    return true;
                }
                finally
                {
                    CloseServiceHandle(hService);
                }
            }
            finally
            {
                CloseServiceHandle(hScm);
            }
        }

        #endregion

        #region Internal Helpers

        private const uint SC_MANAGER_CONNECT = 0x0001;
        private const uint SERVICE_QUERY_CONFIG = 0x0001;
        private const uint SERVICE_CHANGE_CONFIG = 0x0002;
        private const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;
        private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
        private const uint SERVICE_BOOT_START = 0;
        private const uint SERVICE_SYSTEM_START = 1;
        private const uint SERVICE_AUTO_START = 2;
        private const uint SERVICE_DEMAND_START = 3;
        private const uint SERVICE_DISABLED = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_DELAYED_AUTO_START_INFO
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool fDelayedAutostart;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(string lpMachineName, string lpDatabaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryServiceConfig2(IntPtr hService, uint dwInfoLevel, IntPtr buffer, uint bufferSize, out uint bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeServiceConfig(
            IntPtr hService,
            uint dwServiceType,
            uint dwStartType,
            uint dwErrorControl,
            string lpBinaryPathName,
            string lpLoadOrderGroup,
            IntPtr lpdwTagId,
            string lpDependencies,
            string lpServiceStartName,
            string lpPassword,
            string lpDisplayName);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeServiceConfig2(IntPtr hService, uint dwInfoLevel, ref SERVICE_DELAYED_AUTO_START_INFO lpInfo);

        /// <summary>True for the null/empty input shapes every guard in this class rejects.</summary>
        private static bool IsNullOrEmpty(string value)
        {
            return string.IsNullOrEmpty(value);
        }

        /// <summary>
        /// Maps the designer-friendly <see cref="ServiceStartType"/> to the raw Win32 dwStartType
        /// value. Undefined enum values report failure instead of throwing.
        /// </summary>
        internal static bool TryToWin32StartType(ServiceStartType startType, out uint dwStartType, out string message)
        {
            switch (startType)
            {
                case ServiceStartType.Boot: dwStartType = SERVICE_BOOT_START; break;
                case ServiceStartType.System: dwStartType = SERVICE_SYSTEM_START; break;
                case ServiceStartType.Automatic: dwStartType = SERVICE_AUTO_START; break;
                case ServiceStartType.AutomaticDelayedStart: dwStartType = SERVICE_AUTO_START; break;
                case ServiceStartType.Manual: dwStartType = SERVICE_DEMAND_START; break;
                case ServiceStartType.Disabled: dwStartType = SERVICE_DISABLED; break;
                default:
                    dwStartType = 0;
                    message = $"'{startType}' is not a defined ServiceStartType value.";
                    return false;
            }

            message = null;
            return true;
        }

        /// <summary>
        /// Waits for a service to reach a status, translating a timeout into False instead
        /// of letting the exception propagate. Note the deliberately fully-qualified
        /// <c>System.ServiceProcess.TimeoutException</c> in the catch clause below - a bare
        /// <c>TimeoutException</c> in this file resolves to <c>System.TimeoutException</c>
        /// (the BCL one), a different type from the one <c>WaitForStatus</c> actually throws.
        /// That would compile fine and silently never catch the real exception.
        /// </summary>
        private static bool WaitForStatusInternal(ServiceController sc, ServiceControllerStatus expectedStatus, int timeoutMs, out string message)
        {
            try
            {
                sc.WaitForStatus(expectedStatus, TimeSpan.FromMilliseconds(timeoutMs));
                message = null;
                return true;
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                message = $"Timed out after {timeoutMs} ms waiting for the service to reach {expectedStatus}.";
                return false;
            }
        }

        /// <summary>
        /// Idempotent start phase shared by <see cref="StartService"/> and
        /// <see cref="RestartService"/>: an already-running (or already starting) service is
        /// success, so the common automation retry case doesn't blow up. Assumes the caller
        /// has already validated <paramref name="timeoutMs"/>.
        /// </summary>
        private static bool StartAndWait(ServiceController sc, string serviceName, int timeoutMs, out string message)
        {
            var status = sc.Status;
            if (status == ServiceControllerStatus.Running)
            {
                message = $"Service '{serviceName}' is already running.";
                return true;
            }
            if (status == ServiceControllerStatus.StartPending)
                return WaitForStatusInternal(sc, ServiceControllerStatus.Running, timeoutMs, out message);

            sc.Start();
            return WaitForStatusInternal(sc, ServiceControllerStatus.Running, timeoutMs, out message);
        }

        /// <summary>
        /// Idempotent stop phase shared by <see cref="StopService"/> and
        /// <see cref="RestartService"/>: an already-stopped (or already stopping) service is
        /// success. Assumes the caller has already validated <paramref name="timeoutMs"/>.
        /// </summary>
        private static bool StopAndWait(ServiceController sc, string serviceName, int timeoutMs, out string message)
        {
            var status = sc.Status;
            if (status == ServiceControllerStatus.Stopped)
            {
                message = $"Service '{serviceName}' is already stopped.";
                return true;
            }
            if (status == ServiceControllerStatus.StopPending)
                return WaitForStatusInternal(sc, ServiceControllerStatus.Stopped, timeoutMs, out message);

            sc.Stop();
            return WaitForStatusInternal(sc, ServiceControllerStatus.Stopped, timeoutMs, out message);
        }

        /// <summary>
        /// Maps a <see cref="ServiceController"/> exception to a caller-facing message, with a
        /// plain-language rewrite for the SCM's "service does not exist" error (1060), which
        /// otherwise surfaces as ServiceController's generic "Cannot open service ... on
        /// computer '.'." wrapper.
        /// </summary>
        private static string DescribeServiceException(Exception ex, string serviceName, string operation)
        {
            if (ex.InnerException is Win32Exception w32 && w32.NativeErrorCode == ERROR_SERVICE_DOES_NOT_EXIST)
                return $"No Windows service named '{serviceName}' is installed.";

            return $"{operation} failed for service '{serviceName}': {ex.Message}";
        }

        /// <summary>
        /// Reads the delayed-auto-start flag for a service via QueryServiceConfig2. Assumes the
        /// caller already confirmed the service exists. Never throws - a failed query reports
        /// false with a message, matching the rest of this component.
        /// </summary>
        private static bool TryGetDelayedAutoStart(string serviceName, out bool delayed, out string message)
        {
            delayed = false;
            IntPtr hScm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (hScm == IntPtr.Zero)
            {
                message = new Win32Exception(Marshal.GetLastWin32Error(), "OpenSCManager failed.").Message;
                return false;
            }

            try
            {
                IntPtr hService = OpenService(hScm, serviceName, SERVICE_QUERY_CONFIG);
                if (hService == IntPtr.Zero)
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), $"OpenService failed for '{serviceName}'.").Message;
                    return false;
                }

                try
                {
                    int size = Marshal.SizeOf<SERVICE_DELAYED_AUTO_START_INFO>();
                    IntPtr buffer = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (!QueryServiceConfig2(hService, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, buffer, (uint)size, out _))
                        {
                            message = new Win32Exception(Marshal.GetLastWin32Error(), "QueryServiceConfig2 failed.").Message;
                            return false;
                        }

                        var info = Marshal.PtrToStructure<SERVICE_DELAYED_AUTO_START_INFO>(buffer);
                        delayed = info.fDelayedAutostart;
                        message = null;
                        return true;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    CloseServiceHandle(hService);
                }
            }
            finally
            {
                CloseServiceHandle(hScm);
            }
        }

        #endregion

        /// <summary>
        /// Clean-up override matching the other components in the suite - there is nothing
        /// to release, but the designer pattern calls for the override; it only forwards to
        /// the base, which detaches this component from its container's site.
        /// </summary>
        /// <param name="disposing">True when called from Dispose(); false when called from a finalizer.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // No managed or unmanaged resources to release.
            }

            // Base Component.Dispose detaches this component from its container's site.
            base.Dispose(disposing);
        }
    }
}