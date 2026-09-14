using SecSwitch.Core;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("SecSwitch currently supports Windows only.");
    return 2;
}

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "scan";
var modulesPath = GetOption(args, "--modules") ?? Path.Combine(Directory.GetCurrentDirectory(), "modules");

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

            var minutes = GetIntOption(args, "--minutes", 5);
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            return await SessionEngine.StartAsync(modules, minutes, Console.Out, cts.Token);
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

Console.WriteLine("SecSwitch — read-only scanner");
Console.WriteLine($"Modules: {loadedModules.Count}");
Console.WriteLine();

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

    var installedText = status.Installed ? "installed" : "not found";
    var runningText = status.Running ? "running" : "stopped";
    Console.WriteLine($"{module.Name,-32} {installedText,-10} {runningText}");

    foreach (var item in status.Evidence)
    {
        Console.WriteLine($"  - {item}");
    }
}

Console.WriteLine();
Console.WriteLine($"Detected {detected} supported module(s).");
return 0;

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
    Console.Error.WriteLine("  secswitch scan [--modules <path>]");
    Console.Error.WriteLine("  secswitch status [--modules <path>]");
    Console.Error.WriteLine("  secswitch session start [--minutes <n>] [--modules <path>]");
    Console.Error.WriteLine("  secswitch session status");
    Console.Error.WriteLine("  secswitch session extend [--minutes <n>]");
    Console.Error.WriteLine("  secswitch session stop");
}
