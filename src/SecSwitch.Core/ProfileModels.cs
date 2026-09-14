using System.Text.Json;

namespace SecSwitch.Core;

public sealed class SecSwitchProfile
{
    public int Version { get; init; } = 1;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> ModuleModes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class ProfileModes
{
    public const string Keep = "keep";
    public const string OnDemand = "ondemand";
}

public static class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string ProfileFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SecSwitch",
        "profile.json");

    public static bool Exists => File.Exists(ProfileFilePath);

    public static SecSwitchProfile Load()
    {
        if (!File.Exists(ProfileFilePath))
        {
            return new SecSwitchProfile();
        }

        try
        {
            return JsonSerializer.Deserialize<SecSwitchProfile>(File.ReadAllText(ProfileFilePath), JsonOptions)
                   ?? new SecSwitchProfile();
        }
        catch
        {
            return new SecSwitchProfile();
        }
    }

    public static void Save(SecSwitchProfile profile)
    {
        profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var directory = Path.GetDirectoryName(ProfileFilePath)!;
        Directory.CreateDirectory(directory);
        var temp = ProfileFilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profile, JsonOptions));
        File.Move(temp, ProfileFilePath, overwrite: true);
    }

    public static string GetMode(SecSwitchProfile profile, string moduleId)
    {
        return profile.ModuleModes.TryGetValue(moduleId, out var mode)
            ? mode
            : ProfileModes.Keep;
    }
}
