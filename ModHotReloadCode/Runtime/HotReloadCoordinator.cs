using System.Reflection;
using MegaCrit.Sts2.Core.Modding;
using ModHotReload.Reflection;

namespace ModHotReload.Runtime;

internal static class HotReloadCoordinator
{
    private static readonly HashSet<string> ReloadingModIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, long> LastDllTicks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DateTime> LastReloadAttemptUtc = new(StringComparer.OrdinalIgnoreCase);
    private static bool _reloadAllInProgress;
    private static int _automaticReloadPauseDepth;
    private static readonly Dictionary<string, int> FailCounts = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsAnyReloadInProgress()
    {
        lock (ReloadingModIds)
            return ReloadingModIds.Count > 0;
    }

    internal static void PauseAutomaticReload(string reason)
    {
        Interlocked.Increment(ref _automaticReloadPauseDepth);
        MainFile.Logger.Info($"[热重载] 暂停自动重载 ({reason})");
    }

    internal static void ResumeAutomaticReload(string reason)
    {
        if (Interlocked.Decrement(ref _automaticReloadPauseDepth) < 0)
            Interlocked.Exchange(ref _automaticReloadPauseDepth, 0);
        MainFile.Logger.Info($"[热重载] 恢复自动重载 ({reason})");
    }

    private static bool IsAutomaticReloadPaused => Volatile.Read(ref _automaticReloadPauseDepth) > 0;

    private static TimeSpan MinReloadInterval =>
        TimeSpan.FromSeconds(ModHotReloadSettings.Current.MinReloadIntervalSeconds);

    internal static bool IsAutomaticHotReloadEnabled =>
        ModHotReloadSettings.Current.HotReloadEnabled;

    internal static bool IsReloading(string modId) => ReloadingModIds.Contains(modId);

    internal static void SeedDllTimestamp(string modId, long ticks) => LastDllTicks[modId] = ticks;

    internal static void ClearReloadThrottle(string modId)
    {
        LastReloadAttemptUtc.Remove(modId);
        LastDllTicks.Remove(modId);
    }

    internal static void RememberDllTimestamp(Mod mod)
    {
        string? modId = mod.manifest?.id;
        if (string.IsNullOrEmpty(modId))
            return;
        RememberDllTicks(mod, modId);
    }

    internal static void SyncModToStaging(Mod mod) => SyncModFolderToStaging(mod);

    internal static void ClearThrottle(string modId) => ClearReloadThrottle(modId);

    internal static void SyncModFolderToStaging(Mod mod)
    {
        string modId = mod.manifest!.id;
        string[] names = [modId + ".dll", modId + ".pck", modId + ".json"];
        foreach (string name in names)
        {
            string live = Path.Combine(mod.path, name);
            if (File.Exists(live))
                ModStagingStore.SyncFileFromLive(modId, live);
        }
    }

    internal static void ReloadByFolderName(string folderName) =>
        ReloadByFolderName(folderName, ReloadChangeKind.DllOrJson, force: false);

    internal static void ReloadByFolderName(string folderName, ReloadChangeKind kind, bool force = false)
    {
        Mod? mod = FindModByFolder(folderName);
        if (mod == null)
        {
            MainFile.Logger.Warn($"[热重载] 未找到模组目录 '{folderName}'。");
            return;
        }

        Reload(mod, kind, force);
    }

    internal static void ReloadAllLoadedMods()
    {
        _reloadAllInProgress = true;
        ModDependencyCascade.ClearAllLoadedModModels();

        var ids = ModDependencyCascade.GetReloadOrder();

        MainFile.Logger.Info($"[热重载] reloadall：{ids.Count} 个模组");

        try
        {
            foreach (string id in ids)
            {
                try
                {
                    ReloadByFolderName(id, ReloadChangeKind.DllOrJson, force: true);
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Error($"[热重载] reloadall 跳过 {id}（不影响其它 mod）: {ex.Message}");
                }
            }
        }
        finally
        {
            _reloadAllInProgress = false;
        }
    }

