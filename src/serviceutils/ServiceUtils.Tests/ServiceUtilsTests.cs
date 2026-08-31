using System;
using System.ServiceProcess;
using ServiceAutomation;
using Xunit;

namespace ServiceAutomation.Tests
{
    /// <summary>
    /// Platform-independent unit tests for ServiceUtils' input guards, plus the
    /// never-throw contract on the paths that return before any Service Control
    /// Manager or advapi32 call. These run anywhere (including non-Windows CI
    /// shells), so they deliberately stay behind the null/empty-name, negative-
    /// timeout, and undefined-enum guards - live service control against a real
    /// SCM is covered by the Pega Unit Test plan in the repo's TESTING.md.
    /// </summary>
    public class ServiceUtilsTests
    {
        private readonly ServiceUtils _svc = new ServiceUtils();

        // --- Null/empty service name: false + message, never an exception ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void IsServiceInstalled_NullOrEmptyName_ReturnsFalseWithMessage(string serviceName)
        {
            bool installed = _svc.IsServiceInstalled(serviceName, out string message);

            Assert.False(installed);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void IsServiceInstalledWithQuerySucceeded_NullOrEmptyName_ReturnsFalseWithFailedQuery(string serviceName)
        {
            bool installed = _svc.IsServiceInstalled(serviceName, out bool querySucceeded, out string message);

            // An invalid name is a query failure, not a genuine "not installed" answer.
            Assert.False(installed);
            Assert.False(querySucceeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void IsRunning_NullOrEmptyName_ReturnsFalseWithMessage(string serviceName)
        {
            bool running = _svc.IsRunning(serviceName, out string message);

            Assert.False(running);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void IsRunningWithQuerySucceeded_NullOrEmptyName_ReturnsFalseWithFailedQuery(string serviceName)
        {
            bool running = _svc.IsRunning(serviceName, out bool querySucceeded, out string message);

            Assert.False(running);
            Assert.False(querySucceeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void TryGetStatus_NullOrEmptyName_ReturnsFalseWithDefaultStatus(string serviceName)
        {
            bool got = _svc.TryGetStatus(serviceName, out ServiceControllerStatus status, out string message);

            Assert.False(got);
            Assert.Equal(ServiceControllerStatus.Stopped, status); // documented default
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void TryGetStatusRepoEnum_NullOrEmptyName_ReturnsFalseWithDefaultStatus(string serviceName)
        {
            bool got = _svc.TryGetStatus(serviceName, out ServiceStatus status, out string message);

            Assert.False(got);
            Assert.Equal(ServiceStatus.Stopped, status); // documented default
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void TryGetStartType_NullOrEmptyName_ReturnsFalseWithDefaultStartType(string serviceName)
        {
            bool got = _svc.TryGetStartType(serviceName, out ServiceStartType startType, out string message);

            Assert.False(got);
            Assert.Equal(ServiceStartType.Manual, startType); // documented default
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void TryFindFirstServiceNameByDisplayName_NullOrEmptyName_ReturnsFalseWithMessage(string displayName)
        {
            bool found = _svc.TryFindFirstServiceNameByDisplayName(displayName, out string serviceName, out string message);

            Assert.False(found);
            Assert.Null(serviceName);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ListServiceNamesDelimited_NeverThrows()
        {
            string names = _svc.ListServiceNamesDelimited();

            Assert.NotNull(names);
        }

        [Fact]
        public void FindServiceNamesByDisplayNameDelimited_NoMatch_ReturnsEmptyString()
        {
            string names = _svc.FindServiceNamesByDisplayNameDelimited("\0no such display name\0");

            Assert.Equal(string.Empty, names);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void StartService_NullOrEmptyName_ReturnsFalseWithMessage(string serviceName)
        {
            bool started = _svc.StartService(serviceName, timeoutMs: 1000, out string message);

            Assert.False(started);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void StartServiceWithWasAlreadyRunning_NullOrEmptyName_ReturnsFalseWithMessage(string serviceName)
        {
            bool started = _svc.StartService(serviceName, timeoutMs: 1000, out bool wasAlreadyRunning, out string message);

            Assert.False(started);
            Assert.False(wasAlreadyRunning);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void StopService_NullOrEmptyName_ReturnsFalseWithMessage(string serviceName)
        {
            bool stopped = _svc.StopService(serviceName, timeoutMs: 1000, out string message);

            Assert.False(stopped);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void StopServiceWithWasAlreadyStopped_NullOrEmptyName_ReturnsFalseWithMessage(string serviceName)
        {
            bool stopped = _svc.StopService(serviceName, timeoutMs: 1000, out bool wasAlreadyStopped, out string message);

            Assert.False(stopped);
            Assert.False(wasAlreadyStopped);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void RestartService_NullOrEmptyName_ReturnsFalseWithMessage(string serviceName)
        {
            bool restarted = _svc.RestartService(serviceName, timeoutMs: 1000, out string message);

            Assert.False(restarted);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void RestartServiceWithWasAlreadyStopped_NullOrEmptyName_ReturnsFalseWithMessage(string serviceName)
        {
            bool restarted = _svc.RestartService(serviceName, timeoutMs: 1000, out bool wasAlreadyStopped, out string message);

            Assert.False(restarted);
            Assert.False(wasAlreadyStopped);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void PauseService_NullOrEmptyName_ReturnsFalseWithMessage(string serviceName)
        {
            bool paused = _svc.PauseService(serviceName, out string message);

            Assert.False(paused);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void PauseServiceWithWasAlreadyPaused_NullOrEmptyName_ReturnsFalseWithMessage(string serviceName)
        {
            bool paused = _svc.PauseService(serviceName, out bool wasAlreadyPaused, out string message);

            Assert.False(paused);
            Assert.False(wasAlreadyPaused);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ResumeService_NullOrEmptyName_ReturnsFalseWithMessage(string serviceName)
        {
            bool resumed = _svc.ResumeService(serviceName, out string message);

            Assert.False(resumed);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ResumeServiceWithWasAlreadyRunning_NullOrEmptyName_ReturnsFalseWithMessage(string serviceName)
        {
            bool resumed = _svc.ResumeService(serviceName, out bool wasAlreadyRunning, out string message);

            Assert.False(resumed);
            Assert.False(wasAlreadyRunning);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void WaitForServiceStatus_NullOrEmptyName_ReturnsFalseWithoutPolling(string serviceName)
        {
            // A nonzero timeout proves the guard returns before any polling/SCM wait.
            bool reached = _svc.WaitForServiceStatus(serviceName, ServiceControllerStatus.Running, timeoutMs: 5000, out string message);

            Assert.False(reached);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void WaitForServiceStatusRepoEnum_NullOrEmptyName_ReturnsFalseWithoutPolling(string serviceName)
        {
            bool reached = _svc.WaitForServiceStatus(serviceName, ServiceStatus.Running, timeoutMs: 5000, out string message);

            Assert.False(reached);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForServiceStatusRepoEnum_UndefinedStatus_ReturnsFalseWithMessage()
        {
            const ServiceStatus undefined = (ServiceStatus)999;

            bool reached = _svc.WaitForServiceStatus("Anything", undefined, timeoutMs: 5000, out string message);

            Assert.False(reached);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void SetStartType_NullOrEmptyName_ReturnsFalseWithMessage(string serviceName)
        {
            bool set = _svc.SetStartType(serviceName, ServiceStartType.Automatic, out string message);

            Assert.False(set);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- Negative timeout: false + message, returned without touching the SCM ---

        [Fact]
        public void StartService_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool started = _svc.StartService("Anything", timeoutMs: -1, out string message);

            Assert.False(started);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void StopService_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool stopped = _svc.StopService("Anything", timeoutMs: -1, out string message);

            Assert.False(stopped);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void RestartService_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool restarted = _svc.RestartService("Anything", timeoutMs: -1, out string message);

            Assert.False(restarted);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForServiceStatus_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool reached = _svc.WaitForServiceStatus("Anything", ServiceControllerStatus.Running, timeoutMs: -1, out string message);

            Assert.False(reached);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void StartServiceWithWasAlreadyRunning_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool started = _svc.StartService("Anything", timeoutMs: -1, out bool wasAlreadyRunning, out string message);

            Assert.False(started);
            Assert.False(wasAlreadyRunning);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void StopServiceWithWasAlreadyStopped_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool stopped = _svc.StopService("Anything", timeoutMs: -1, out bool wasAlreadyStopped, out string message);

            Assert.False(stopped);
            Assert.False(wasAlreadyStopped);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void RestartServiceWithWasAlreadyStopped_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool restarted = _svc.RestartService("Anything", timeoutMs: -1, out bool wasAlreadyStopped, out string message);

            Assert.False(restarted);
            Assert.False(wasAlreadyStopped);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void WaitForServiceStatusRepoEnum_NegativeTimeout_ReturnsFalseWithMessage()
        {
            bool reached = _svc.WaitForServiceStatus("Anything", ServiceStatus.Running, timeoutMs: -1, out string message);

            Assert.False(reached);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- SetStartType: undefined ServiceStartType reports failure, never throws ---

        [Fact]
        public void SetStartType_UndefinedStartType_ReturnsFalseWithMessage()
        {
            const ServiceStartType undefined = (ServiceStartType)999;

            bool set = _svc.SetStartType("Anything", undefined, out string message);

            Assert.False(set);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- TryToWin32StartType: enum-to-Win32 mapping pinned to SERVICE_* values ---

        [Theory]
        [InlineData(ServiceStartType.Boot, 0u)]                    // SERVICE_BOOT_START
        [InlineData(ServiceStartType.System, 1u)]                  // SERVICE_SYSTEM_START
        [InlineData(ServiceStartType.Automatic, 2u)]               // SERVICE_AUTO_START
        [InlineData(ServiceStartType.AutomaticDelayedStart, 2u)]   // SERVICE_AUTO_START (delayed flag is set separately)
        [InlineData(ServiceStartType.Manual, 3u)]                  // SERVICE_DEMAND_START
        [InlineData(ServiceStartType.Disabled, 4u)]                // SERVICE_DISABLED
        public void TryToWin32StartType_DefinedValues_MapToWin32Constants(ServiceStartType startType, uint expected)
        {
            bool mapped = ServiceUtils.TryToWin32StartType(startType, out uint dwStartType, out string message);

            Assert.True(mapped);
            Assert.Null(message);
            Assert.Equal(expected, dwStartType);
        }

        [Fact]
        public void TryToWin32StartType_UndefinedValue_ReturnsFalseWithMessage()
        {
            bool mapped = ServiceUtils.TryToWin32StartType((ServiceStartType)999, out uint dwStartType, out string message);

            Assert.False(mapped);
            Assert.Equal(0u, dwStartType);
            Assert.False(string.IsNullOrEmpty(message));
        }
    }
}