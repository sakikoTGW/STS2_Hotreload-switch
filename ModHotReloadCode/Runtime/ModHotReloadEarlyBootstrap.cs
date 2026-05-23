using HarmonyLib;
using ModHotReload.Core;
using ModHotReload.Patches;

namespace ModHotReload.Runtime;

/// <summary>
/// ModHotReload 程序集一进入 CLR 即：collectible 加载重定向 + TryLoadMod 补丁。
/// 由 <see cref="ModModuleEntry"/> 在卫星程序集加载后调用。
/// </summary>
internal static class ModHotReloadEarlyBootstrap
{
    private static int _installed;

    internal static void InstallTryLoadModPatch()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        ModBootstrapEntry.Install(
            logInfo: EarlyLog.Info,
            logWarn: EarlyLog.Warn);

        try
        {
            var harmony = new Harmony($"{MainFile.ModId}.early");
            HarmonyInstaller.ApplyCritical(harmony);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] 早期关键补丁失败（将依赖 Initialize）: {ex.Message}");
            Interlocked.Exchange(ref _installed, 0);
        }
    }
}
