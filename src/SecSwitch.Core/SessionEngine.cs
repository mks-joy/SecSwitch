using System.Text.Json;

namespace SecSwitch.Core;

public static class SessionEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string SessionFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SecSwitch",
        "session.json");

    public static bool HasActiveSession => File.Exists(SessionFilePath);

    public static SessionState? LoadActiveSession()
    {
        if (!File.Exists(SessionFilePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SessionState>(File.ReadAllText(SessionFilePath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<int> StartAsync(
        IReadOnlyList<ModuleManifest> modules,
        int minutes,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        if (minutes is < 1 or > 120)
        {
            await output.WriteLineAsync("Session duration must be between 1 and 120 minutes.");
            return 2;
        }

        if (HasActiveSession)
        {
            var existing = LoadActiveSession();
            if (existing is not null)
            {
                var remaining = existing.ExpiresAtUtc - DateTimeOffset.UtcNow;
                await output.WriteLineAsync($"A SecSwitch session is already active ({FormatRemaining(remaining)} remaining).");
                await output.WriteLineAsync("Use 'session extend --minutes <n>' or 'session stop'.");
            }
            else
            {
                await output.WriteLineAsync($"A session state file already exists but could not be read: {SessionFilePath}");
            }

            return 1;
        }

        var state = new SessionState
        {
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(minutes)
        };

        SaveState(state);
        await output.WriteLineAsync($"Starting {minutes}-minute SecSwitch session...");

        foreach (var module in modules)
        {
            var status = ModuleScanner.Scan(module);
            if (!status.Installed)
            {
                continue;
            }

            if (string.Equals(module.SessionControl, "observeOnly", StringComparison.OrdinalIgnoreCase))
            {
                var runtimeState = status.Running ? "running" : "stopped";
                await output.WriteLineAsync($"  ~ {module.Name}: observe-only ({runtimeState}); runtime state not changed");
                continue;
            }

            var moduleState = new SessionModuleState
            {
                ModuleId = module.Id,
                ModuleName = module.Name
            };
            state.Modules.Add(moduleState);

            // Snapshot every allowlisted process before changing the module. Persist both the
            // names and the exact PIDs so restore can safely discover helpers that appear much
            // later than the initial launch window while preserving anything that pre-existed.
            var beforeByProcess = module.Processes.ToDictionary(
                process => process.Name,
                process => WindowsRuntime.GetProcessIds(process.Name).ToHashSet(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var processDefinition in module.Processes)
            {
                if (!moduleState.AllowlistedProcessNames.Contains(processDefinition.Name, StringComparer.OrdinalIgnoreCase))
                {
                    moduleState.AllowlistedProcessNames.Add(processDefinition.Name);
                }

                foreach (var processId in beforeByProcess[processDefinition.Name])
                {
                    moduleState.PreexistingProcesses.Add(new SessionProcessState
                    {
                        Name = processDefinition.Name,
                        ProcessId = processId
                    });
                }
            }

            SaveState(state);

            var existingServices = module.ServiceNames
                .Where(WindowsRuntime.ServiceExists)
                .ToArray();

            if (existingServices.Length > 0)
            {
                var attemptedServiceStart = false;

                foreach (var serviceName in existingServices)
                {
                    if (WindowsRuntime.IsServiceRunning(serviceName))
                    {
                        await output.WriteLineAsync($"  = {module.Name}: service already running ({serviceName})");
                        continue;
                    }

                    attemptedServiceStart = true;

                    // Persist cleanup intent before attempting the start. This deliberately
                    // closes the failure window where a service starts successfully but the
                    // start operation is reported as a timeout/error or the process exits
                    // before we can write the session state. Restore is idempotent, so trying
                    // to stop a service that never actually started is harmless.
                    moduleState.StartedServices.Add(serviceName);
                    SaveState(state);

                    var result = WindowsRuntime.StartService(serviceName);
                    await output.WriteLineAsync($"  {(result.Success ? "+" : "!")} {module.Name}: {result.Message}");
                }

                // Capture helpers that appear immediately for status/debugging. Restore no
                // longer depends on this short sampling window; it uses the persisted baseline
                // and performs a fresh allowlisted sweep when the session ends.
                if (attemptedServiceStart && beforeByProcess.Count > 0)
                {
                    await Task.Delay(250, cancellationToken);
                    var tracked = CaptureNewProcesses(module, moduleState, beforeByProcess);
                    foreach (var process in tracked)
                    {
                        await output.WriteLineAsync($"  + {module.Name}: tracked process started with service ({process.Name}, PID {process.ProcessId})");
                    }

                    if (tracked.Count > 0)
                    {
                        SaveState(state);
                    }
                }

                continue;
            }

            // For process-only modules, one executable may spawn another executable in the
            // same module (UniSign is a real example). Start missing processes and then diff
            // every allowlisted process against the snapshot taken above.
            foreach (var processDefinition in module.Processes)
            {
                var currentIds = WindowsRuntime.GetProcessIds(processDefinition.Name);
                if (currentIds.Count > 0)
                {
                    var existedBefore = beforeByProcess[processDefinition.Name].Count > 0;
                    await output.WriteLineAsync(existedBefore
                        ? $"  = {module.Name}: process already running ({processDefinition.Name})"
                        : $"  + {module.Name}: process started by module launch ({processDefinition.Name})");
                    continue;
                }

                var result = WindowsRuntime.StartProcess(processDefinition, out _);
                await output.WriteLineAsync($"  {(result.Success ? "+" : "!")} {module.Name}: {result.Message}");
            }

            await Task.Delay(250, cancellationToken);
            var startedProcesses = CaptureNewProcesses(module, moduleState, beforeByProcess);

            if (startedProcesses.Count > 0)
            {
                SaveState(state);
            }
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync($"Session active until {state.ExpiresAtUtc.ToLocalTime():HH:mm:ss}.");
        await output.WriteLineAsync("Keep this command running. Ctrl+C will end the session and restore its changes.");

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var current = LoadActiveSession();
                if (current is null)
                {
                    await output.WriteLineAsync("Session was ended by another SecSwitch command.");
                    return 0;
                }

                var remaining = current.ExpiresAtUtc - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync("Session interrupted. Restoring modules...");
        }

        return await RestoreAndClearAsync(output);
    }

    public static async Task<int> StopAsync(TextWriter output)
    {
        if (!HasActiveSession)
        {
            await output.WriteLineAsync("No active SecSwitch session.");
            return 0;
        }

        return await RestoreAndClearAsync(output);
    }

    public static async Task<int> ExtendAsync(int minutes, TextWriter output)
    {
        if (minutes is < 1 or > 120)
        {
            await output.WriteLineAsync("Extension must be between 1 and 120 minutes.");
            return 2;
        }

        var state = LoadActiveSession();
        if (state is null)
        {
            await output.WriteLineAsync("No active SecSwitch session.");
            return 1;
        }

        var baseTime = state.ExpiresAtUtc > DateTimeOffset.UtcNow
            ? state.ExpiresAtUtc
            : DateTimeOffset.UtcNow;
        state.ExpiresAtUtc = baseTime.AddMinutes(minutes);
        SaveState(state);

        await output.WriteLineAsync($"Session extended by {minutes} minute(s). New expiry: {state.ExpiresAtUtc.ToLocalTime():HH:mm:ss}");
        return 0;
    }

    public static async Task<int> PrintStatusAsync(TextWriter output)
    {
        var state = LoadActiveSession();
        if (state is null)
        {
            await output.WriteLineAsync("No active SecSwitch session.");
            return 0;
        }

        var remaining = state.ExpiresAtUtc - DateTimeOffset.UtcNow;
        await output.WriteLineAsync($"Session: {state.SessionId}");
        await output.WriteLineAsync($"Started: {state.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        await output.WriteLineAsync($"Expires: {state.ExpiresAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        await output.WriteLineAsync($"Remaining: {FormatRemaining(remaining)}");

        foreach (var module in state.Modules)
        {
            if (module.StartedServices.Count == 0 && module.StartedProcesses.Count == 0)
            {
                continue;
            }

            await output.WriteLineAsync($"- {module.ModuleName}");
            foreach (var service in module.StartedServices)
            {
                await output.WriteLineAsync($"    service: {service}");
            }

            foreach (var process in module.StartedProcesses)
            {
                await output.WriteLineAsync($"    process: {process.Name} (PID {process.ProcessId})");
            }
        }

        return 0;
    }

    private static async Task<int> RestoreAndClearAsync(TextWriter output)
    {
        var state = LoadActiveSession();
        if (state is null)
        {
            TryDeleteState();
            await output.WriteLineAsync("No readable active session state. Cleared stale session marker.");
            return 1;
        }

        var failures = 0;
        await output.WriteLineAsync("Restoring modules started by SecSwitch...");

        foreach (var module in state.Modules.AsEnumerable().Reverse())
        {
            // First ask services to shut down normally. Some products spawn helper processes
            // after startup or keep monitors alive after the service stops, so this is followed
            // by a fresh allowlisted process sweep based on the session's pre-start baseline.
            foreach (var service in module.StartedServices.AsEnumerable().Reverse())
            {
                var result = WindowsRuntime.StopService(service);
                await output.WriteLineAsync($"  {(result.Success ? "-" : "!")} {module.ModuleName}: {result.Message}");
                if (!result.Success)
                {
                    failures++;
                }
            }

            if (module.AllowlistedProcessNames.Count > 0 || module.StartedProcesses.Count > 0)
            {
                await Task.Delay(350);
                failures += await CleanupNewAllowlistedProcessesAsync(module, output, maxPasses: 3);
            }

            // A monitor process can occasionally restart its service while cleanup is in
            // progress. Re-check only services that SecSwitch itself started, stop them again
            // if needed, then perform one final process sweep.
            var restartedService = false;
            foreach (var service in module.StartedServices.AsEnumerable().Reverse())
            {
                if (!WindowsRuntime.IsServiceRunning(service))
                {
                    continue;
                }

                restartedService = true;
                var result = WindowsRuntime.StopService(service);
                await output.WriteLineAsync($"  {(result.Success ? "-" : "!")} {module.ModuleName}: service restarted during cleanup; {result.Message}");
                if (!result.Success)
                {
                    failures++;
                }
            }

            if (restartedService && (module.AllowlistedProcessNames.Count > 0 || module.StartedProcesses.Count > 0))
            {
                await Task.Delay(350);
                failures += await CleanupNewAllowlistedProcessesAsync(module, output, maxPasses: 2);
            }
        }

        TryDeleteState();
        await output.WriteLineAsync(failures == 0
            ? "Session ended. SecSwitch changes were restored."
            : $"Session ended with {failures} restore error(s). Review the messages above.");

        return failures == 0 ? 0 : 1;
    }

    private static async Task<int> CleanupNewAllowlistedProcessesAsync(
        SessionModuleState module,
        TextWriter output,
        int maxPasses)
    {
        var processNames = module.AllowlistedProcessNames.Count > 0
            ? module.AllowlistedProcessNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : module.StartedProcesses.Select(process => process.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var baselineByName = module.PreexistingProcesses
            .GroupBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(process => process.ProcessId).ToHashSet(),
                StringComparer.OrdinalIgnoreCase);

        var failures = 0;

        for (var pass = 0; pass < maxPasses; pass++)
        {
            var foundNewProcess = false;

            foreach (var processName in processNames)
            {
                baselineByName.TryGetValue(processName, out var baselineIds);
                baselineIds ??= [];

                foreach (var processId in WindowsRuntime.GetProcessIds(processName))
                {
                    if (baselineIds.Contains(processId))
                    {
                        continue;
                    }

                    foundNewProcess = true;
                    var result = WindowsRuntime.StopProcess(processId, processName);
                    await output.WriteLineAsync($"  {(result.Success ? "-" : "!")} {module.ModuleName}: {result.Message}");
                    if (!result.Success)
                    {
                        failures++;
                    }
                }
            }

            if (!foundNewProcess)
            {
                break;
            }

            if (pass < maxPasses - 1)
            {
                await Task.Delay(300);
            }
        }

        foreach (var processName in processNames)
        {
            baselineByName.TryGetValue(processName, out var baselineIds);
            baselineIds ??= [];

            var residual = WindowsRuntime.GetProcessIds(processName)
                .Where(processId => !baselineIds.Contains(processId))
                .ToArray();

            foreach (var processId in residual)
            {
                await output.WriteLineAsync($"  ! {module.ModuleName}: process still running after cleanup ({processName}, PID {processId})");
                failures++;
            }
        }

        return failures;
    }

    private static List<SessionProcessState> CaptureNewProcesses(
        ModuleManifest module,
        SessionModuleState moduleState,
        IReadOnlyDictionary<string, HashSet<int>> beforeByProcess)
    {
        var captured = new List<SessionProcessState>();

        foreach (var processDefinition in module.Processes)
        {
            if (!beforeByProcess.TryGetValue(processDefinition.Name, out var beforeIds))
            {
                beforeIds = [];
            }

            foreach (var processId in WindowsRuntime.GetProcessIds(processDefinition.Name))
            {
                if (beforeIds.Contains(processId))
                {
                    continue;
                }

                if (moduleState.StartedProcesses.Any(process =>
                        process.ProcessId == processId &&
                        string.Equals(process.Name, processDefinition.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var processState = new SessionProcessState
                {
                    Name = processDefinition.Name,
                    ProcessId = processId
                };

                moduleState.StartedProcesses.Add(processState);
                captured.Add(processState);
            }
        }

        return captured;
    }

    private static void SaveState(SessionState state)
    {
        var directory = Path.GetDirectoryName(SessionFilePath)!;
        Directory.CreateDirectory(directory);

        var temp = SessionFilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temp, SessionFilePath, overwrite: true);
    }

    private static void TryDeleteState()
    {
        try
        {
            if (File.Exists(SessionFilePath))
            {
                File.Delete(SessionFilePath);
            }
        }
        catch
        {
            // A later status/start command will report the stale state file if deletion fails.
        }
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "expired";
        }

        return remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Minutes}:{remaining.Seconds:00}";
    }
}
