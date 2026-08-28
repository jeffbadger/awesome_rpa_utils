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
    /// </summary>
    [Description("Queries, starts, stops, restarts, and configures Windows services. " +
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

        #region Query

        /// <summary>Returns <c>true</c> if a service with the given name is installed.</summary>
        /// <param name="serviceName">The service name (not display name) to check.</param>
        /// <exception cref="ArgumentException"><paramref name="serviceName"/> is null or empty.</exception>
        [Category("Service - Query")]
        [Description("Returns True if a service with the given name is installed.")]
        public bool IsServiceInstalled(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentException("A service name is required.", nameof(serviceName));

            using (var sc = new ServiceController(serviceName))
            {
                try
                {
                    _ = sc.Status;
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Returns <c>true</c> if a service with the given name is installed and currently
        /// running. Unlike <see cref="GetStatus"/>, this does not throw for a service that
        /// isn't installed - it returns <c>false</c>, matching <see cref="IsServiceInstalled"/>'s
        /// safe-check convention rather than requiring a try/catch for a simple yes/no check.
        /// </summary>
        /// <param name="serviceName">The service name (not display name) to check.</param>
        /// <exception cref="ArgumentException"><paramref name="serviceName"/> is null or empty.</exception>
        [Category("Service - Query")]
        [Description("Returns True if a service with the given name is installed and currently running.")]
        public bool IsRunning(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentException("A service name is required.", nameof(serviceName));

            using (var sc = new ServiceController(serviceName))
            {
                try
                {
                    return sc.Status == ServiceControllerStatus.Running;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        /// <summary>Gets a service's current status.</summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <exception cref="ArgumentException"><paramref name="serviceName"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">No service with that name is installed.</exception>
        [Category("Service - Query")]
        [Description("Gets a service's current status.")]
        public ServiceControllerStatus GetStatus(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentException("A service name is required.", nameof(serviceName));

            using (var sc = new ServiceController(serviceName))
            {
                return sc.Status;
            }
        }

        /// <summary>Gets a service's configured startup type.</summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <exception cref="ArgumentException"><paramref name="serviceName"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">No service with that name is installed.</exception>
        /// <exception cref="Win32Exception">The delayed-auto-start query failed.</exception>
        [Category("Service - Query")]
        [Description("Gets a service's configured startup type.")]
        public ServiceStartType GetStartType(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentException("A service name is required.", nameof(serviceName));

            ServiceStartMode mode;
            using (var sc = new ServiceController(serviceName))
            {
                mode = sc.StartType;
            }

            if (mode == ServiceStartMode.Automatic && IsDelayedAutoStart(serviceName))
                return ServiceStartType.AutomaticDelayedStart;

            switch (mode)
            {
                case ServiceStartMode.Boot: return ServiceStartType.Boot;
                case ServiceStartMode.System: return ServiceStartType.System;
                case ServiceStartMode.Automatic: return ServiceStartType.Automatic;
                case ServiceStartMode.Manual: return ServiceStartType.Manual;
                case ServiceStartMode.Disabled: return ServiceStartType.Disabled;
                default: throw new InvalidOperationException($"Unrecognized service start mode: {mode}.");
            }
        }

        /// <summary>Gets the service names of every installed service.</summary>
        [Category("Service - Query")]
        [Description("Gets the service names of every installed service.")]
        public List<string> ListServiceNames()
        {
            var results = new List<string>();
            foreach (ServiceController sc in ServiceController.GetServices())
            {
                results.Add(sc.ServiceName);
                sc.Dispose();
            }
            return results;
        }

        /// <summary>Finds the service name(s) of installed services matching a display name.</summary>
        /// <param name="displayName">The display name to match.</param>
        /// <param name="exactMatch">If <c>true</c> (default), requires an exact match. If <c>false</c>, matches any service whose display name contains <paramref name="displayName"/> (case-insensitive).</param>
        /// <returns>The matching service name(s) (empty if none match).</returns>
        /// <exception cref="ArgumentException"><paramref name="displayName"/> is null or empty.</exception>
        [Category("Service - Query")]
        [Description("Finds the service name(s) of installed services matching a display name (exact or substring match).")]
        public List<string> FindServiceNamesByDisplayName(string displayName, bool exactMatch = true)
        {
            if (string.IsNullOrEmpty(displayName))
                throw new ArgumentException("A display name is required.", nameof(displayName));

            var results = new List<string>();
            foreach (ServiceController sc in ServiceController.GetServices())
            {
                bool matches = exactMatch
                    ? string.Equals(sc.DisplayName, displayName, StringComparison.Ordinal)
                    : sc.DisplayName.IndexOf(displayName, StringComparison.OrdinalIgnoreCase) >= 0;
                if (matches)
                    results.Add(sc.ServiceName);
                sc.Dispose();
            }
            return results;
        }

        #endregion

        #region Control

        /// <summary>Starts a service and waits for it to reach <see cref="ServiceControllerStatus.Running"/>.</summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="timeoutMs">Maximum time to wait for it to reach Running, in milliseconds (default 30000).</param>
        /// <returns><c>true</c> if it reached Running within the timeout; <c>false</c> if it timed out.</returns>
        /// <exception cref="ArgumentException"><paramref name="serviceName"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">No service with that name is installed, or it could not be started.</exception>
        [Category("Service - Control")]
        [Description("Starts a service and waits for it to reach Running. Returns False (not an exception) if it times out.")]
        public bool StartService(string serviceName, int timeoutMs = 30000)
        {
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentException("A service name is required.", nameof(serviceName));

            using (var sc = new ServiceController(serviceName))
            {
                sc.Start();
                return WaitForStatusInternal(sc, ServiceControllerStatus.Running, timeoutMs);
            }
        }

        /// <summary>Stops a service and waits for it to reach <see cref="ServiceControllerStatus.Stopped"/>.</summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="timeoutMs">Maximum time to wait for it to reach Stopped, in milliseconds (default 30000).</param>
        /// <returns><c>true</c> if it reached Stopped within the timeout; <c>false</c> if it timed out.</returns>
        /// <exception cref="ArgumentException"><paramref name="serviceName"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">No service with that name is installed, or it does not support being stopped.</exception>
        [Category("Service - Control")]
        [Description("Stops a service and waits for it to reach Stopped. Returns False (not an exception) if it times out.")]
        public bool StopService(string serviceName, int timeoutMs = 30000)
        {
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentException("A service name is required.", nameof(serviceName));

            using (var sc = new ServiceController(serviceName))
            {
                sc.Stop();
                return WaitForStatusInternal(sc, ServiceControllerStatus.Stopped, timeoutMs);
            }
        }

        /// <summary>
        /// Stops then starts a service. Each phase gets its own <paramref name="timeoutMs"/>
        /// budget, so a full restart can take up to roughly twice that long.
        /// </summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="timeoutMs">Maximum time to wait for each phase (stop, then start) to complete, in milliseconds (default 30000).</param>
        /// <returns><c>true</c> only if both the stop and the start phases completed within their timeouts.</returns>
        /// <exception cref="ArgumentException"><paramref name="serviceName"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">No service with that name is installed, or it does not support being stopped.</exception>
        [Category("Service - Control")]
        [Description("Stops then starts a service. Each phase gets its own timeoutMs budget.")]
        public bool RestartService(string serviceName, int timeoutMs = 30000)
        {
            // Each call opens and disposes its own ServiceController - Stop and Start
            // don't need to share one instance, since ServiceController is a thin,
            // stateless-between-calls wrapper keyed by service name.
            bool stopped = StopService(serviceName, timeoutMs);
            bool started = StartService(serviceName, timeoutMs);
            return stopped && started;
        }

        /// <summary>Pauses a running service.</summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <exception cref="ArgumentException"><paramref name="serviceName"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">No service with that name is installed, or it does not support pause/continue.</exception>
        [Category("Service - Control")]
        [Description("Pauses a running service.")]
        public void PauseService(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentException("A service name is required.", nameof(serviceName));

            using (var sc = new ServiceController(serviceName))
            {
                sc.Pause();
            }
        }

        /// <summary>Resumes a paused service.</summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <exception cref="ArgumentException"><paramref name="serviceName"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">No service with that name is installed, or it does not support pause/continue.</exception>
        [Category("Service - Control")]
        [Description("Resumes a paused service.")]
        public void ResumeService(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentException("A service name is required.", nameof(serviceName));

            using (var sc = new ServiceController(serviceName))
            {
                sc.Continue();
            }
        }

        /// <summary>
        /// Polls for a service to reach the given status until it does, or the timeout
        /// elapses. Unlike this repo's other <c>WaitForX</c> methods, there is no poll
        /// interval parameter - the underlying <c>ServiceController.WaitForStatus</c> blocks
        /// natively against the Service Control Manager rather than requiring a hand-rolled
        /// poll loop.
        /// </summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="expectedStatus">The status to wait for.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <returns><c>true</c> if the service reached <paramref name="expectedStatus"/> within the timeout; <c>false</c> if it timed out.</returns>
        /// <exception cref="ArgumentException"><paramref name="serviceName"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">No service with that name is installed.</exception>
        [Category("Service - Control")]
        [Description("Polls for a service to reach the given status until it does, or the timeout elapses.")]
        public bool WaitForServiceStatus(string serviceName, ServiceControllerStatus expectedStatus, int timeoutMs)
        {
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentException("A service name is required.", nameof(serviceName));

            using (var sc = new ServiceController(serviceName))
            {
                return WaitForStatusInternal(sc, expectedStatus, timeoutMs);
            }
        }

        #endregion

        #region Configuration

        /// <summary>
        /// Sets a service's startup type. Unlike every other method in this component, this
        /// uses direct Win32 (<c>advapi32.dll</c>) calls rather than <c>ServiceController</c>,
        /// because <c>ServiceController</c> has no method to change startup type at all.
        /// </summary>
        /// <param name="serviceName">The service name (not display name).</param>
        /// <param name="startType">The startup type to set.</param>
        /// <exception cref="ArgumentException"><paramref name="serviceName"/> is null or empty.</exception>
        /// <exception cref="Win32Exception">The service could not be opened or reconfigured (e.g. access denied, or no service with that name).</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="startType"/> is not a defined value.</exception>
        [Category("Service - Configuration")]
        [Description("Sets a service's startup type, including delayed-auto-start.")]
        public void SetStartType(string serviceName, ServiceStartType startType)
        {
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentException("A service name is required.", nameof(serviceName));

            uint dwStartType = ToWin32StartType(startType);
            bool delayed = startType == ServiceStartType.AutomaticDelayedStart;

            IntPtr hScm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (hScm == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenSCManager failed.");

            try
            {
                IntPtr hService = OpenService(hScm, serviceName, SERVICE_CHANGE_CONFIG);
                if (hService == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"OpenService failed for '{serviceName}'.");

                try
                {
                    if (!ChangeServiceConfig(hService, SERVICE_NO_CHANGE, dwStartType, SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "ChangeServiceConfig failed.");

                    // Explicitly set the delayed-auto-start flag either way - not calling
                    // ChangeServiceConfig2 at all for the "normal" cases would leave a
                    // previously-set delayed flag stale when switching away from
                    // AutomaticDelayedStart back to plain Automatic (or to Manual/Disabled).
                    var info = new SERVICE_DELAYED_AUTO_START_INFO { fDelayedAutostart = delayed };
                    if (!ChangeServiceConfig2(hService, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref info))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "ChangeServiceConfig2 failed.");
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

        /// <summary>Maps our designer-friendly <see cref="ServiceStartType"/> to the raw Win32 dwStartType value.</summary>
        private static uint ToWin32StartType(ServiceStartType startType)
        {
            switch (startType)
            {
                case ServiceStartType.Boot: return SERVICE_BOOT_START;
                case ServiceStartType.System: return SERVICE_SYSTEM_START;
                case ServiceStartType.Automatic: return SERVICE_AUTO_START;
                case ServiceStartType.AutomaticDelayedStart: return SERVICE_AUTO_START;
                case ServiceStartType.Manual: return SERVICE_DEMAND_START;
                case ServiceStartType.Disabled: return SERVICE_DISABLED;
                default: throw new ArgumentOutOfRangeException(nameof(startType), startType, "Unrecognized start type.");
            }
        }

        /// <summary>
        /// Waits for a service to reach a status, translating a timeout into False instead
        /// of letting the exception propagate. Note the deliberately fully-qualified
        /// <c>System.ServiceProcess.TimeoutException</c> in the catch clause below - a bare
        /// <c>TimeoutException</c> in this file resolves to <c>System.TimeoutException</c>
        /// (the BCL one), a different type from the one <c>WaitForStatus</c> actually throws.
        /// That would compile fine and silently never catch the real exception.
        /// </summary>
        private static bool WaitForStatusInternal(ServiceController sc, ServiceControllerStatus expectedStatus, int timeoutMs)
        {
            try
            {
                sc.WaitForStatus(expectedStatus, TimeSpan.FromMilliseconds(timeoutMs));
                return true;
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                return false;
            }
        }

        /// <summary>Reads the delayed-auto-start flag for a service via QueryServiceConfig2. Assumes the caller already confirmed the service exists.</summary>
        private static bool IsDelayedAutoStart(string serviceName)
        {
            IntPtr hScm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (hScm == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenSCManager failed.");

            try
            {
                IntPtr hService = OpenService(hScm, serviceName, SERVICE_QUERY_CONFIG);
                if (hService == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"OpenService failed for '{serviceName}'.");

                try
                {
                    int size = Marshal.SizeOf<SERVICE_DELAYED_AUTO_START_INFO>();
                    IntPtr buffer = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (!QueryServiceConfig2(hService, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, buffer, (uint)size, out uint bytesNeeded))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryServiceConfig2 failed.");

                        var info = Marshal.PtrToStructure<SERVICE_DELAYED_AUTO_START_INFO>(buffer);
                        return info.fDelayedAutostart;
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
    }
}
