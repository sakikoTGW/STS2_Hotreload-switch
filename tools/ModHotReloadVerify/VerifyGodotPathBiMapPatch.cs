using System.Reflection;
using HarmonyLib;

namespace ModHotReloadVerify;

/// <summary>与 ModHotReload 中补丁相同签名，用于离游戏进程验证 Harmony 兼容性。</summary>
[HarmonyPatch]
internal static class VerifyGodotPathBiMapPatch
{
    private static MethodBase? TargetMethod()
    {
        Type? bridge = Type.GetType("Godot.Bridge.ScriptManagerBridge, GodotSharp");
        Type? biMap = bridge?.GetNestedType("PathScriptTypeBiMap", BindingFlags.NonPublic | BindingFlags.Public);
        return biMap?.GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(string), typeof(Type)],
            null);
    }

    [HarmonyPrefix]
    private static void Prefix(string scriptPath)
    {
    }
}
