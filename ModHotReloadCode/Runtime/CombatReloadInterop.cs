using System.Reflection;
using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload.Runtime;

/// <summary>战斗内热重载后，尝试调用内容 mod 提供的静态清理/重同步入口。</summary>
internal static class CombatReloadInterop
{
    private static readonly string[] HookMethodNames =
    [
        "ClearAllForHotReload",
        "OnHotReloadInCombat",
        "OnModHotReloadInCombat",
    ];

    internal static void AfterModReloadInCombat(Mod mod)
    {
        Assembly? assembly = mod.assembly;
        if (assembly == null)
            return;

        int calls = 0;
        foreach (Type type in SafeGetTypes(assembly))
        {
            foreach (string methodName in HookMethodNames)
            {
                MethodInfo? method = type.GetMethod(
                    methodName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (method == null || !method.IsStatic || method.GetParameters().Length != 0)
                    continue;

                try
                {
                    method.Invoke(null, null);
                    calls++;
                    MainFile.Logger.Info($"[热重载] 战斗内重载后调用 {type.Name}.{methodName}");
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Warn($"[热重载] {type.Name}.{methodName}: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
        }

        if (calls == 0)
            MainFile.Logger.Info($"[热重载] {mod.manifest?.id} 战斗内重载完成（无 ClearAllForHotReload 钩子；手牌实例可能仍为旧 CLR 类型）。");
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
    }
}
