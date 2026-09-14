using SecSwitch.Core;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("SecSwitch currently supports Windows only.");
    return 2;
}

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "scan";
var modulesPath = GetOption(args, "--modules") ?? Path.Combine(Directory.GetCurrentDirectory(), "modules");

if (command == "setup")
{
    var modules = LoadModulesOrExit(modulesPath);
    if (modules is null)
    {
        return 1;
    }

    return RunSetup(modules);
}

if (command == "profile")
{
    var subcommand = args.Skip(1).FirstOrDefault()?.ToLowerInvariant() ?? "show";
    if (subcommand != "show")
    {
        PrintUsage();
        return 2;
    }

    var modules = LoadModulesOrExit(modulesPath);
    if (modules is null)
    {
        return 1;
    }

    var profile = ProfileStore.Load();
    Console.WriteLine($"Profile: {ProfileStore.ProfileFilePath}");
    Console.WriteLine(ProfileStore.Exists ? $"Updated: {profile.UpdatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : "No saved profile yet.");
    Console.WriteLine();

    foreach (var module in modules)
    {
        var status = ModuleScanner.Scan(module);
        if (!status.Installed)
        {
            continue;
        }

        var storedMode = ProfileStore.GetMode(profile, module.Id);
        var modeText = IsObserveOnly(module) && string.Equals(storedMode, ProfileModes.OnDemand, StringComparison.OrdinalIgnoreCase)
            ? "ondemand (currently observe-only; session control disabled)"
            : storedMode;

        Console.WriteLine($"{module.Name,-32} {modeText}");
    }

    return 0;
}

if (command == "session")
{
    var subcommand = args.Skip(1).FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))?.ToLowerInvariant()
                     ?? "status";

    switch (subcommand)
    {
        case "start":
        {
            var modules = LoadModulesOrExit(modulesPath);
            if (modules is null)
            {
                return 1;
            }

            if (!ProfileStore.Exists)
            {
                Console.Error.WriteLine("No SecSwitch profile found. Run 'secswitch setup' first.");
                return 1;
            }

            var profile = ProfileStore.Load();
            var requestedModules = modules
                .Where(module => string.Equals(ProfileStore.GetMode(profile, module.Id), ProfileModes.OnDemand, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var skippedModules = requestedModules.Where(IsObserveOnly).ToArray();
            foreach (var module in skippedModules)
            {
                Console.WriteLine($"~ {module.Name}: profile requests on-demand management, but this module is currently observe-only and will not be changed.");
            }

            var managedModules = requestedModules.Where(module => !IsObserveOnly(module)).ToArray();

            if (managedModules.Length == 0)
            {
                Console.Error.WriteLine("No modules with verified runtime control are configured for on-demand management. Run 'secswitch setup'.");
                return 1;
            }

            var minutes = GetIntOption(args, "--minutes", 5);
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            return await SessionEngine.StartAsync(managedModules, minutes, Console.Out, cts.Token);
        }

        case "stop":
            return await SessionEngine.StopAsync(Console.Out);

        case "extend":
        {
            var minutes = GetIntOption(args, "--minutes", 5);
            return await SessionEngine.ExtendAsync(minutes, Console.Out);
        }

        case "status":
            return await SessionEngine.PrintStatusAsync(Console.Out);

        default:
            PrintUsage();
            return 2;
    }
}

if (command is not ("scan" or "status"))
{
    PrintUsage();
    return 2;
}

var loadedModules = LoadModulesOrExit(modulesPath);
if (loadedModules is null)
{
    return 1;
}

Console.WriteLine("SecSwitch — scanner");
Console.WriteLine($"Modules: {loadedModules.Count}");
Console.WriteLine("Sampling CPU usage for 1 second...");
Console.WriteLine();

var resourceUsage = ResourceSampler.MeasureAll(loadedModules);
var detected = 0;
foreach (var module in loadedModules)
{
    var status = ModuleScanner.Scan(module);
    if (!status.Installed && command == "scan")
    {
        continue;
    }

    if (status.Installed)
    {
        detected++;
    }

    if (resourceUsage.TryGetValue(module.Id, out var usage))
    {
        status.CpuPercent = usage.CpuPercent;
        status.WorkingSetBytes = usage.WorkingSetBytes;
        status.RunningProcessCount = usage.ProcessCount;
    }

    var installedText = status.Installed ? "installed" : "not found";
    var runningText = status.Running ? "running" : "stopped";
    var startType = status.ServiceStartTypes.Count == 0
        ? "process"
        : string.Join(", ", status.ServiceStartTypes.Select(value => value.Split(':', 2)[1].Trim()));

    Console.WriteLine($"{module.Name,-32} {installedText,-10} {runningText,-8} {startType,-20} CPU {status.CpuPercent,5:0.00}%  RAM {status.WorkingSetBytes / 1024d / 1024d,6:0.0} MB");

    if (!string.IsNullOrWhiteSpace(module.DescriptionKo))
    {
        Console.WriteLine($"  {module.DescriptionKo}");
    }

    if (module.KnownUses.Count > 0)
    {
        Console.WriteLine($"  알려진 사용처: {string.Join(", ", module.KnownUses)}");
    }

    if (IsObserveOnly(module))
    {
        Console.WriteLine("  SecSwitch 제어 상태: 관찰만 지원 (안전한 종료/원복 경로 검증 전)");
    }

    if (command == "status")
    {
        foreach (var item in status.Evidence)
        {
            Console.WriteLine($"  - {item}");
        }
    }
}

Console.WriteLine();
Console.WriteLine($"Detected {detected} supported module(s).");
return 0;

static int RunSetup(IReadOnlyList<ModuleManifest> modules)
{
    var installed = modules
        .Select(module => ModuleScanner.Scan(module))
        .Where(status => status.Installed)
        .ToArray();

    if (installed.Length == 0)
    {
        Console.WriteLine("No supported security modules were detected.");
        return 0;
    }

    Console.WriteLine("SecSwitch first-run setup");
    Console.WriteLine("설치된 웹 보안 모듈을 확인하고, 그대로 둘지 On-demand로 관리할지 선택합니다.");
    Console.WriteLine("On-demand를 선택하면 가능한 서비스는 Manual로 바꾸고 현재 실행을 정리합니다.");
    Console.WriteLine();
    Console.WriteLine("Sampling CPU usage for 1 second...");
    var resources = ResourceSampler.MeasureAll(modules);
    var profile = ProfileStore.Load();

    foreach (var status in installed)
    {
        var module = status.Module;
        resources.TryGetValue(module.Id, out var usage);
        usage ??= new ModuleResourceUsage(0, 0, 0);

        Console.WriteLine(new string('-', 72));
        Console.WriteLine(module.Name);
        if (!string.IsNullOrWhiteSpace(module.Vendor))
        {
            Console.WriteLine($"제조사: {module.Vendor}");
        }
        if (!string.IsNullOrWhiteSpace(module.DescriptionKo))
        {
            Console.WriteLine($"설명: {module.DescriptionKo}");
        }
        if (!string.IsNullOrWhiteSpace(module.Purpose))
        {
            Console.WriteLine($"용도: {module.Purpose}");
        }
        if (module.KnownUses.Count > 0)
        {
            Console.WriteLine($"알려진 사용처: {string.Join(", ", module.KnownUses)}");
        }

        var startType = status.ServiceStartTypes.Count == 0
            ? "일반 프로세스"
            : string.Join(", ", status.ServiceStartTypes);
        Console.WriteLine($"현재 상태: {(status.Running ? "실행 중" : "중지")} / {startType}");
        Console.WriteLine($"현재 점유: CPU {usage.CpuPercent:0.00}% / RAM {usage.WorkingSetBytes / 1024d / 1024d:0.0} MB / 프로세스 {usage.ProcessCount}개");

        if (IsObserveOnly(module))
        {
            Console.WriteLine("현재 이 모듈은 안전한 종료/원복 경로가 검증되지 않아 관찰만 지원합니다. 설정을 변경하지 않습니다.");
            profile.ModuleModes[module.Id] = ProfileModes.Keep;
            continue;
        }

        Console.Write("선택: [K] 그대로 유지 / [O] On-demand 관리 (기본 K): ");
        var answer = (Console.ReadLine() ?? string.Empty).Trim();
        if (!answer.Equals("o", StringComparison.OrdinalIgnoreCase))
        {
            profile.ModuleModes[module.Id] = ProfileModes.Keep;
            Console.WriteLine("→ 유지: 현재 설정을 변경하지 않습니다.");
            continue;
        }

        Console.WriteLine("→ On-demand 설정 적용 중...");
        var result = OnDemandConfigurator.Apply(module);
        foreach (var message in result.Messages)
        {
            Console.WriteLine($"   {(result.Success ? "·" : "!")} {message}");
        }

        profile.ModuleModes[module.Id] = result.Success ? ProfileModes.OnDemand : ProfileModes.Keep;
        Console.WriteLine(result.Success
            ? "→ 완료: 이 모듈은 SecSwitch 5분 세션 대상에 포함됩니다."
            : "→ 일부 조치에 실패하여 안전을 위해 '유지'로 저장했습니다.");
    }

    ProfileStore.Save(profile);
    Console.WriteLine();
    Console.WriteLine($"설정 저장 완료: {ProfileStore.ProfileFilePath}");
    Console.WriteLine("이후에는 'secswitch session start --minutes 5'로 On-demand 모듈을 준비할 수 있습니다.");
    return 0;
}

static bool IsObserveOnly(ModuleManifest module)
{
    return string.Equals(module.SessionControl, "observeOnly", StringComparison.OrdinalIgnoreCase);
}

static IReadOnlyList<ModuleManifest>? LoadModulesOrExit(string modulesPath)
{
    var modules = ManifestLoader.LoadDirectory(modulesPath);
    if (modules.Count > 0)
    {
        return modules;
    }

    Console.Error.WriteLine($"No module manifests found in: {modulesPath}");
    return null;
}

static string? GetOption(string[] arguments, string option)
{
    for (var i = 0; i < arguments.Length - 1; i++)
    {
        if (string.Equals(arguments[i], option, StringComparison.OrdinalIgnoreCase))
        {
            return arguments[i + 1];
        }
    }

    return null;
}

static int GetIntOption(string[] arguments, string option, int defaultValue)
{
    var raw = GetOption(arguments, option);
    return int.TryParse(raw, out var parsed) ? parsed : defaultValue;
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  secswitch setup [--modules <path>]");
    Console.Error.WriteLine("  secswitch scan [--modules <path>]");
    Console.Error.WriteLine("  secswitch status [--modules <path>]");
    Console.Error.WriteLine("  secswitch profile show [--modules <path>]");
    Console.Error.WriteLine("  secswitch session start [--minutes <n>] [--modules <path>]");
    Console.Error.WriteLine("  secswitch session status");
    Console.Error.WriteLine("  secswitch session extend [--minutes <n>]");
    Console.Error.WriteLine("  secswitch session stop");
}
