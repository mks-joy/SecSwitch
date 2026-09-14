using System.Diagnostics;

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
            if (WindowsRuntime.ServiceExists(serviceName))
            {
                installed = true;
                evidence.Add($"service:{serviceName}");

                if (WindowsRuntime.IsServiceRunning(serviceName))
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
}
