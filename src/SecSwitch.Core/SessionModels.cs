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

    // Exact process names from the module allowlist. These are persisted so cleanup can
    // discover helper processes that appear late in the session, even if they were not
    // visible during the initial post-start sampling window.
    public List<string> AllowlistedProcessNames { get; init; } = [];

    // Processes that already existed before SecSwitch changed this module. Cleanup must
    // never terminate these PIDs. The baseline also survives if the CLI is interrupted and
    // a later 'session stop' command performs the restore.
    public List<SessionProcessState> PreexistingProcesses { get; init; } = [];
}

public sealed class SessionProcessState
{
    public required string Name { get; init; }
    public int ProcessId { get; init; }
}