    internal static void NotifyReloadFailed(string modId, ReloadChangeKind kind)
    {
        int fails = FailCounts.TryGetValue(modId, out int n) ? n + 1 : 1;
        FailCounts[modId] = fails;

        var settings = ModHotReloadSettings.Current;
        if (fails >= settings.MaxReloadRetries)
        {
            MainFile.Logger.Error(
                $"[热重载] {modId} 已达最大重试 {settings.MaxReloadRetries} 次，请检查 mod 日志后手动 reload。");
            FailCounts.Remove(modId);
            return;
        }

        ModStagingStore.MarkPending(modId, kind);
        GameSafetyGuard.ScheduleRetry(modId, settings.RetryBackoffSeconds);
        MainFile.Logger.Warn(
            $"[热重载] {modId} 失败 ({fails}/{settings.MaxReloadRetries})，已排队重试。");
    }

    private static void NotifyReloadSucceeded(string modId) => FailCounts.Remove(modId);

    internal static void Reload(Mod mod, ReloadChangeKind kind = ReloadChangeKind.DllOrJson, bool force = false)
    {
        string? modId = mod.manifest?.id;
        if (string.IsNullOrEmpty(modId))
            return;

        if (!force && (!IsAutomaticHotReloadEnabled || IsAutomaticReloadPaused || ModSwitchCleanup.IsModeSwitchInProgress))
        {
            if (IsAutomaticReloadPaused || ModSwitchCleanup.IsModeSwitchInProgress)
                MainFile.Logger.Info($"[热重载] 切档/暂停中，跳过自动重载 {modId}。");
            else
                MainFile.Logger.Info($"[热重载] 已关闭自动热重载（config hotReloadEnabled=false），跳过 {modId}。");
            return;
        }

        if (string.Equals(modId, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
        {
            MainFile.Logger.Warn("[热重载] 不能热重载 ModHotReload 自身。");
            return;
        }

        if (ModManagerReflection.IsModDisabled(modId, mod.modSource))
        {
            MainFile.Logger.Info($"[热重载] {modId} 设置中已禁用，跳过热重载。");
            return;
        }

        if (mod.state == ModLoadState.Disabled || mod.state == ModLoadState.None)
        {
            MainFile.Logger.Info($"[热重载] {modId} 状态={mod.state}，走完整启用（非跳过热重载）。");
            ModLifecycleCoordinator.EnableOrRefresh(mod, modId, force ? "reload(force)" : "reload");
            return;
        }

        if (mod.state != ModLoadState.Loaded && mod.state != ModLoadState.Failed)
        {
            MainFile.Logger.Info($"[热重载] {modId} 状态={mod.state}，跳过。");
            return;
        }

        if (!force && LastReloadAttemptUtc.TryGetValue(modId, out DateTime lastUtc))
        {
            TimeSpan since = DateTime.UtcNow - lastUtc;
            if (since < MinReloadInterval)
            {
                MainFile.Logger.Info($"[热重载] {modId} 触发过密（{since.TotalMilliseconds:F0}ms），本次合并跳过。");
                return;
            }
        }
        LastReloadAttemptUtc[modId] = DateTime.UtcNow;

        kind = RefineChangeKind(mod, modId, kind);

        // 强约束：战斗中 DLL 重载只允许走 SL 管道，禁止即时重载。
        if (kind == ReloadChangeKind.DllOrJson && GameSafetyGuard.IsInCombat)
        {
            if (CombatSlReloadOrchestrator.TrySchedule(mod, kind, force))
                return;

            ModStagingStore.MarkPending(modId, kind);
            MainFile.Logger.Warn($"[热重载] {modId} 战斗中未能进入 SL 管道，已仅写 pending，等待脱战后再应用。");
            return;
        }

        lock (ReloadingModIds)
        {
            if (!ReloadingModIds.Add(modId))
                return;
        }

        bool success = false;
        try
        {
            MainFile.Logger.Info($"[热重载] >>> {mod.manifest?.name ?? modId} ({modId}) kind={kind} root={ModStagingStore.GetEffectiveModRoot(mod)}");

            if (kind == ReloadChangeKind.PckOnly)
            {
                ReloadPckOnly(mod, modId);
                success = true;
            }
            else
            {
                FullReload(mod, modId);
                success = mod.state == ModLoadState.Loaded;
            }

            if (success)
            {
                NotifyReloadSucceeded(modId);
                ModStagingStore.ClearPending(modId);
                if (string.Equals(modId, "Rien", StringComparison.OrdinalIgnoreCase))
                    RienRuntimeCriticalFixes.Install(MainFile.Logger, mod.assembly);
            }
            else
                NotifyReloadFailed(modId, kind);

            if (success && kind != ReloadChangeKind.PckOnly && GameSafetyGuard.IsInCombat)
                CombatReloadInterop.AfterModReloadInCombat(mod);

            if (success && string.Equals(modId, "BaseLib", StringComparison.OrdinalIgnoreCase) && !_reloadAllInProgress)
            {
                MainFile.Logger.Info("[热重载] BaseLib 更新 → reloadall");
                ReloadAllLoadedMods();
            }
            else if (success && !_reloadAllInProgress && mod.state == ModLoadState.Loaded)
            {
                ModDependencyCascade.ReloadDependents(modId, force);
            }

            MainFile.Logger.Info(success
                ? $"[热重载] <<< 成功 {modId}"
                : $"[热重载] <<< 失败 {modId} state={mod.state}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[热重载] {modId} 异常（其它 mod 不受影响）: {ex}");
            NotifyReloadFailed(modId, kind);
        }
        finally
        {
            lock (ReloadingModIds)
                ReloadingModIds.Remove(modId);
        }
    }

    /// <summary>文件变更时：先同步到外置暂存，再触发重载逻辑。</summary>
    internal static void OnLiveFileChanged(string modFolder, string triggerPath, ReloadChangeKind kind)
    {
        if (!IsAutomaticHotReloadEnabled
            || !ModHotReloadSettings.Current.FileWatchEnabled
            || IsAutomaticReloadPaused
            || ModSwitchCleanup.IsModeSwitchInProgress)
            return;

        Mod? mod = FindModByFolder(modFolder);
        if (mod?.manifest?.id == null)
        {
            LogUnknownModFolder(modFolder);
            return;
        }

        string modId = mod.manifest.id;
        if (mod.state == ModLoadState.Disabled
            || ModManagerReflection.IsModDisabled(modId, mod.modSource))
        {
            MainFile.Logger.Info($"[热重载] {modId} 已禁用，忽略文件变更。");
            return;
        }

        try
        {
            SyncModFolderToStaging(mod);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] 暂存同步失败: {ex.Message}");
        }

        Reload(mod, kind);
    }

    private static void FullReload(Mod mod, string modId)
    {
        GodotScriptRegistrationInterop.UnregisterPathsForMod(modId, mod.assembly);
        ModelDbCleanup.RemoveModModels(modId, mod.assembly);
        BaseLibInterop.TryUnregisterModContent(modId, mod.assembly);
        HarmonyUnpatchUtil.UnpatchMod(mod);
        OfficialModLoader.UnloadAssemblyContext(modId);
        RefreshManifest(mod);
        ResetModState(mod);
        OfficialModLoader.LoadMod(mod);
        RememberDllTicks(mod, modId);
        if (mod.state == ModLoadState.Loaded)
            ModLifecycleCoordinator.AfterSuccessfulLoad(mod);
    }

    /// <summary>SL 管道在回主菜单后调用；不重复走 Reload 入口锁以外的逻辑。</summary>
    internal static bool ExecuteFullReloadForSl(Mod mod, string modId, ReloadChangeKind kind, bool force)
    {
        lock (ReloadingModIds)
        {
            if (!ReloadingModIds.Add(modId))
                return mod.state == ModLoadState.Loaded;
        }

        bool success = false;
        try
        {
            if (kind == ReloadChangeKind.PckOnly)
            {
                ReloadPckOnly(mod, modId);
                success = true;
            }
            else
            {
                FullReload(mod, modId);
                success = mod.state == ModLoadState.Loaded;
            }

            if (success)
                ModStagingStore.ClearPending(modId);

            if (success && kind != ReloadChangeKind.PckOnly && GameSafetyGuard.IsInCombat)
                CombatReloadInterop.AfterModReloadInCombat(mod);

            return success;
        }
        finally
        {
            lock (ReloadingModIds)
                ReloadingModIds.Remove(modId);
        }
    }

    private static void ReloadPckOnly(Mod mod, string modId)
    {
        string? pckPath = ModStagingStore.ResolvePayloadPath(mod, modId + ".pck", preferLive: true);
        if (pckPath == null || !ModManagerReflection.FileExists(pckPath))
            throw new InvalidOperationException($"找不到 PCK: {modId}.pck");

        GodotResourceInterop.ReloadResourcePack(pckPath, modId);
        Sts2AssetInterop.AfterModPayloadReload(modId, refreshPreload: false);
    }

    private static ReloadChangeKind RefineChangeKind(Mod mod, string modId, ReloadChangeKind kind)
    {
        if (kind == ReloadChangeKind.DllOrJson)
            return ReloadChangeKind.DllOrJson;

        if (kind != ReloadChangeKind.Unknown)
            return kind;

        string? dllPath = ModStagingStore.ResolvePayloadPath(mod, modId + ".dll");
        if (dllPath == null)
            return ReloadChangeKind.PckOnly;

        long ticks = File.GetLastWriteTimeUtc(dllPath).Ticks;
        if (LastDllTicks.TryGetValue(modId, out long prev) && prev == ticks)
            return ReloadChangeKind.PckOnly;

        return ReloadChangeKind.DllOrJson;
    }

    private static void RememberDllTicks(Mod mod, string modId)
    {
        string? dllPath = ModStagingStore.ResolvePayloadPath(mod, modId + ".dll");
        if (dllPath != null && File.Exists(dllPath))
            LastDllTicks[modId] = File.GetLastWriteTimeUtc(dllPath).Ticks;
    }

    private static void RefreshManifest(Mod mod)
    {
        if (string.IsNullOrEmpty(mod.path) || mod.manifest?.id == null)
            return;

        string modId = mod.manifest.id;
        string? stagedJson = ModStagingStore.ResolvePayloadPath(mod, modId + ".json");
        string? jsonPath = stagedJson
            ?? Directory.GetFiles(mod.path, "*.json")
                .FirstOrDefault(f =>
                    string.Equals(Path.GetFileNameWithoutExtension(f), modId, StringComparison.OrdinalIgnoreCase))
              ?? Directory.GetFiles(mod.path, "*.json").FirstOrDefault();

        if (jsonPath == null)
            return;

        MethodInfo? readManifest = typeof(ModManager).GetMethod(
            "ReadModManifest",
            BindingFlags.Static | BindingFlags.NonPublic);

        if (readManifest?.Invoke(null, [jsonPath, mod.modSource]) is Mod refreshed && refreshed.manifest != null)
            mod.manifest = refreshed.manifest;
    }

    private static void ResetModState(Mod mod)
    {
        mod.state = ModLoadState.None;
        mod.assembly = null;
        mod.errors = null;
    }

    internal static Mod? FindModByFolder(string folderName)
    {
        foreach (Mod mod in ModManager.Mods)
        {
            if (mod.manifest?.id != null &&
                string.Equals(mod.manifest.id, folderName, StringComparison.OrdinalIgnoreCase))
                return mod;

            if (!string.IsNullOrEmpty(mod.path))
            {
                string name = Path.GetFileName(mod.path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase))
                    return mod;
            }
        }

        return null;
    }

