using System.Diagnostics;
using Microsoft.Win32;

namespace SecSwitch.Core;

public static class ServiceConfiguration
{
    public static string GetStartType(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            if (key?.GetValue("Start") is not int start)
            {
                return "Unknown";
            }

            return start switch
            {
                0 => "Boot",
                1 => "System",
                2 => IsDelayedAutoStart(key) ? "Automatic (Delayed)" : "Automatic",
                3 => "Manual",
                4 => "Disabled",
                _ => $"Unknown ({start})"
            };
        }
        catch
        {
            return "Unknown";
        }
    }

    public static RuntimeActionResult SetManual(string serviceName)
    {
        if (!WindowsRuntime.ServiceExists(serviceName))
        {
            return new RuntimeActionResult(false, $"Service not found: {serviceName}");
        }

        var current = GetStartType(serviceName);
        if (string.Equals(current, "Manual", StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeActionResult(true, $"Service already Manual: {serviceName}");
        }

        return RunScConfig(serviceName, "demand");
    }

    private static bool IsDelayedAutoStart(RegistryKey key)
    {
        return key.GetValue("DelayedAutoStart") is int delayed && delayed != 0;
    }

    private static RuntimeActionResult RunScConfig(string serviceName, string startMode)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"config \"{serviceName}\" start= {startMode}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return new RuntimeActionResult(false, "Could not start sc.exe.");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode == 0)
            {
                return new RuntimeActionResult(true, $"Set service to Manual: {serviceName}");
            }

            var details = string.Join(" ", new[] { stdout, stderr }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().ReplaceLineEndings(" ")));

            return new RuntimeActionResult(
                false,
                string.IsNullOrWhiteSpace(details)
                    ? $"Failed to set {serviceName} to Manual (exit code {process.ExitCode}). Administrator privileges may be required."
                    : $"Failed to set {serviceName} to Manual (exit code {process.ExitCode}): {details}");
        }
        catch (Exception ex)
        {
            return new RuntimeActionResult(false, $"Failed to configure {serviceName}: {ex.Message}");
        }
    }
}
