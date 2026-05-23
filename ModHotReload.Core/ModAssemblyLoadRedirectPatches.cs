using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

namespace ModHotReload.Core;

/// <summary>拦截 Default 上的 mod DLL 加载，改走 collectible（从根源避免进 Default）。</summary>
[HarmonyPatch]
public static class AssemblyLoadFromRedirectPatch
{
    [HarmonyPatch(typeof(Assembly), nameof(Assembly.LoadFrom), [typeof(string)])]
    [HarmonyPrefix]
    public static bool Prefix(string assemblyFile, ref Assembly __result)
    {
        if (!ModDllPathRegistry.TryGetModId(assemblyFile, out string modId))
            return true;

        string? dir = Path.GetDirectoryName(assemblyFile);
        __result = ModCollectibleHost.GetOrLoad(modId, assemblyFile, dir ?? "");
        return false;
    }
}

[HarmonyPatch]
public static class AlcLoadFromAssemblyPathRedirectPatch
{
    [HarmonyPatch(typeof(AssemblyLoadContext), nameof(AssemblyLoadContext.LoadFromAssemblyPath), [typeof(string)])]
    [HarmonyPrefix]
    public static bool Prefix(AssemblyLoadContext __instance, string assemblyPath, ref Assembly __result)
    {
        if (__instance != AssemblyLoadContext.Default)
            return true;

        if (!ModDllPathRegistry.TryGetModId(assemblyPath, out string modId))
            return true;

        string? dir = Path.GetDirectoryName(assemblyPath);
        __result = ModCollectibleHost.GetOrLoad(modId, assemblyPath, dir ?? "");
        return false;
    }
}
