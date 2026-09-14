using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SecSwitch.Core;

public static class WindowsRuntime
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;

    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceStartPending = 0x00000002;
    private const uint ServiceStopPending = 0x00000003;
    private const uint ServiceRunning = 0x00000004;

    private const int ScStatusProcessInfo = 0;
    private const int ErrorAccessDenied = 5;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceNotActive = 1062;

    public static bool ServiceExists(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        return key is not null;
    }

    public static bool IsServiceRunning(string serviceName)
    {
        return TryGetServiceState(serviceName, out var state, out _)
               && state == ServiceRunning;
    }

    public static RuntimeActionResult StartService(string serviceName, TimeSpan? timeout = null)
    {
        if (!ServiceExists(serviceName))
        {
            return new RuntimeActionResult(false, $"Service not found: {serviceName}");
        }

        var manager = NativeOpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            return Win32Failure("open Service Control Manager", serviceName, Marshal.GetLastWin32Error());
        }

        try
        {
            var service = NativeOpenService(manager, serviceName, ServiceQueryStatus | ServiceStart);
            if (service == IntPtr.Zero)
            {
                return Win32Failure("open service", serviceName, Marshal.GetLastWin32Error());
            }

            try
            {
                if (!TryQueryServiceState(service, out var state, out var queryError))
                {
                    return Win32Failure("query service", serviceName, queryError);
                }

                if (state == ServiceRunning)
                {
                    return new RuntimeActionResult(true, $"Service already running: {serviceName}");
                }

                var wait = timeout ?? TimeSpan.FromSeconds(8);

                if (state == ServiceStartPending)
                {
                    return WaitForServiceState(service, serviceName, ServiceRunning, wait,
                        $"Service already starting: {serviceName}",
                        $"Timed out waiting for service to start: {serviceName}");
                }

                if (state == ServiceStopPending)
                {
                    var stopped = WaitForServiceState(service, serviceName, ServiceStopped, wait,
                        $"Service stopped before restart: {serviceName}",
                        $"Timed out waiting for service to stop before restart: {serviceName}");
                    if (!stopped.Success)
                    {
                        return stopped;
                    }
                }

                if (!NativeStartService(service, 0, null))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorServiceAlreadyRunning)
                    {
                        return new RuntimeActionResult(true, $"Service already running: {serviceName}");
                    }

                    return Win32Failure("start service", serviceName, error);
                }

                return WaitForServiceState(service, serviceName, ServiceRunning, wait,
                    $"Started service: {serviceName}",
                    $"Timed out waiting for service to start: {serviceName}");
            }
            finally
            {
                NativeCloseServiceHandle(service);
            }
        }
        finally
        {
            NativeCloseServiceHandle(manager);
        }
    }

    public static RuntimeActionResult StopService(string serviceName, TimeSpan? timeout = null)
    {
        if (!ServiceExists(serviceName))
        {
            return new RuntimeActionResult(false, $"Service not found: {serviceName}");
        }

        var manager = NativeOpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            return Win32Failure("open Service Control Manager", serviceName, Marshal.GetLastWin32Error());
        }

        try
        {
            var service = NativeOpenService(manager, serviceName, ServiceQueryStatus | ServiceStop);
            if (service == IntPtr.Zero)
            {
                return Win32Failure("open service", serviceName, Marshal.GetLastWin32Error());
            }

            try
            {
                if (!TryQueryServiceState(service, out var state, out var queryError))
                {
                    return Win32Failure("query service", serviceName, queryError);
                }

                if (state == ServiceStopped)
                {
                    return new RuntimeActionResult(true, $"Service already stopped: {serviceName}");
                }

                var wait = timeout ?? TimeSpan.FromSeconds(8);

                if (state == ServiceStopPending)
                {
                    return WaitForServiceState(service, serviceName, ServiceStopped, wait,
                        $"Service already stopping: {serviceName}",
                        $"Timed out waiting for service to stop: {serviceName}");
                }

                var status = new ServiceStatus();
                if (!NativeControlService(service, ServiceControlStop, ref status))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorServiceNotActive)
                    {
                        return new RuntimeActionResult(true, $"Service already stopped: {serviceName}");
                    }

                    return Win32Failure("stop service", serviceName, error);
                }

                return WaitForServiceState(service, serviceName, ServiceStopped, wait,
                    $"Stopped service: {serviceName}",
                    $"Timed out waiting for service to stop: {serviceName}");
            }
            finally
            {
                NativeCloseServiceHandle(service);
            }
        }
        finally
        {
            NativeCloseServiceHandle(manager);
        }
    }

    public static IReadOnlyList<int> GetProcessIds(string processName)
    {
        var normalized = Path.GetFileNameWithoutExtension(processName);
        return Process.GetProcessesByName(normalized)
            .Select(process =>
            {
                try
                {
                    return process.Id;
                }
                finally
                {
                    process.Dispose();
                }
            })
            .ToArray();
    }

    public static RuntimeActionResult StartProcess(ProcessDefinition definition, out IReadOnlyList<int> startedProcessIds)
    {
        startedProcessIds = [];

        if (string.IsNullOrWhiteSpace(definition.Path))
        {
            return new RuntimeActionResult(false, $"No executable path configured for process: {definition.Name}");
        }

        var path = Environment.ExpandEnvironmentVariables(definition.Path);
        if (!File.Exists(path))
        {
            return new RuntimeActionResult(false, $"Executable not found: {path}");
        }

        var before = GetProcessIds(definition.Name).ToHashSet();
        if (before.Count > 0)
        {
            return new RuntimeActionResult(true, $"Process already running: {definition.Name}");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
            });

            Thread.Sleep(750);

            var after = GetProcessIds(definition.Name);
            startedProcessIds = after.Where(id => !before.Contains(id)).ToArray();

            if (startedProcessIds.Count == 0 && process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        startedProcessIds = [process.Id];
                    }
                }
                catch
                {
                    // The process may have handed off to another process and exited quickly.
                }
            }

            return new RuntimeActionResult(true, $"Started process: {definition.Name}");
        }
        catch (Exception ex)
        {
            return new RuntimeActionResult(false, $"Failed to start {definition.Name}: {ex.Message}");
        }
    }

    public static RuntimeActionResult StopProcess(int processId, string processName)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var expected = Path.GetFileNameWithoutExtension(processName);
            if (!string.Equals(process.ProcessName, expected, StringComparison.OrdinalIgnoreCase))
            {
                return new RuntimeActionResult(false,
                    $"Refusing to stop PID {processId}: expected {expected}, found {process.ProcessName}.");
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            return new RuntimeActionResult(true, $"Stopped process: {processName} (PID {processId})");
        }
        catch (ArgumentException)
        {
            return new RuntimeActionResult(true, $"Process already stopped: {processName} (PID {processId})");
        }
        catch (Exception ex)
        {
            return new RuntimeActionResult(false, $"Failed to stop {processName} (PID {processId}): {ex.Message}");
        }
    }

    private static bool TryGetServiceState(string serviceName, out uint state, out int errorCode)
    {
        state = 0;
        errorCode = 0;

        var manager = NativeOpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        try
        {
            var service = NativeOpenService(manager, serviceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                errorCode = Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                return TryQueryServiceState(service, out state, out errorCode);
            }
            finally
            {
                NativeCloseServiceHandle(service);
            }
        }
        finally
        {
            NativeCloseServiceHandle(manager);
        }
    }

    private static bool TryQueryServiceState(IntPtr service, out uint state, out int errorCode)
    {
        var status = new ServiceStatusProcess();
        if (!NativeQueryServiceStatusEx(
                service,
                ScStatusProcessInfo,
                ref status,
                Marshal.SizeOf<ServiceStatusProcess>(),
                out _))
        {
            state = 0;
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        state = status.CurrentState;
        errorCode = 0;
        return true;
    }

    private static RuntimeActionResult WaitForServiceState(
        IntPtr service,
        string serviceName,
        uint desiredState,
        TimeSpan timeout,
        string successMessage,
        string timeoutMessage)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (!TryQueryServiceState(service, out var state, out var errorCode))
            {
                return Win32Failure("query service", serviceName, errorCode);
            }

            if (state == desiredState)
            {
                return new RuntimeActionResult(true, successMessage);
            }

            Thread.Sleep(250);
        }

        return new RuntimeActionResult(false, timeoutMessage);
    }

    private static RuntimeActionResult Win32Failure(string operation, string serviceName, int errorCode)
    {
        var friendly = errorCode switch
        {
            ErrorAccessDenied => "Access denied. Administrator privileges are required for this operation.",
            ErrorServiceAlreadyRunning => "The service is already running.",
            ErrorServiceNotActive => "The service is not running.",
            _ => new Win32Exception(errorCode).Message
        };

        return new RuntimeActionResult(false,
            $"Failed to {operation} '{serviceName}' (Win32 {errorCode}): {friendly}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr NativeOpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr NativeOpenService(IntPtr serviceControlManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeStartService(IntPtr service, uint numServiceArgs, string[]? serviceArgVectors);

    [DllImport("advapi32.dll", EntryPoint = "ControlService", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeControlService(IntPtr service, uint control, ref ServiceStatus serviceStatus);

    [DllImport("advapi32.dll", EntryPoint = "QueryServiceStatusEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeQueryServiceStatusEx(
        IntPtr service,
        int infoLevel,
        ref ServiceStatusProcess buffer,
        int bufferSize,
        out int bytesNeeded);

    [DllImport("advapi32.dll", EntryPoint = "CloseServiceHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeCloseServiceHandle(IntPtr serviceHandle);
}

public sealed record RuntimeActionResult(bool Success, string Message);
