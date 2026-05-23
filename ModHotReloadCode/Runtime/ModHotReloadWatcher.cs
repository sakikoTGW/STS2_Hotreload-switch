using System.Collections.Concurrent;
using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload.Runtime;

/// <summary>监视 mods 目录；在 Godot 主线程去抖、等文件稳定后再重载。</summary>
public partial class ModHotReloadWatcher : Node
{
    private static double DebounceSeconds => ModHotReloadSettings.Current.DebounceSeconds;

    private static TimeSpan DuplicateEventWindow =>
        TimeSpan.FromMilliseconds(ModHotReloadSettings.Current.DuplicateEventWindowMs);

    private static readonly HashSet<string> WatchedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".pck", ".json"
    };

    private FileSystemWatcher? _watcher;
    private readonly ConcurrentQueue<ReloadQueueItem> _pending = new();
    private readonly Dictionary<string, DateTime> _lastEventUtcByPath = new(StringComparer.OrdinalIgnoreCase);
    private double _debounceLeft;
    private string _modsRoot = "";

    public override void _Ready()
    {
        string exe = OS.GetExecutablePath();
        string? dir = Path.GetDirectoryName(exe);
        if (string.IsNullOrEmpty(dir))
        {
            MainFile.Logger.Error("[热重载] 无法解析游戏目录。");
            return;
        }

        _modsRoot = Path.GetFullPath(Path.Combine(dir, "mods"));
        if (!Directory.Exists(_modsRoot))
        {
            MainFile.Logger.Warn($"[热重载] mods 目录不存在: {_modsRoot}");
            return;
        }

        _watcher = new FileSystemWatcher(_modsRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };
        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Renamed += OnFsRenamed;
        _watcher.Error += (_, e) => MainFile.Logger.Error($"[热重载] FileSystemWatcher: {e.GetException()}");
        _watcher.EnableRaisingEvents = true;

        SeedDllTimestamps();
        HotReloadCoordinator.SyncAllLoadedModsToStaging();
        RuntimeModModeCoordinator.ApplyStartupMode();
        GameSafetyGuard.TryFlushPendingWhenSafe();
        MainFile.Logger.Info($"[热重载] 监视: {_modsRoot}");
        MainFile.Logger.Info($"[热重载] 外置暂存: {ModStagingStore.StagingRoot}");
        MainFile.Logger.Info($"[热重载] 运行期 Mod 模式: {RuntimeModModeCoordinator.Status}");
    }

    public override void _Process(double delta)
    {
        GameSafetyGuard.TryEnsureCombatHook();
        GameSafetyGuard.TryFlushPendingWhenSafe();

        if (_pending.IsEmpty)
            return;

        _debounceLeft -= delta;
        if (_debounceLeft > 0)
            return;

        ProcessPendingBatch();
    }

    public override void _ExitTree()
    {
        if (_watcher == null)
            return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }

    private void ProcessPendingBatch()
    {
        // 同一 mod 夹并批：有 DLL/json 变更则全量重载，否则可只做 PCK
        var batch = new Dictionary<string, ReloadQueueItem>(StringComparer.OrdinalIgnoreCase);

        while (_pending.TryDequeue(out ReloadQueueItem item))
        {
            if (batch.TryGetValue(item.Folder, out ReloadQueueItem existing))
            {
                ReloadChangeKind merged = MergeKind(existing.Kind, item.Kind);
                batch[item.Folder] = item with { Kind = merged };
            }
            else
            {
                batch[item.Folder] = item;
            }
        }

        foreach (ReloadQueueItem item in batch.Values)
        {
            Mod? mod = HotReloadCoordinator.FindModByFolder(item.Folder);
            if (mod == null)
            {
                MainFile.Logger.Warn($"[热重载] 队列中的模组未加载: {item.Folder}");
                continue;
            }

            if (!ModFileUtil.WaitForStableFile(item.TriggerPath, maxAttempts: 30, delayMs: 80))
            {
                MainFile.Logger.Warn($"[热重载] 文件未稳定，跳过: {item.TriggerPath}");
                continue;
            }

            HotReloadCoordinator.OnLiveFileChanged(item.Folder, item.TriggerPath, item.Kind);
        }

        if (!_pending.IsEmpty)
            _debounceLeft = DebounceSeconds;
    }

    private static ReloadChangeKind MergeKind(ReloadChangeKind a, ReloadChangeKind b)
    {
        if (a == ReloadChangeKind.DllOrJson || b == ReloadChangeKind.DllOrJson)
            return ReloadChangeKind.DllOrJson;
        if (a == ReloadChangeKind.PckOnly || b == ReloadChangeKind.PckOnly)
            return ReloadChangeKind.PckOnly;
        return ReloadChangeKind.Unknown;
    }

    private void OnFsRenamed(object sender, RenamedEventArgs e) => Enqueue(e.FullPath);

    private void OnFsEvent(object sender, FileSystemEventArgs e) => Enqueue(e.FullPath);

    private void Enqueue(string fullPath)
    {
        if (string.IsNullOrEmpty(_modsRoot))
            return;

        if (!ModHotReloadSettings.Current.FileWatchEnabled
            || !ModHotReloadSettings.Current.HotReloadEnabled)
            return;

        string ext = Path.GetExtension(fullPath);
        if (!WatchedExtensions.Contains(ext))
            return;

        string? folder = GetModFolderName(fullPath);
        if (folder == null || string.Equals(folder, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
            return;

        ReloadChangeKind kind = HotReloadCoordinator.ClassifyChange(fullPath);
        DateTime now = DateTime.UtcNow;
        if (_lastEventUtcByPath.TryGetValue(fullPath, out DateTime lastUtc)
            && (now - lastUtc) < DuplicateEventWindow)
            return;
        _lastEventUtcByPath[fullPath] = now;

        _pending.Enqueue(new ReloadQueueItem(folder, fullPath, kind));
        _debounceLeft = DebounceSeconds;
    }

    private string? GetModFolderName(string fullPath)
    {
        try
        {
            string relative = Path.GetRelativePath(_modsRoot, fullPath);
            string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return parts.Length >= 2 ? parts[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SeedDllTimestamps()
    {
        foreach (Mod mod in MegaCrit.Sts2.Core.Modding.ModManager.Mods)
        {
            string? id = mod.manifest?.id;
            if (id == null || string.IsNullOrEmpty(mod.path))
                continue;

            string dll = Path.Combine(mod.path, id + ".dll");
            if (File.Exists(dll))
                HotReloadCoordinator.SeedDllTimestamp(id, File.GetLastWriteTimeUtc(dll).Ticks);
        }
    }
}
