namespace SecSwitch.Core;

public static class OnDemandConfigurator
{
    public static OnDemandConfigurationResult Apply(ModuleManifest module)
    {
        var messages = new List<string>();
        var failures = 0;

        if (string.Equals(module.SessionControl, "observeOnly", StringComparison.OrdinalIgnoreCase))
        {
            return new OnDemandConfigurationResult(
                false,
                [$"{module.Name} is currently observe-only; SecSwitch will not change its runtime/startup state yet."]);
        }

        foreach (var serviceName in module.ServiceNames.Where(WindowsRuntime.ServiceExists))
        {
            var configure = ServiceConfiguration.SetManual(serviceName);
            messages.Add(configure.Message);
            if (!configure.Success)
            {
                failures++;
            }

            if (WindowsRuntime.IsServiceRunning(serviceName))
            {
                var stop = WindowsRuntime.StopService(serviceName, TimeSpan.FromSeconds(15));
                messages.Add(stop.Message);
                if (!stop.Success)
                {
                    failures++;
                }
            }
        }

        // Clean up allowlisted helper processes that can remain after a service stops.
        // This is intentionally restricted to exact process names from the module manifest.
        foreach (var process in module.Processes)
        {
            foreach (var pid in WindowsRuntime.GetProcessIds(process.Name))
            {
                var stop = WindowsRuntime.StopProcess(pid, process.Name);
                messages.Add(stop.Message);
                if (!stop.Success)
                {
                    failures++;
                }
            }
        }

        return new OnDemandConfigurationResult(failures == 0, messages);
    }
}

public sealed record OnDemandConfigurationResult(bool Success, IReadOnlyList<string> Messages);
