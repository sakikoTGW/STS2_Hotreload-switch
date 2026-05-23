using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using ModHotReload.Runtime;

namespace ModHotReload.Patches;

[HarmonyPatch(typeof(ModManager), nameof(ModManager.PlayerAgreedToModLoading), MethodType.Getter)]
internal static class IntegrationTestForceModAgreementPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result)
    {
        if (!IntegrationTestMode.IsRequested && !RienCombatVerifyMode.IsRequested)
            return true;

        __result = true;
        return false;
    }
}
