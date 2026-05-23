using System.Text.Json;
using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload.Runtime;

/// <summary>
/// 外置暂存：%LOCALAPPDATA%/STS2_ModHotReload/staging/{modId}/  
/// 编译/监视写入 mods 后先同步到暂存，热重载从暂存读取（避免游戏锁文件 + 保留最新构建）。
/// </summary>
internal static class ModStagingStore
{
    private static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "STS2_ModHotReload");

    internal static string StagingRoot => Path.Combine(RootDir, "staging");

    internal static string PendingFile => Path.Combine(RootDir, "pending-reloads.json");

    internal static string GetStagingModDir(string modId) =>
        Path.Combine(StagingRoot, modId);

    internal static void SyncFileFromLive(string modId, string liveFilePath)
    {
        if (!File.Exists(liveFilePath))
            return;

        string stagingDir = GetStagingModDir(modId);
        Directory.CreateDirectory(stagingDir);

        string name = Path.GetFileName(liveFilePath);
        string dest = Path.Combine(stagingDir, name);
        // 开发机可能先 push 到暂存（live 被游戏锁住无法更新）；勿用旧 live 覆盖新暂存
        if (File.Exists(dest) &&
            File.GetLastWriteTimeUtc(liveFilePath) < File.GetLastWriteTimeUtc(dest))
            return;

        CopyWithRetry(liveFilePath, dest);
        WriteManifest(modId, stagingDir);
    }

    /// <summary>编译产物直接写入暂存（游戏占用 mods 内 DLL 时由脚本/工程调用）。</summary>
    internal static void PushFileToStaging(string modId, string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException(sourceFilePath);

        string stagingDir = GetStagingModDir(modId);
        Directory.CreateDirectory(stagingDir);
        string dest = Path.Combine(stagingDir, Path.GetFileName(sourceFilePath));
        CopyWithRetry(sourceFilePath, dest);
        WriteManifest(modId, stagingDir);
    }

    /// <summary>热重载时优先从暂存目录读 DLL/PCK（若暂存不旧于 live）。</summary>
    internal static string GetEffectiveModRoot(Mod mod) =>
        mod.path;

    /// <summary>暂存与 live 中取 payload；PCK 默认优先 live（部署目录），DLL 优先较新者。</summary>
    internal static string? ResolvePayloadPath(Mod mod, string fileName) =>
        ResolvePayloadPath(mod, fileName, preferLive: fileName.EndsWith(".pck", StringComparison.OrdinalIgnoreCase));

    internal static string? ResolvePayloadPath(Mod mod, string fileName, bool preferLive)
    {
        string? modId = mod.manifest?.id;
        if (modId == null || string.IsNullOrEmpty(mod.path))
            return null;

        string staging = Path.Combine(GetStagingModDir(modId), fileName);
        string live = Path.Combine(mod.path, fileName);
        bool hasStaging = File.Exists(staging);
        bool hasLive = File.Exists(live);

        if (hasLive && !hasStaging)
            return live;
        if (hasStaging && !hasLive)
            return staging;
        if (hasStaging && hasLive)
        {
            if (preferLive)
                return File.GetLastWriteTimeUtc(live) >= File.GetLastWriteTimeUtc(staging) ? live : staging;
            return File.GetLastWriteTimeUtc(staging) >= File.GetLastWriteTimeUtc(live) ? staging : live;
        }

        return null;
    }

    internal static void MarkPending(string modId, ReloadChangeKind kind)
    {
        List<PendingEntry> list = LoadPendingMutable();
        PendingEntry? entry = list.FirstOrDefault(p => p.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            list.Add(new PendingEntry(modId, kind, DateTime.UtcNow));
        }
        else
        {
            entry.Kind = MergeKind(entry.Kind, kind);
            entry.SinceUtc = DateTime.UtcNow;
        }

        SavePending(list);
    }

    internal static IReadOnlyList<PendingEntry> LoadPending() => LoadPendingMutable();

    private static List<PendingEntry> LoadPendingMutable()
    {
        if (!File.Exists(PendingFile))
            return [];

        try
        {
            string json = File.ReadAllText(PendingFile);
            return JsonSerializer.Deserialize<List<PendingEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    internal static void ClearPending(string modId)
    {
        var list = LoadPending().Where(p => !p.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase)).ToList();
        SavePending(list);
    }

    internal static void ClearAllPending() => SavePending([]);

    private static void SavePending(List<PendingEntry> list)
    {
        Directory.CreateDirectory(RootDir);
        string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(PendingFile, json);
    }

    private static void WriteManifest(string modId, string stagingDir)
    {
        var manifest = new StagingManifest(modId, DateTime.UtcNow);
        string path = Path.Combine(stagingDir, ".hotreload-manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));
    }

    private static void CopyWithRetry(string src, string dest)
    {
        for (int i = 0; i < 12; i++)
        {
            try
            {
                File.Copy(src, dest, overwrite: true);
                return;
            }
            catch
            {
                Thread.Sleep(80);
            }
        }

        throw new IOException($"无法同步到暂存: {src} -> {dest}");
    }

    private static ReloadChangeKind MergeKind(ReloadChangeKind a, ReloadChangeKind b)
    {
        if (a == ReloadChangeKind.DllOrJson || b == ReloadChangeKind.DllOrJson)
            return ReloadChangeKind.DllOrJson;
        return ReloadChangeKind.PckOnly;
    }

    internal sealed class PendingEntry
    {
        public string ModId { get; set; } = "";
        public ReloadChangeKind Kind { get; set; }
        public DateTime SinceUtc { get; set; }

        public PendingEntry() { }

        public PendingEntry(string modId, ReloadChangeKind kind, DateTime sinceUtc)
        {
            ModId = modId;
            Kind = kind;
            SinceUtc = sinceUtc;
        }
    }

    private sealed record StagingManifest(string ModId, DateTime SyncedUtc);
}
