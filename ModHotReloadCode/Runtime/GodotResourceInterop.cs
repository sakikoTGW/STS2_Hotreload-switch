using Godot;

namespace ModHotReload.Runtime;

internal static class GodotResourceInterop
{
    internal static bool ReloadResourcePack(string pckPath, string modId)
    {
        if (!File.Exists(pckPath))
            return false;

        if (!ModFileUtil.WaitForStableFile(pckPath))
            MainFile.Logger.Warn($"[热重载] PCK 未稳定: {pckPath}");

        // 先清 STS2 侧失败标记，再清 Godot 缓存，最后重挂 PCK
        PckVirtualUnmountRegistry.Enable(modId);
        Sts2AssetInterop.PurgeModAssets(modId);
        ClearResourceCache();

        if (!ProjectSettings.LoadResourcePack(pckPath, replaceFiles: true))
            throw new InvalidOperationException($"Godot 加载 PCK 失败: {pckPath}");

        MainFile.Logger.Info($"[热重载] PCK 已挂载/覆盖: {pckPath}（Godot 4 无公开卸载资源包 API，关闭时仅清缓存与失败标记）");
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
