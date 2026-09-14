using System.Diagnostics;

namespace SecSwitch.Core;

public static class ResourceSampler
{
    public static IReadOnlyDictionary<string, ModuleResourceUsage> MeasureAll(
        IReadOnlyList<ModuleManifest> modules,
        TimeSpan? interval = null)
    {
        var sampleInterval = interval ?? TimeSpan.FromSeconds(1);
        var targetNames = modules
            .SelectMany(module => module.Processes)
            .Select(process => Path.GetFileNameWithoutExtension(process.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var before = Snapshot(targetNames);
        Thread.Sleep(sampleInterval);
        var after = Snapshot(targetNames);
        var cores = Math.Max(1, Environment.ProcessorCount);

        var result = new Dictionary<string, ModuleResourceUsage>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            double cpu = 0;
            long memory = 0;
            var processCount = 0;

            foreach (var definition in module.Processes)
            {
                var name = Path.GetFileNameWithoutExtension(definition.Name);
                foreach (var current in after.Values.Where(value =>
                             string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    memory += current.WorkingSetBytes;
                    processCount++;

                    if (before.TryGetValue(current.Id, out var previous))
                    {
                        var cpuSeconds = current.CpuSeconds - previous.CpuSeconds;
                        if (cpuSeconds > 0)
                        {
                            cpu += cpuSeconds / sampleInterval.TotalSeconds / cores * 100d;
                        }
                    }
                }
            }

            result[module.Id] = new ModuleResourceUsage(
                Math.Round(cpu, 2),
                memory,
                processCount);
        }

        return result;
    }

    private static Dictionary<int, ProcessSample> Snapshot(HashSet<string> targetNames)
    {
        var values = new Dictionary<int, ProcessSample>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!targetNames.Contains(process.ProcessName))
                {
                    continue;
                }

                values[process.Id] = new ProcessSample(
                    process.Id,
                    process.ProcessName,
                    process.TotalProcessorTime.TotalSeconds,
                    process.WorkingSet64);
            }
            catch
            {
                // A process may exit or deny access between enumeration and sampling.
            }
            finally
            {
                process.Dispose();
            }
        }

        return values;
    }

    private sealed record ProcessSample(int Id, string Name, double CpuSeconds, long WorkingSetBytes);
}
