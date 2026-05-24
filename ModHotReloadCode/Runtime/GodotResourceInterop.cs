using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload.Runtime;

internal static class GodotResourceInterop
{
    /// <summary>
    /// 清空 ResourceLoader 缓存后，按依赖顺序重挂所有已加载 mod 的 PCK。
    /// 只重挂单个 mod 会导致其它 mod 的 res://images/... 等路径失效（godot.log: char_select_*.png）。
    /// </summary>
    internal static void RemountAllLoadedPcks()
    {
        List<string> order = ModDependencyCascade.GetReloadOrder();
        if (order.Count == 0)
            return;

        ClearResourceCache();

        int mounted = 0;
        foreach (string modId in order)
        {
            Mod? mod = ModManager.Mods.FirstOrDefault(m =>
                string.Equals(m.manifest?.id, modId, StringComparison.OrdinalIgnoreCase));
            if (mod == null || mod.state != ModLoadState.Loaded)
                continue;

            string? pckPath = ModStagingStore.ResolvePayloadPath(mod, modId + ".pck", preferLive: true);
            if (pckPath == null && mod.manifest?.hasPck != true)
                continue;
            if (pckPath == null || !File.Exists(pckPath))
                continue;

            PckVirtualUnmountRegistry.Enable(modId);
            if (!ProjectSettings.LoadResourcePack(pckPath, replaceFiles: true))
            {
                MainFile.Logger.Warn($"[热重载] PCK 重挂失败: {pckPath}");
                continue;
            }

            mounted++;
        }

        if (mounted > 0)
        {
            MainFile.Logger.Info(
                $"[热重载] 已重挂 {mounted} 个 PCK（依赖顺序），避免清缓存后其它 mod 资源丢失。");
        }
    }

    internal static bool ReloadResourcePack(string pckPath, string modId)
    {
        if (!File.Exists(pckPath))
            return false;

        if (!ModFileUtil.WaitForStableFile(pckPath))
            MainFile.Logger.Warn($"[热重载] PCK 未稳定: {pckPath}");

        PckVirtualUnmountRegistry.Enable(modId);
        Sts2AssetInterop.PurgeModAssets(modId);
        RemountAllLoadedPcks();

        MainFile.Logger.Info(
            $"[热重载] PCK 已挂载/覆盖: {pckPath}（含全部已加载 mod 重挂，Godot 4 无公开卸载 API）");
        return true;
    }

    internal static void VirtualUnmountResourcePack(string modId) =>
        PckVirtualUnmountRegistry.Disable(modId);

    internal static void ClearResourceCache()
    {
        try
        {
            var method = typeof(ResourceLoader).GetMethod(
                "ClearCache",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                ?? typeof(ResourceLoader).GetMethod(
                    "ClearCache",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            method?.Invoke(null, null);
            MainFile.Logger.Info("[热重载] ResourceLoader 缓存已清空");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] 清空 ResourceLoader 缓存失败: {ex.Message}");
        }
    }
}
