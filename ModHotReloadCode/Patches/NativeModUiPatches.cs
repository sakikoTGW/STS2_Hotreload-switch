using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using ModHotReload.Reflection;
using ModHotReload.Runtime;

namespace ModHotReload.Patches;

[HarmonyPatch(typeof(NModMenuRow), "OnTickboxToggled")]
internal static class NModMenuRowOnTickboxToggledPatch
{
    [HarmonyPostfix]
    private static void Postfix(NModMenuRow __instance)
    {
        NativeModUiBridge.OnModRowToggled(__instance);
    }
}

[HarmonyPatch(typeof(NModdingScreen), "OnModEnabledOrDisabled")]
internal static class NModdingScreenOnModEnabledOrDisabledPatch
{
    [HarmonyPostfix]
    private static void Postfix(NModdingScreen __instance)
    {
        if (ModManagerReflection.SuppressModDetectedEvent)
            return;

        NativeModUiBridge.OnModSettingsCommitted(__instance);
    }
}

/// <summary>阻止运行期热加载时再插一行模组列表。</summary>
[HarmonyPatch(typeof(NModdingScreen), "OnNewModDetected")]
internal static class NModdingScreenOnNewModDetectedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Mod mod) => !NativeModUiBridge.ShouldSkipNewModDetected(mod);
}

/// <summary>阻止主流程弹出「模组尚未完全加载 / 需重启」对话框。</summary>
[HarmonyPatch(typeof(NGame), "OnNewModDetected")]
internal static class NGameOnNewModDetectedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Mod mod) => !NativeModUiBridge.ShouldSkipNewModDetected(mod);
}
