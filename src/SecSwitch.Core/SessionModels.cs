namespace SecSwitch.Core;

public sealed class SessionState
{
    public Guid SessionId { get; init; } = Guid.NewGuid();
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public List<SessionModuleState> Modules { get; init; } = [];
}

public sealed class SessionModuleState
{
    public required string ModuleId { get; init; }
    public required string ModuleName { get; init; }
    public List<string> StartedServices { get; init; } = [];
    public List<SessionProcessState> StartedProcesses { get; init; } = [];
}

public sealed class SessionProcessState
{
    public required string Name { get; init; }
    public int ProcessId { get; init; }
}
