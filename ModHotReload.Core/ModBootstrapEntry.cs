using HarmonyLib;

namespace ModHotReload.Core;

public static class ModBootstrapEntry
{
    private static int _installed;

    public static void Install(string? modsRoot = null, Action<string>? logInfo = null, Action<string>? logWarn = null)
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        ModCollectibleHost.SetLoggers(logInfo, logWarn);
        ModDllPathRegistry.Initialize(modsRoot);
        ModCollectibleHost.RegisterDefaultAlcQuarantine();

        try
        {
            var harmony = new Harmony("ModHotReload.Core.redirect");
            harmony.PatchAll(typeof(AssemblyLoadFromRedirectPatch).Assembly);
            logInfo?.Invoke("[ALC] 已安装 Assembly.LoadFrom / LoadFromAssemblyPath 重定向（mod 不进 Default）");
        }
        catch (Exception ex)
        {
            logWarn?.Invoke($"[ALC] 重定向补丁失败: {ex.Message}");
            Interlocked.Exchange(ref _installed, 0);
        }
    }
}
