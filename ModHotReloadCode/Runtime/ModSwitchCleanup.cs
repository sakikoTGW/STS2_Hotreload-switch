using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload.Runtime;

/// <summary>
/// Mod/Vanilla 切档与单 mod 关闭时的统一清理：等待异步队列、快照回滚、按 modId 清理缓存目录、反射卸载钩子。
/// </summary>
internal static class ModSwitchCleanup
{
    private static readonly string RootDir = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "STS2_ModHotReload");

    private static readonly string SnapshotFile = Path.Combine(RootDir, "switch-snapshot.json");

    private static int _switchDepth;

    internal static bool IsModeSwitchInProgress => Volatile.Read(ref _switchDepth) > 0;

    internal static string GetModCacheDir(string modId) =>
        Path.Combine(RootDir, "mods", Sanitize(modId), "cache");

    internal static void BeginModeSwitch()
    {
        Interlocked.Increment(ref _switchDepth);
        HotReloadCoordinator.PauseAutomaticReload("mode-switch");
    }

    internal static void EndModeSwitch()
    {
        Interlocked.Decrement(ref _switchDepth);
        HotReloadCoordinator.ResumeAutomaticReload("mode-switch");
    }

    internal static async Task WaitForQuiescenceAsync(int maxFrames = 45)
    {
        GameSafetyGuard.TryFlushPendingWhenSafe();

        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        for (int i = 0; i < maxFrames; i++)
        {
            GameSafetyGuard.TryFlushPendingWhenSafe();
            if (!HotReloadCoordinator.IsAnyReloadInProgress()
                && ModStagingStore.LoadPending().Count == 0)
                return;

            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        MainFile.Logger.Warn("[热重载] 切档前仍有 pending/重载未完成，继续切档（可能需手动 reload）。");
    }

    internal static ModSwitchSnapshot TakeSnapshot()
    {
        var snap = new ModSwitchSnapshot
        {
            SchemaVersion = 1,
            Mode = RuntimeModModeCoordinator.CurrentMode.ToString(),
            TakenUtc = DateTime.UtcNow,
            Mods = ModManager.Mods
                .Where(m => !string.Equals(m.manifest?.id, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
                .Select(m => new ModSwitchSnapshot.Entry
                {
                    Id = m.manifest?.id ?? "",
                    State = m.state.ToString(),
                    Enabled = m.state != ModLoadState.Disabled
                })
                .Where(e => !string.IsNullOrEmpty(e.Id))
                .ToList()
        };

        try
        {
            Directory.CreateDirectory(RootDir);
            File.WriteAllText(SnapshotFile, JsonSerializer.Serialize(snap, JsonOptions));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] 切档快照写入失败: {ex.Message}");
        }

        return snap;
    }

    internal static void CommitSnapshot()
    {
        try
        {
            if (File.Exists(SnapshotFile))
                File.Delete(SnapshotFile);
        }
        catch
        {
            // ignore
        }
    }

    internal static void RollbackSnapshot(ModSwitchSnapshot snapshot)
    {
        MainFile.Logger.Warn($"[热重载] 切档失败，尝试回滚到 {snapshot.Mode}（{snapshot.Mods.Count} 个 mod 记录）…");
        RuntimeModModeCoordinator.RollbackFromSnapshot(snapshot);
    }

    /// <summary>单 mod 关闭：卸载钩子 + 命名空间化缓存目录。</summary>
    internal static void TeardownMod(string modId, Assembly? assembly)
    {
        InvokeModUnloadHooks(modId, assembly);
        PurgeModScopedStorage(modId);
        WarnExternalModDataIfPresent(modId);
    }

    internal static void PurgeModScopedStorage(string modId)
    {
        string cacheDir = GetModCacheDir(modId);
        TryDeleteDirectory(cacheDir);

        string modRoot = Path.Combine(RootDir, "mods", Sanitize(modId));
        if (Directory.Exists(modRoot))
        {
            foreach (string sub in Directory.GetDirectories(modRoot))
            {
                string name = Path.GetFileName(sub);
                if (name.Equals("cache", StringComparison.OrdinalIgnoreCase))
                    continue;
                TryDeleteDirectory(sub);
            }
        }

        ModStagingStore.ClearPending(modId);
    }

    internal static void InvokeModUnloadHooks(string modId, Assembly? assembly)
    {
        if (assembly == null)
            return;

        string[] methodNames = ["OnModUnload", "OnDisable", "DisposeMod", "Unregister"];
        int invoked = 0;

        foreach (Type type in SafeTypes(assembly))
        {
            foreach (string name in methodNames)
            {
                MethodInfo? mi = type.GetMethod(
                    name,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
                if (mi == null)
                    continue;

                try
                {
                    mi.Invoke(null, null);
                    invoked++;
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Warn($"[热重载] {modId} {type.Name}.{name} 卸载钩子异常: {ex.Message}");
                }
            }
        }

        if (invoked > 0)
            MainFile.Logger.Info($"[热重载] {modId} 已调用 {invoked} 个卸载钩子。");
    }

    private static void WarnExternalModDataIfPresent(string modId)
    {
        string roaming = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "SlayTheSpire2",
            modId);
        if (Directory.Exists(roaming))
        {
            MainFile.Logger.Info(
                $"[热重载] 检测到 mod 数据目录（未自动删除）: {roaming} — 作者应使用 res://{modId}/ 或 {GetModCacheDir(modId)}");
        }
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
            MainFile.Logger.Info($"[热重载] 已清理 mod 缓存目录: {path}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] 清理目录失败 {path}: {ex.Message}");
        }
    }

    private static string Sanitize(string modId)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            modId = modId.Replace(c, '_');
        return modId;
    }

    private static JsonSerializerOptions JsonOptions => new() { WriteIndented = true };

    internal sealed class ModSwitchSnapshot
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "Modded";

        [JsonPropertyName("takenUtc")]
        public DateTime TakenUtc { get; set; }

        [JsonPropertyName("mods")]
        public List<Entry> Mods { get; set; } = [];

        internal sealed class Entry
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";

            [JsonPropertyName("state")]
            public string State { get; set; } = "";

            [JsonPropertyName("enabled")]
            public bool Enabled { get; set; }
        }
    }
}
