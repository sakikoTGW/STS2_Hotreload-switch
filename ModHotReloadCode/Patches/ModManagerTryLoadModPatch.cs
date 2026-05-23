using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using ModHotReload.Reflection;
using ModHotReload.Runtime;

namespace ModHotReload.Patches;

/// <summary>
/// 拦截官方 TryLoadMod，改走 OfficialModLoader（PCK 先于 DLL + collectible ALC + 资源缓存清理）。
/// 已经在本补丁安装前被官方加载进 Default 的 mod 无法事后硬卸载，只能从下一次被我们加载开始隔离。
/// </summary>
[HarmonyPatch(typeof(ModManager), "TryLoadMod")]
internal static class ModManagerTryLoadModPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Mod mod)
    {
        string? modId = mod.manifest?.id;
        if (modId == null || string.Equals(modId, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (RuntimeModModeCoordinator.IsVanillaMode)
        {
            mod.state = ModLoadState.Disabled;
            mod.assembly = null;
            mod.errors = null;
            return false;
        }

        if (!HotReloadCoordinator.IsReloading(modId)
            && ModManagerReflection.IsModDisabled(modId, mod.modSource))
        {
            mod.state = ModLoadState.Disabled;
            mod.assembly = null;
            mod.errors = null;
            return false;
        }

        if (mod.state == ModLoadState.Disabled)
        {
            if (!ModManagerReflection.IsModDisabled(modId, mod.modSource))
            {
                ModLifecycleCoordinator.EnableOrRefresh(mod, modId, "TryLoadMod");
                return false;
            }

            return true;
        }

        if (!HotReloadCoordinator.IsReloading(modId) && mod.state == ModLoadState.Loaded)
        {
            if (mod.assembly != null && ModAssemblyLoader.IsCollectibleAlc(mod.assembly))
                return true;

            if (mod.assembly != null && ModAssemblyLoader.IsDefaultAlc(mod.assembly) && mod.manifest?.hasDll == true)
            {
                MainFile.Logger.Info($"[热重载] {modId} 首次加载进了 Default ALC，运行期迁到 collectible…");
                HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: true);
                return false;
            }

            return true;
        }

        if (mod.state == ModLoadState.Loaded || mod.state == ModLoadState.Failed)
            mod.state = ModLoadState.None;

        OfficialModLoader.LoadMod(mod);
        return false;
    }
}
