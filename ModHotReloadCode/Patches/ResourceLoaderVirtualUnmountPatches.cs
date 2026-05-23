using System.Reflection;
using Godot;
using HarmonyLib;
using ModHotReload.Runtime;

namespace ModHotReload.Patches;

[HarmonyPatch(typeof(ResourceLoader), nameof(ResourceLoader.Exists), [typeof(string), typeof(string)])]
internal static class ResourceLoaderExistsVirtualUnmountPatch
{
    [HarmonyPrefix]
    private static bool Prefix(string path, ref bool __result)
    {
        if (!PckVirtualUnmountRegistry.IsBlocked(path))
            return true;

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ResourceLoader), nameof(ResourceLoader.HasCached), [typeof(string)])]
internal static class ResourceLoaderHasCachedVirtualUnmountPatch
{
    [HarmonyPrefix]
    private static bool Prefix(string path, ref bool __result)
    {
        if (!PckVirtualUnmountRegistry.IsBlocked(path))
            return true;

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ResourceLoader), nameof(ResourceLoader.GetCachedRef), [typeof(string)])]
internal static class ResourceLoaderGetCachedRefVirtualUnmountPatch
{
    [HarmonyPrefix]
    private static bool Prefix(string path, ref Resource? __result)
    {
        if (!PckVirtualUnmountRegistry.IsBlocked(path))
            return true;

        __result = null;
        return false;
    }
}

[HarmonyPatch(typeof(ResourceLoader), nameof(ResourceLoader.LoadThreadedGet), [typeof(string)])]
internal static class ResourceLoaderLoadThreadedGetVirtualUnmountPatch
{
    [HarmonyPrefix]
    private static bool Prefix(string path, ref Resource? __result)
    {
        if (!PckVirtualUnmountRegistry.IsBlocked(path))
            return true;

        __result = null;
        return false;
    }
}

[HarmonyPatch(typeof(ResourceLoader), nameof(ResourceLoader.LoadThreadedRequest), [typeof(string), typeof(string), typeof(bool), typeof(ResourceLoader.CacheMode)])]
internal static class ResourceLoaderLoadThreadedRequestVirtualUnmountPatch
{
    [HarmonyPrefix]
    private static bool Prefix(string path, ref Error __result)
    {
        if (!PckVirtualUnmountRegistry.IsBlocked(path))
            return true;

        __result = Error.FileNotFound;
        return false;
    }
}

[HarmonyPatch(typeof(ResourceLoader), nameof(ResourceLoader.GetDependencies), [typeof(string)])]
internal static class ResourceLoaderGetDependenciesVirtualUnmountPatch
{
    [HarmonyPrefix]
    private static bool Prefix(string path, ref string[] __result)
    {
        if (!PckVirtualUnmountRegistry.IsBlocked(path))
            return true;

        __result = [];
        return false;
    }
}

[HarmonyPatch(typeof(ResourceLoader), nameof(ResourceLoader.ListDirectory), [typeof(string)])]
internal static class ResourceLoaderListDirectoryVirtualUnmountPatch
{
    [HarmonyPrefix]
    private static bool Prefix(string directoryPath, ref string[] __result)
    {
        if (!PckVirtualUnmountRegistry.IsDirectoryBlocked(directoryPath))
            return true;

        __result = [];
        return false;
    }
}

/// <summary>
/// 只 patch 非泛型 <c>Resource Load(...)</c>。泛型 <c>Load&lt;T&gt;</c> 会让 Harmony/MonoMod 抛 NotSupportedException。
/// </summary>
[HarmonyPatch]
internal static class ResourceLoaderLoadVirtualUnmountPatch
{
    private static MethodBase? TargetMethod()
    {
        foreach (var method in typeof(ResourceLoader).GetMethods(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (method.IsGenericMethod || method.ReturnType != typeof(Resource))
                continue;

            var ps = method.GetParameters();
            if (ps.Length != 3
                || ps[0].ParameterType != typeof(string)
                || ps[1].ParameterType != typeof(string)
                || ps[2].ParameterType != typeof(ResourceLoader.CacheMode))
                continue;

            return method;
        }

        return null;
    }

    [HarmonyPrefix]
    private static bool Prefix(string path, ref Resource __result)
    {
        if (!PckVirtualUnmountRegistry.IsBlocked(path))
            return true;

        __result = null!;
        return false;
    }
}
