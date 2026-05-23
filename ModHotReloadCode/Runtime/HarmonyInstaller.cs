using System.Reflection;
using HarmonyLib;
using ModHotReload.Patches;

namespace ModHotReload.Runtime;

/// <summary>分阶段打补丁：关键路径失败即中止；Godot 等可选补丁失败不拖垮启停/热重载。</summary>
internal static class HarmonyInstaller
{
    private static int _criticalPatchesApplied;

    private static readonly Type[] CriticalPatchTypes =
    [
        typeof(ModManagerTryLoadModPatch),
        typeof(NModMenuRowOnTickboxToggledPatch),
        typeof(NModdingScreenOnModEnabledOrDisabledPatch),
        typeof(NModdingScreenOnNewModDetectedPatch),
        typeof(NGameOnNewModDetectedPatch),
        typeof(ModManagerIsRunningModdedPatch),
        typeof(ModManagerGameplayRelevantPatch),
        typeof(ModManagerHasHarmonyPatchesPatch),
    ];

    private static readonly Type[] OptionalPatchTypes =
    [
        typeof(GodotLookupScriptsInAssemblyPatch),
        typeof(GodotPathScriptTypeBiMapAddPatch),
        typeof(ResourceLoaderExistsVirtualUnmountPatch),
        typeof(ResourceLoaderHasCachedVirtualUnmountPatch),
        typeof(ResourceLoaderGetCachedRefVirtualUnmountPatch),
        typeof(ResourceLoaderLoadThreadedGetVirtualUnmountPatch),
        typeof(ResourceLoaderLoadThreadedRequestVirtualUnmountPatch),
        typeof(ResourceLoaderGetDependenciesVirtualUnmountPatch),
        typeof(ResourceLoaderListDirectoryVirtualUnmountPatch),
        typeof(ResourceLoaderLoadVirtualUnmountPatch),
        typeof(CombatLifecyclePatch),
        typeof(IntegrationTestForceModAgreementPatch),
    ];

    internal static void ApplyCritical(Harmony harmony)
    {
        if (Interlocked.Exchange(ref _criticalPatchesApplied, 1) != 0)
            return;

        foreach (Type type in CriticalPatchTypes)
            PatchType(harmony, type, required: true);
    }

    internal static void ApplyOptional(Harmony harmony)
    {
        foreach (Type type in OptionalPatchTypes)
            PatchType(harmony, type, required: false);
    }

    internal static void ApplyAllSafe(Harmony harmony)
    {
        ApplyCritical(harmony);
        ApplyOptional(harmony);
    }

    private static void PatchType(Harmony harmony, Type type, bool required)
    {
        try
        {
            new PatchClassProcessor(harmony, type).Patch();
        }
        catch (Exception ex)
        {
            string msg = $"[热重载] 补丁 {type.Name} 失败: {ex.Message}";
            if (required)
                throw new InvalidOperationException(msg, ex);
            MainFile.Logger.Warn(msg);
        }
    }
}
