using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using ModHotReload.Patches;

namespace ModHotReload.Runtime;

/// <summary>集成测试启动最早阶段：在 ModHotReload 程序集加载时即打补丁（便于后续 mod 通过 PlayerAgreed 检查）。</summary>
internal static class IntegrationTestBootstrap
{
    private static bool _bootstrapped;

    internal static void RunIfRequested()
    {
        if ((!IntegrationTestMode.IsRequested && !RienCombatVerifyMode.IsRequested) || _bootstrapped)
            return;

        _bootstrapped = true;
        var harmony = new Harmony($"{MainFile.ModId}.itest.bootstrap");
        harmony.PatchAll(typeof(IntegrationTestForceModAgreementPatch).Assembly);
    }
}
