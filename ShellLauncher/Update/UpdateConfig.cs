using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShellLauncher
{
    /// <summary>
    /// Persisted update settings — C:\ProgramData\ShellLauncher\update_config.json.
    /// Created with defaults on first run.
    /// </summary>
    public class UpdateConfig
    {
        /// <summary>GitHub Releases API endpoint for this project.</summary>
        public string GithubReleasesUrl { get; set; } =
            "https://api.github.com/repos/Auseroth/ShellLauncher/releases/latest";

        // ─── Persistence ─────────────────────────────────────────────────────

        [JsonIgnore]
        public static string FilePath { get; } =
            @"C:\ProgramData\ShellLauncher\update_config.json";

        private static readonly JsonSerializerOptions _writeOpts =
            new JsonSerializerOptions { WriteIndented = true };

        public static UpdateConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var cfg = JsonSerializer.Deserialize<UpdateConfig>(File.ReadAllText(FilePath));
                    if (cfg != null) return cfg;
                }
            }
            catch { /* fall through to defaults */ }

            var defaults = new UpdateConfig();
            defaults.Save();
            return defaults;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, _writeOpts));
            }
            catch { }
        }
    }
}