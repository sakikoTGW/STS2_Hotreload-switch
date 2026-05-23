using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Rooms;
using ModHotReload.Reflection;

namespace ModHotReload.Runtime;

/// <summary>战斗内 DLL 重载：默认立即执行；可选排队至战斗结束（见 CombatReloadSettings）。</summary>
internal static class GameSafetyGuard
{
    private static CombatManager? _hookedInstance;
    private static bool _applyingPending;
    private static DateTime _nextGlobalFlushUtc = DateTime.MinValue;
    private static readonly Dictionary<string, DateTime> NextRetryUtc = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>仅集成测试使用：模拟「战斗进行中」。</summary>
    internal static bool? TestOverrideInCombat;

    internal static bool IsInCombat =>
        TestOverrideInCombat
        ?? CombatManager.Instance?.IsInProgress == true;

    /// <summary>是否应把 DLL 变更排队到战斗结束（仅 Defer 模式）。</summary>
    internal static bool ShouldQueueDllReload =>
        IsInCombat && CombatReloadSettings.DeferDllReloadUntilCombatEnds;

    /// <summary>战斗中（用于告警：手牌等可能仍持有旧程序集类型）。</summary>
    internal static bool IsDllReloadUnsafe => IsInCombat;

    internal static void AttachTo(CombatManager instance)
    {
        if (_hookedInstance == instance)
            return;

        if (_hookedInstance != null)
            _hookedInstance.CombatEnded -= OnCombatEnded;

        bool firstHook = _hookedInstance == null;
        _hookedInstance = instance;
        instance.CombatEnded += OnCombatEnded;
        if (firstHook)
            MainFile.Logger.Info("[热重载] 已订阅 CombatEnded（CombatManager 实例）。");
    }

    /// <summary>菜单/主界面：Instance 可能晚于 mod 初始化。</summary>
    internal static void TryEnsureCombatHook()
    {
        if (CombatManager.Instance != null)
            AttachTo(CombatManager.Instance);
    }

    internal static void EnsureCombatEndedHook() => TryEnsureCombatHook();

    internal static void OnCombatEndedFlush() => ApplyAllPending(force: true);

    private static void OnCombatEnded(CombatRoom _) => OnCombatEndedFlush();

    internal static void TryFlushPendingWhenSafe()
    {
        // 强约束：战斗中不执行 pending 应用，避免抢占战斗主线程。
        if (IsInCombat || ShouldQueueDllReload)
            return;
        if (DateTime.UtcNow < _nextGlobalFlushUtc)
            return;

        ApplyAllPending(force: true);
    }

    private static void ApplyAllPending(bool force)
    {
        if (_applyingPending)
            return;

        var pending = ModStagingStore.LoadPending().ToList();
        if (pending.Count == 0)
            return;

        _applyingPending = true;
        try
        {
            MainFile.Logger.Info($"[热重载] 应用 {pending.Count} 个暂存 DLL 重载…");
            ApplyPendingEntries(pending, force);
            _nextGlobalFlushUtc = DateTime.UtcNow.AddMilliseconds(800);
        }
        finally
        {
            _applyingPending = false;
        }
    }

    private static void ApplyPendingEntries(List<ModStagingStore.PendingEntry> pending, bool force)
    {
        var pendingById = pending.ToDictionary(p => p.ModId, StringComparer.OrdinalIgnoreCase);
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string id in ModDependencyCascade.GetReloadOrder())
        {
            if (!pendingById.TryGetValue(id, out ModStagingStore.PendingEntry? entry))
                continue;

            ProcessPendingEntry(entry, force);
            processed.Add(id);
        }

        foreach (ModStagingStore.PendingEntry entry in pending)
        {
            if (processed.Contains(entry.ModId))
                continue;

            ProcessPendingEntry(entry, force);
        }
    }

    private static void ProcessPendingEntry(ModStagingStore.PendingEntry entry, bool force)
    {
        if (NextRetryUtc.TryGetValue(entry.ModId, out DateTime next) && DateTime.UtcNow < next)
            return;

        Mod? mod = HotReloadCoordinator.FindModByFolder(entry.ModId);
        if (mod == null)
        {
            ModStagingStore.ClearPending(entry.ModId);
            NextRetryUtc.Remove(entry.ModId);
            MainFile.Logger.Warn($"[热重载] 待处理 mod 未加载: {entry.ModId}");
            return;
        }

        string? modId = mod.manifest?.id;
        if (modId != null
            && (mod.state == ModLoadState.Disabled
                || ModManagerReflection.IsModDisabled(modId, mod.modSource)))
        {
            ModStagingStore.ClearPending(entry.ModId);
            NextRetryUtc.Remove(entry.ModId);
            MainFile.Logger.Info($"[热重载] {modId} 已禁用，丢弃 pending 重载。");
            return;
        }

        if (entry.Kind == ReloadChangeKind.PckOnly)
        {
            ModStagingStore.ClearPending(entry.ModId);
            NextRetryUtc.Remove(entry.ModId);
            return;
        }

        HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force);
        if (mod.state == ModLoadState.Loaded)
            NextRetryUtc.Remove(entry.ModId);
    }

    internal static void ScheduleRetry(string modId, double backoffSeconds)
    {
        DateTime retryAt = DateTime.UtcNow.AddSeconds(backoffSeconds);
        NextRetryUtc[modId] = retryAt;
        MainFile.Logger.Warn($"[热重载] {modId} 退避到 {retryAt:HH:mm:ss} 再重试。");
    }
}
