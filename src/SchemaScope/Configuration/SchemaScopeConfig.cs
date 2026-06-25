using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SchemaScope.Configuration;

public sealed class SchemaScopeConfig
{
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string VersionFolder { get; set; } = string.Empty;
    public string PrepatchFile { get; set; } = string.Empty;

    public string DefaultSchema { get; set; } = "dbo";

    public VersionScheme VersionScheme { get; set; } = new();
    public ConnectionSettings Connection { get; set; } = new();

    [JsonIgnore]
    public string? Path { get; private set; }

    public static string DefaultPath => AppPaths.ConfigFile;

    public static SchemaScopeConfig Load(string? explicitPath = null)
    {
        var writePath = string.IsNullOrWhiteSpace(explicitPath) ? DefaultPath : explicitPath;

        var config = ReadFrom(writePath);
        if (config is null && string.IsNullOrWhiteSpace(explicitPath))
        {
            config = ReadFrom(AppPaths.SeedConfigFile);
        }

        config ??= new SchemaScopeConfig();
        config.Path = writePath;
        return config;
    }

    public void Save()
    {
        try
        {
            var target = Path ?? DefaultPath;
            var dir = System.IO.Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(target, JsonSerializer.Serialize(this, SerializerOptions));
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Failed to save config '{Path ?? DefaultPath}'", ex);
        }
    }

    private static SchemaScopeConfig? ReadFrom(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            return JsonSerializer.Deserialize<SchemaScopeConfig>(json, SerializerOptions);
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Failed to read config '{path}'; falling back to defaults", ex);
            return null;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };
}
