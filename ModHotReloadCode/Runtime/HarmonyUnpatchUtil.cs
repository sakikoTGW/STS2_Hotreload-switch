using System.Collections.ObjectModel;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload.Runtime;

/// <summary>
/// 模组实际常用 new Harmony(ModId)，官方 fallback 用 author.modId；须全部卸掉。
/// </summary>
internal static class HarmonyUnpatchUtil
{
    internal static void UnpatchMod(Mod mod)
    {
        Assembly? oldAssembly = mod.assembly;
        string modId = mod.manifest?.id ?? "";
        string author = mod.manifest?.author ?? "unknown";

        var harmonyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            modId,
            $"{author}.{modId}",
            $"{modId}.patches"
        };

        if (oldAssembly != null)
        {
            foreach (string owner in CollectOwnersForAssembly(oldAssembly))
                harmonyIds.Add(owner);
        }

        foreach (string id in harmonyIds)
        {
            try
            {
                new Harmony(id).UnpatchAll(id);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[热重载] UnpatchAll({id}): {ex.Message}");
            }
        }

        MainFile.Logger.Info($"[热重载] Harmony 已清理 mod={modId}，id 数={harmonyIds.Count}");
    }

    private static IEnumerable<string> CollectOwnersForAssembly(Assembly assembly)
    {
        var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToArray())
        {
            global::HarmonyLib.Patches? patches = Harmony.GetPatchInfo(original);
            if (patches == null || !PatchUsesAssembly(patches, assembly))
                continue;

            foreach (string owner in patches.Owners)
                owners.Add(owner);
        }

        return owners;
    }

    private static bool PatchUsesAssembly(global::HarmonyLib.Patches patches, Assembly assembly)
    {
        foreach (Patch? patch in AllPatches(patches))
        {
            if (patch?.PatchMethod?.DeclaringType?.Assembly == assembly)
                return true;
        }

        return false;
    }

    private static IEnumerable<Patch?> AllPatches(global::HarmonyLib.Patches patches)
    {
        foreach (ReadOnlyCollection<Patch> group in new[]
                 {
                     patches.Prefixes, patches.Postfixes, patches.Transpilers, patches.Finalizers,
                     patches.InnerPrefixes, patches.InnerPostfixes
                 })
        {
            foreach (Patch patch in group)
                yield return patch;
        }
    }
}
