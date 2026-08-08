using System.Text.Json;
using System.Text.Json.Serialization;

namespace LilAgents;

/// <summary>
/// Replaces macOS UserDefaults with a JSON file under %APPDATA%\lil-agents.
/// Writes are debounced through <see cref="Save"/> being cheap and idempotent —
/// the file is tiny and only touched on user-driven changes.
/// </summary>
public sealed class Settings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly object Gate = new();
    private static Settings? _current;

    public static string Directory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, "lil-agents");
        }
    }

    public static string FilePath => Path.Combine(Directory, "settings.json");

    public static Settings Current
    {
        get
        {
            lock (Gate)
            {
                return _current ??= Load();
            }
        }
    }

    // MARK: - Persisted state

    public bool HasCompletedOnboarding { get; set; }
    public bool SoundsEnabled { get; set; } = true;
    public string ThemeName { get; set; } = "Peach";
    public int PinnedMonitorIndex { get; set; } = -1;

    /// <summary>Per-character provider, keyed by character name ("Bruce", "Jazz").</summary>
    public Dictionary<string, string> Providers { get; set; } = new();

    /// <summary>Per-character size, keyed by character name.</summary>
    public Dictionary<string, string> Sizes { get; set; } = new();

    /// <summary>Per-character manual visibility, keyed by character name.</summary>
    public Dictionary<string, bool> Visible { get; set; } = new();

    // OpenClaw gateway configuration.
    public string OpenClawGatewayUrl { get; set; } = "ws://localhost:3001";
    public string OpenClawAuthToken { get; set; } = "";
    public string OpenClawSessionPrefix { get; set; } = "lil-agents";
    public string? OpenClawAgentId { get; set; }

    /// <summary>Base64 Ed25519 private key backing the OpenClaw device identity.</summary>
    public string? OpenClawDeviceKey { get; set; }

    // MARK: - Accessors

    public string GetProvider(string character) =>
        Providers.TryGetValue(character, out var value) ? value : "claude";

    public void SetProvider(string character, string provider)
    {
        Providers[character] = provider;
        Save();
    }

    public string GetSize(string character) =>
        Sizes.TryGetValue(character, out var value) ? value : "large";

    public void SetSize(string character, string size)
    {
        Sizes[character] = size;
        Save();
    }

    public bool GetVisible(string character) =>
        !Visible.TryGetValue(character, out var value) || value;

    public void SetVisible(string character, bool visible)
    {
        Visible[character] = visible;
        Save();
    }

    // MARK: - Persistence

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<Settings>(json, SerializerOptions);
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable settings file should never stop the app starting;
            // fall back to defaults and let the next Save overwrite it.
        }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var json = JsonSerializer.Serialize(this, SerializerOptions);
            // Write-then-move so an interrupted write cannot truncate the live file.
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
