using MegaCrit.Sts2.Core.Modding;
using ModHotReload.Reflection;

namespace ModHotReload.Runtime;

/// <summary>
/// 模组启用/禁用/再启用的唯一状态机，避免 UI、TryLoadMod、热重载三条路径行为不一致。
/// </summary>
internal static class ModLifecycleCoordinator
{
    internal static void ApplyEnabledState(
        Mod mod,
        bool enabled,
        bool persistSettings,
        string reason,
        bool refreshIfAlreadyLoaded = true)
    {
        if (string.Equals(mod.manifest?.id, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
            return;

        string? modId = mod.manifest?.id;
        if (string.IsNullOrEmpty(modId))
            return;

        if (enabled && RuntimeModModeCoordinator.IsVanillaMode)
        {
            MainFile.Logger.Warn(
                $"[热重载] 当前为无 Mod 模式，无法启用 {modId}。请先执行 modmode on。");
            return;
        }

        bool settingsWasDisabled = ModManagerReflection.IsModDisabled(modId, mod.modSource);

        if (persistSettings)
            ModManagerReflection.SetModEnabled(mod, enabled);

        if (enabled)
            ApplyEnable(mod, modId, settingsWasDisabled, reason, refreshIfAlreadyLoaded);
        else
            ApplyDisable(mod, modId, reason);
    }

    /// <summary>reload / TryLoadMod 对 Disabled|None 模组：完整走启用流程。</summary>
    internal static void EnableOrRefresh(Mod mod, string modId, string reason) =>
        ApplyEnabledState(mod, enabled: true, persistSettings: false, reason);

    internal static void PreparePayloadFromDisk(Mod mod, string modId)
    {
        PrepareForLoad(modId);
        try
        {
            HotReloadCoordinator.SyncModToStaging(mod);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] {modId} 同步磁盘到暂存失败: {ex.Message}");
        }
    }

    private static void ApplyEnable(
        Mod mod,
        string modId,
        bool settingsWasDisabled,
        string reason,
        bool refreshIfAlreadyLoaded)
    {
        bool needsFullLoad = mod.state != ModLoadState.Loaded
            || settingsWasDisabled
            || mod.assembly == null;

        if (!needsFullLoad)
        {
            if (!refreshIfAlreadyLoaded)
                return;

            MainFile.Logger.Info($"[热重载] {modId} 已 Loaded，{reason} 触发磁盘刷新…");
            HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: true);
            BaseLibInterop.TryRefreshMainMenuInjection();
            Sts2UiRefreshInterop.ScheduleAfterModListChanged();
            return;
        }

        if (mod.state == ModLoadState.Loaded)
        {
            MainFile.Logger.Info($"[热重载] {modId} 再启用前先卸载旧实例（{reason}）");
            RuntimeModModeCoordinator.DisableMod(mod, persistSettings: false);
        }

        PrepareForLoad(modId);
        RuntimeModModeCoordinator.EnableMod(mod, persistSettings: false);
        BaseLibInterop.TryRefreshMainMenuInjection();
        Sts2UiRefreshInterop.ScheduleAfterModListChanged();
    }

    private static void ApplyDisable(Mod mod, string modId, string reason)
    {
        if (mod.state == ModLoadState.Disabled && mod.assembly == null)
            return;

        if (mod.state == ModLoadState.Loaded || mod.state == ModLoadState.Failed || mod.assembly != null)
        {
            RuntimeModModeCoordinator.DisableMod(mod, persistSettings: false);
            MainFile.Logger.Info($"[热重载] {modId} 已关闭（{reason}）");
            return;
        }

        mod.state = ModLoadState.Disabled;
        mod.assembly = null;
        mod.errors = null;
        PckVirtualUnmountRegistry.Disable(modId);
        ModStagingStore.ClearPending(modId);
        HotReloadCoordinator.ClearReloadThrottle(modId);
    }

    internal static void PrepareForLoad(string modId)
    {
        PckVirtualUnmountRegistry.Enable(modId);
        HotReloadCoordinator.ClearReloadThrottle(modId);
        ModStagingStore.ClearPending(modId);

        Mod? mod = HotReloadCoordinator.FindModByFolder(modId);
        if (mod != null)
        {
            try
            {
                HotReloadCoordinator.SyncModToStaging(mod);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[热重载] {modId} 启用前暂存同步失败: {ex.Message}");
            }
        }
    }

    internal static void AfterSuccessfulLoad(Mod mod)
    {
        string? modId = mod.manifest?.id;
        if (string.IsNullOrEmpty(modId))
            return;

        HotReloadCoordinator.RememberDllTimestamp(mod);
        PckVirtualUnmountRegistry.Enable(modId);
    }
}
