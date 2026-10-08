using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AudioSwitch.Core.Settings;

public static class JsonFile
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Loads <paramref name="path"/>; writes <paramref name="defaults"/> there if it doesn't exist. A file that
    /// can't be parsed is left untouched (so hand edits aren't lost) and the defaults are used.</summary>
    public static T LoadOrCreate<T>(string path, T defaults, ILogger log)
    {
        if (!File.Exists(path))
        {
            Save(path, defaults);
            log.LogInformation("Created {Path} with defaults", path);
            return defaults;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? defaults;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            log.LogError("Could not read {Path} ({Error}); using defaults", path, ex.Message);
            return defaults;
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
