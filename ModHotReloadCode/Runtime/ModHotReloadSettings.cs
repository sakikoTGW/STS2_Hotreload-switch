using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModHotReload.Runtime;

/// <summary>
/// 外置配置：%LOCALAPPDATA%/STS2_ModHotReload/config.json（SchemaVersion 用于后续兼容）。
/// </summary>
internal sealed class ModHotReloadSettings
{
    private static ModHotReloadSettings? _current;
    private static readonly object Gate = new();

    internal const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>总开关：false 时忽略文件监视触发的重载（控制台 reload 仍可用）。</summary>
    [JsonPropertyName("hotReloadEnabled")]
    public bool HotReloadEnabled { get; set; } = true;

    [JsonPropertyName("fileWatchEnabled")]
    public bool FileWatchEnabled { get; set; } = true;

    [JsonPropertyName("debounceSeconds")]
    public double DebounceSeconds { get; set; } = 2.4;

    [JsonPropertyName("minReloadIntervalSeconds")]
    public double MinReloadIntervalSeconds { get; set; } = 1.5;

    [JsonPropertyName("maxReloadRetries")]
    public int MaxReloadRetries { get; set; } = 3;

    [JsonPropertyName("retryBackoffSeconds")]
    public double RetryBackoffSeconds { get; set; } = 4.0;

    [JsonPropertyName("duplicateEventWindowMs")]
    public double DuplicateEventWindowMs { get; set; } = 900;

    /// <summary>BaseLib 热重载成功后是否自动 reloadall（默认 false，避免误伤大量依赖 mod）。</summary>
    [JsonPropertyName("cascadeReloadAllOnBaseLib")]
    public bool CascadeReloadAllOnBaseLib { get; set; }

    /// <summary>任意 mod 重载成功后是否级联重载 manifest.dependencies 中的已加载 mod。</summary>
    [JsonPropertyName("cascadeDependentsOnReload")]
    public bool CascadeDependentsOnReload { get; set; } = true;

    internal static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "STS2_ModHotReload",
        "config.json");

    internal static ModHotReloadSettings Current
    {
        get
        {
            lock (Gate)
                return _current ??= Load();
        }
    }

    internal static void Reload()
    {
        lock (Gate)
            _current = Load();
    }

    internal static void Save(ModHotReloadSettings settings)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        string dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, options));
        lock (Gate)
            _current = settings;
    }

    private static ModHotReloadSettings Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                var defaults = new ModHotReloadSettings();
                Save(defaults);
                return defaults;
            }

            string json = File.ReadAllText(ConfigPath);
            ModHotReloadSettings? loaded = JsonSerializer.Deserialize<ModHotReloadSettings>(json);
            if (loaded == null)
                return new ModHotReloadSettings();

            if (loaded.SchemaVersion > CurrentSchemaVersion)
                MainFile.Logger.Warn(
                    $"[热重载] config schema {loaded.SchemaVersion} 较新，部分字段可能无效（当前支持 {CurrentSchemaVersion}）。");

            loaded.Clamp();
            return loaded;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] 读取 config.json 失败，使用默认: {ex.Message}");
            return new ModHotReloadSettings();
        }
    }

    private void Clamp()
    {
        DebounceSeconds = Math.Clamp(DebounceSeconds, 0.2, 30);
        MinReloadIntervalSeconds = Math.Clamp(MinReloadIntervalSeconds, 0, 60);
        MaxReloadRetries = Math.Clamp(MaxReloadRetries, 0, 20);
        RetryBackoffSeconds = Math.Clamp(RetryBackoffSeconds, 0.5, 120);
        DuplicateEventWindowMs = Math.Clamp(DuplicateEventWindowMs, 0, 10_000);
    }

    internal string Describe() =>
        $"hotReload={HotReloadEnabled} watch={FileWatchEnabled} debounce={DebounceSeconds}s " +
        $"interval={MinReloadIntervalSeconds}s retries={MaxReloadRetries} backoff={RetryBackoffSeconds}s " +
        $"cascadeBaseLibAll={CascadeReloadAllOnBaseLib} cascadeDeps={CascadeDependentsOnReload} " +
        $"schema={SchemaVersion}";
}
