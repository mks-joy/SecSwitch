using System.Text.Json;

namespace SecSwitch.Core;

public static class ManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<ModuleManifest> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var modules = new List<ModuleManifest>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var json = File.ReadAllText(file);
            var module = JsonSerializer.Deserialize<ModuleManifest>(json, JsonOptions)
                         ?? throw new InvalidDataException($"Could not parse module manifest: {file}");
            modules.Add(module);
        }

        return modules;
    }
}
