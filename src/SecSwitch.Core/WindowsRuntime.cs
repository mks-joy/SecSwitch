using System.Diagnostics;
using Microsoft.Win32;

namespace SecSwitch.Core;

public static class WindowsRuntime
{
    public static bool ServiceExists(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        return key is not null;
    }

    public static bool IsServiceRunning(string serviceName)
    {
        var result = RunSc("query", serviceName);
        if (result.ExitCode != 0)
        {
            return false;
        }

        foreach (var line in result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var colon = trimmed.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var afterColon = trimmed[(colon + 1)..].TrimStart();
            return afterColon.StartsWith("4 ", StringComparison.Ordinal)
                   || string.Equals(afterColon, "4", StringComparison.Ordinal);
        }

        return false;
    }

    public static RuntimeActionResult StartService(string serviceName, TimeSpan? timeout = null)
    {
        if (!ServiceExists(serviceName))
        {
            return new RuntimeActionResult(false, $"Service not found: {serviceName}");
        }

        if (IsServiceRunning(serviceName))
        {
            return new RuntimeActionResult(true, $"Service already running: {serviceName}");
        }

        var result = RunSc("start", serviceName);
        if (result.ExitCode != 0)
        {
            return new RuntimeActionResult(false, BuildScError("start", serviceName, result));
        }

        var wait = timeout ?? TimeSpan.FromSeconds(8);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < wait)
        {
            if (IsServiceRunning(serviceName))
            {
                return new RuntimeActionResult(true, $"Started service: {serviceName}");
            }

            Thread.Sleep(250);
        }

        return new RuntimeActionResult(false, $"Timed out waiting for service to start: {serviceName}");
    }

    public static RuntimeActionResult StopService(string serviceName, TimeSpan? timeout = null)
    {
        if (!ServiceExists(serviceName))
        {
            return new RuntimeActionResult(false, $"Service not found: {serviceName}");
        }

        if (!IsServiceRunning(serviceName))
        {
            return new RuntimeActionResult(true, $"Service already stopped: {serviceName}");
        }

        var result = RunSc("stop", serviceName);
        if (result.ExitCode != 0)
        {
            return new RuntimeActionResult(false, BuildScError("stop", serviceName, result));
        }

        var wait = timeout ?? TimeSpan.FromSeconds(8);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < wait)
        {
            if (!IsServiceRunning(serviceName))
            {
                return new RuntimeActionResult(true, $"Stopped service: {serviceName}");
            }

            Thread.Sleep(250);
        }

        return new RuntimeActionResult(false, $"Timed out waiting for service to stop: {serviceName}");
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
                return new RuntimeActionResult(
                    false,
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

    private static ScResult RunSc(string verb, string serviceName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"{verb} \"{serviceName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return new ScResult(-1, string.Empty, "Could not start sc.exe");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            return new ScResult(process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return new ScResult(-1, string.Empty, ex.Message);
        }
    }

    private static string BuildScError(string verb, string serviceName, ScResult result)
    {
        var details = string.Join(" ", new[] { result.Output, result.Error }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ReplaceLineEndings(" ")));

        return string.IsNullOrWhiteSpace(details)
            ? $"sc.exe {verb} failed for {serviceName} (exit code {result.ExitCode}). Administrator privileges may be required."
            : $"sc.exe {verb} failed for {serviceName} (exit code {result.ExitCode}): {details}";
    }

    private sealed record ScResult(int ExitCode, string Output, string Error);
}

public sealed record RuntimeActionResult(bool Success, string Message);
