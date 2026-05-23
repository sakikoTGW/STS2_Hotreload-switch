using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using ModHotReload.Runtime;

namespace ModHotReload.Patches;

/// <summary>尽早挂上 CombatEnded；战斗结束时刷新排队 DLL（全 mod 通用）。</summary>
[HarmonyPatch(typeof(CombatManager))]
internal static class CombatLifecyclePatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(CombatManager.SetUpCombat))]
    private static void AfterSetUpCombat(CombatManager __instance)
    {
        GameSafetyGuard.AttachTo(__instance);
        if (RienCombatVerifyMode.IsRequested)
            _ = RienCombatBootstrapInterop.WarmUpAfterCombatEntryAsync();
        else
            ScheduleRienVisualScan();
    }

    private static void ScheduleRienVisualScan()
    {
        try
        {
            AccessTools.TypeByName("Rien.RienCode.Presentation.RienPlayerVisualBootstrap")
                ?.GetMethod("ScheduleScanAfterCombatLoad")
                ?.Invoke(null, null);
        }
        catch
        {
            // ignore
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch("EndCombatInternal")]
    private static void AfterEndCombat()
    {
        GameSafetyGuard.OnCombatEndedFlush();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CombatManager.Instance), MethodType.Getter)]
    private static void AfterInstanceGetter(CombatManager __result)
    {
        if (__result != null)
            GameSafetyGuard.AttachTo(__result);
    }
}
