using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using ModHotReload.Runtime;

namespace ModHotReload.Patches;

[HarmonyPatch(typeof(ModManager), nameof(ModManager.IsRunningModded))]
internal static class ModManagerIsRunningModdedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result)
    {
        __result = RuntimeModModeCoordinator.IsRunningModded();
        return false;
    }
}

[HarmonyPatch(typeof(ModManager), nameof(ModManager.GetGameplayRelevantModNameList))]
internal static class ModManagerGameplayRelevantPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref List<string>? __result)
    {
        __result = RuntimeModModeCoordinator.GetGameplayRelevantModNameList();
        return false;
    }
}

[HarmonyPatch(typeof(ModManager), nameof(ModManager.HasHarmonyPatches))]
internal static class ModManagerHasHarmonyPatchesPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result)
    {
        __result = RuntimeModModeCoordinator.IsRunningModded();
        return false;
    }
}
