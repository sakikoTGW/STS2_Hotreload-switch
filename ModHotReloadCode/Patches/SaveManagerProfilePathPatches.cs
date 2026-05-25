using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;
using ModHotReload.Runtime;

namespace ModHotReload.Patches;

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.GetProfileScopedPath))]
internal static class SaveManagerGetProfileScopedPathPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref string __result)
    {
        if (!RuntimeModModeCoordinator.IsVanillaMode)
            return;

        __result = SaveProfileInterop.NormalizeScopedPathForMode(__result, vanilla: true);
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SwitchProfileId))]
internal static class SaveManagerSwitchProfileIdPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        // 原生 SwitchProfileId 会按 ModManager.IsRunningModded 选路径；补丁后需与当前模式一致
        MainFile.Logger.Info(
            $"[热重载] SwitchProfileId 完成，模式={RuntimeModModeCoordinator.CurrentMode}");
    }
}
