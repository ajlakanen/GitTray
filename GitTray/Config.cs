using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitTray;

/// <summary>
/// Configuration for GitTray application.
/// </summary>
public sealed class Config
{
    [JsonPropertyName("roots")] public List<string> Roots { get; set; } = new();
    [JsonPropertyName("ignorePatterns")] public List<string> IgnorePatterns { get; set; } = new();
    [JsonPropertyName("intervalSeconds")] public int IntervalSeconds { get; set; } = 60;
    [JsonPropertyName("repoDiscoveryMinutes")] public int RepoDiscoveryMinutes { get; set; } = 60;

    /// <summary>
    /// Path to the configuration file.
    /// </summary>
    public static string Path =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitTray",
            "config.json");


    /// <summary>
    /// Load configuration from file or return default if not found or invalid.
    /// </summary>
    public static Config Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(Path));
                if (cfg != null && cfg.Roots.Count > 0) return cfg;
            }
        }
        catch
        {
            /* fall through to default */
        }

        return Default();
    }

    /// <summary>
    /// Get the default configuration.
    /// </summary>
    /// <returns>Default Config instance.</returns>
    public static Config Default()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaultRoots = new List<string>();
        var common = new[]
        {
            System.IO.Path.Combine(userProfile, "source"), System.IO.Path.Combine(userProfile, "Documents"),
            Environment.CurrentDirectory
        };
        foreach (var r in common)
            if (Directory.Exists(r))
                defaultRoots.Add(r);
        return new Config
        {
            Roots = defaultRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            IgnorePatterns = new List<string> { "\\.git\\modules\\*", "node_modules", "bin\\*", "obj\\*" },
            IntervalSeconds = 60,
            RepoDiscoveryMinutes = 60
        };
    }
}