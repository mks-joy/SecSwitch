namespace SecSwitch.Core;

public sealed class ModuleManifest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Vendor { get; init; }
    public string? DescriptionKo { get; init; }
    public string? Purpose { get; init; }
    public List<string> KnownUses { get; init; } = [];
    public List<string> ServiceNames { get; init; } = [];
    public List<ProcessDefinition> Processes { get; init; } = [];
    public List<string> DetectionPaths { get; init; } = [];

    // managed: SecSwitch may start the module and restore it after the session.
    // observeOnly: SecSwitch detects/reports the module but does not change its runtime state.
    // This is used for self-protected products until a verified reversible control path exists.
    public string SessionControl { get; init; } = "managed";
}

public sealed class ProcessDefinition
{
    public required string Name { get; init; }
    public string? Path { get; init; }
}

public sealed class ModuleStatus
{
    public required ModuleManifest Module { get; init; }
    public bool Installed { get; init; }
    public bool Running { get; init; }
    public List<string> ServiceStartTypes { get; init; } = [];
    public double CpuPercent { get; set; }
    public long WorkingSetBytes { get; set; }
    public int RunningProcessCount { get; set; }
    public List<string> Evidence { get; init; } = [];
}

public sealed record ModuleResourceUsage(double CpuPercent, long WorkingSetBytes, int ProcessCount);
