using System.Diagnostics;
using Microsoft.Win32;

namespace SecSwitch.Core;

public static class ModuleScanner
{
    public static ModuleStatus Scan(ModuleManifest module)
    {
        var evidence = new List<string>();
        var installed = false;
        var running = false;

        foreach (var serviceName in module.ServiceNames)
        {
            if (ServiceExists(serviceName))
            {
                installed = true;
                evidence.Add($"service:{serviceName}");

                if (IsServiceRunning(serviceName))
                {
                    running = true;
                    evidence.Add($"running-service:{serviceName}");
                }
            }
        }

        foreach (var process in module.Processes)
        {
            if (!string.IsNullOrWhiteSpace(process.Path) && File.Exists(Environment.ExpandEnvironmentVariables(process.Path)))
            {
                installed = true;
                evidence.Add($"file:{process.Path}");
            }

            var processName = Path.GetFileNameWithoutExtension(process.Name);
            if (Process.GetProcessesByName(processName).Length > 0)
            {
                running = true;
                installed = true;
                evidence.Add($"running-process:{process.Name}");
            }
        }

        foreach (var path in module.DetectionPaths)
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            if (File.Exists(expanded) || Directory.Exists(expanded))
            {
                installed = true;
                evidence.Add($"path:{path}");
            }
        }

        return new ModuleStatus
        {
            Module = module,
            Installed = installed,
            Running = running,
            Evidence = evidence
        };
    }

    private static bool ServiceExists(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        return key is not null;
    }

    private static bool IsServiceRunning(string serviceName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query \"{serviceName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);

            // SCM state 4 means SERVICE_RUNNING. The numeric state is stable across UI languages.
            return output.Contains("STATE", StringComparison.OrdinalIgnoreCase)
                   && output.Contains("4", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
