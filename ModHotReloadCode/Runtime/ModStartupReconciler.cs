using MegaCrit.Sts2.Core.Modding;
using ModHotReload.Reflection;

namespace ModHotReload.Runtime;

/// <summary>
/// ModHotReload 主 DLL 晚于其它 mod 加载时，清理「设置已关但仍 Loaded」的残留。
/// </summary>
internal static class ModStartupReconciler
{
    internal static void ReconcileDisabledButLoadedMods()
    {
        foreach (Mod mod in ModManager.Mods)
        {
            string? modId = mod.manifest?.id;
            if (string.IsNullOrEmpty(modId)
                || string.Equals(modId, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!ModManagerReflection.IsModDisabled(modId, mod.modSource))
                continue;

            if (mod.state != ModLoadState.Loaded && mod.assembly == null)
                continue;

            MainFile.Logger.Warn(
                $"[热重载] {modId} 在设置中已禁用但仍已加载（ModHotReload 加载顺序靠后），正在卸载残留…");
            ModLifecycleCoordinator.ApplyEnabledState(
                mod, enabled: false, persistSettings: false, reason: "startup-reconcile");
        }
    }
}
