using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;
using ModHotReload.Runtime;

namespace ModHotReload.Patches;

/// <summary>热重载时 LookupScriptsInAssembly 的 duplicate key 视为成功。</summary>
[HarmonyPatch]
internal static class GodotLookupScriptsInAssemblyPatch
{
    private static MethodBase? TargetMethod()
    {
        Type? bridge = Type.GetType("Godot.Bridge.ScriptManagerBridge, GodotSharp");
        return bridge?.GetMethod(
            "LookupScriptsInAssembly",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            [typeof(Assembly)],
            null);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        if (!GodotScriptRegistrationInterop.InHotReloadScope)
            return __exception;

        if (GodotScriptRegistrationInterop.IsDuplicateScriptPath(__exception))
            return null;

        return __exception;
    }
}

/// <summary>热重载 scope 内 Add(path,type) 前先释放同路径，使 BaseLib Initialize 可重复执行。</summary>
[HarmonyPatch]
internal static class GodotPathScriptTypeBiMapAddPatch
{
    private static Type? _biMapType;

    private static MethodBase? TargetMethod()
    {
        Type? bridge = Type.GetType("Godot.Bridge.ScriptManagerBridge, GodotSharp");
        _biMapType = bridge?.GetNestedType("PathScriptTypeBiMap", BindingFlags.NonPublic | BindingFlags.Public);
        return _biMapType?.GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(string), typeof(Type)],
            null);
    }

    [HarmonyPrefix]
    private static void Prefix(string scriptPath)
    {
        if (!GodotScriptRegistrationInterop.InHotReloadScope)
            return;

        GodotScriptRegistrationInterop.EnsurePathFreeBeforeAdd(scriptPath);
    }
}
