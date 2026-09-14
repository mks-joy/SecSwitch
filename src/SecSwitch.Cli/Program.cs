using SecSwitch.Core;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("SecSwitch currently supports Windows only.");
    return 2;
}

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "scan";
var modulesPath = GetOption(args, "--modules") ?? Path.Combine(Directory.GetCurrentDirectory(), "modules");

if (command is not ("scan" or "status"))
{
    Console.Error.WriteLine("Usage: secswitch [scan|status] [--modules <path>]");
    return 2;
}

var modules = ManifestLoader.LoadDirectory(modulesPath);
if (modules.Count == 0)
{
    Console.Error.WriteLine($"No module manifests found in: {modulesPath}");
    return 1;
}

Console.WriteLine("SecSwitch — read-only scanner");
Console.WriteLine($"Modules: {modules.Count}");
Console.WriteLine();

var detected = 0;
foreach (var module in modules)
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