    private static void LogUnknownModFolder(string folder)
    {
        var loaded = ModManager.Mods
            .Where(m => m.manifest?.id != null)
            .Select(m => m.manifest!.id)
            .ToList();
        MainFile.Logger.Warn(
            $"[热重载] '{folder}' 未在游戏已加载列表中。已加载: {string.Join(", ", loaded)}。新装 mod 需重启游戏。");
    }

    internal static void SyncAllLoadedModsToStaging()
    {
        foreach (Mod mod in ModManager.Mods)
        {
            if (mod.manifest?.id == null || string.IsNullOrEmpty(mod.path))
                continue;
            if (string.Equals(mod.manifest.id, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (mod.state != ModLoadState.Loaded && mod.state != ModLoadState.Failed)
                continue;

            try
            {
                SyncModFolderToStaging(mod);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[热重载] 初始暂存 {mod.manifest.id}: {ex.Message}");
            }
        }
    }

    internal static void RememberDllTimestampForMod(Mod mod, string modId) => RememberDllTicks(mod, modId);

    internal static ReloadChangeKind ClassifyChange(string fullPath)
    {
        string ext = Path.GetExtension(fullPath);
        if (ext.Equals(".pck", StringComparison.OrdinalIgnoreCase))
            return ReloadChangeKind.PckOnly;
        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            return ReloadChangeKind.DllOrJson;
        return ReloadChangeKind.Unknown;
    }
}
