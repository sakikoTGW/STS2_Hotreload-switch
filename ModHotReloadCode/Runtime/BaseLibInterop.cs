using System.Reflection;
using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload.Runtime;

/// <summary>热重载前尝试清理 BaseLib ModelDb / 注册表（反射，兼容多版本）。</summary>
internal static class BaseLibInterop
{
    internal static void TryUnregisterModContent(string modId, Assembly? modAssembly)
    {
        Assembly? baseLib = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "BaseLib", StringComparison.OrdinalIgnoreCase));

        if (baseLib == null)
            return;

        int calls = 0;
        calls += TryInvokeOnTypes(baseLib, modId, modAssembly, "ModelDb");
        calls += TryInvokeOnTypes(baseLib, modId, modAssembly, "Unregister");
        calls += TryInvokeOnTypes(baseLib, modId, modAssembly, "RemoveMod");

        MainFile.Logger.Info(calls > 0
            ? $"[热重载] BaseLib 清理调用 {calls} 次 (mod={modId})"
            : $"[热重载] BaseLib 无匹配清理 API（mod={modId}），依赖重新 Initialize");
    }

    /// <summary>运行期加载 mod 后，让 BaseLib 重新注册主菜单「模组配置」子界面（避免 No such submenu NModConfigSubmenu）。</summary>
    internal static void TryRefreshMainMenuInjection()
    {
        Assembly? baseLib = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "BaseLib", StringComparison.OrdinalIgnoreCase));

        if (baseLib == null)
            return;

        int calls = 0;
        foreach (Type type in SafeGetTypes(baseLib).Where(t =>
                     t.FullName?.Contains("InjectMainMenuModConfig", StringComparison.OrdinalIgnoreCase) == true))
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (method.GetParameters().Length != 0)
                    continue;

                if (!method.Name.Contains("Inject", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    method.Invoke(null, null);
                    calls++;
                    MainFile.Logger.Info($"[热重载] BaseLib 主菜单刷新: {type.Name}.{method.Name}");
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Warn($"[热重载] BaseLib.{type.Name}.{method.Name}: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
        }

        if (calls == 0)
            MainFile.Logger.Warn("[热重载] 未找到 BaseLib InjectMainMenuModConfig；主菜单「模组配置」可能需重启游戏。");
    }

    internal static void TryRegisterConsoleCommands()
    {
        Assembly? baseLib = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "BaseLib", StringComparison.OrdinalIgnoreCase));

        if (baseLib == null)
            return;

        foreach (Type type in SafeGetTypes(baseLib))
        {
            foreach (MethodInfo register in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!register.Name.Contains("Register", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryRegisterReloadCommands(register, type))
                    return;
            }
        }
    }

    private static bool TryRegisterReloadCommands(MethodInfo register, Type hostType)
    {
        ParameterInfo[] ps = register.GetParameters();
        if (ps.Length < 2 || ps[0].ParameterType != typeof(string))
            return false;

        try
        {
            if (ps[1].ParameterType == typeof(Action<string[]>))
            {
                register.Invoke(null, ["reload", (Action<string[]>)OnReloadCommand]);
                register.Invoke(null, ["reloadall", (Action<string[]>)(_ => HotReloadCoordinator.ReloadAllLoadedMods())]);
                register.Invoke(null, ["itest", (Action<string[]>)(_ => IntegrationTestRunnerManual.Run())]);
                register.Invoke(null, ["modmode", (Action<string[]>)OnModModeCommand]);
                register.Invoke(null, ["modon", (Action<string[]>)OnModOnCommand]);
                register.Invoke(null, ["modoff", (Action<string[]>)OnModOffCommand]);
            }
            else if (ps[1].ParameterType == typeof(Action<string>))
            {
                register.Invoke(null, ["reload", (Action<string>)(s => OnReloadCommand(new[] { s }))]);
                register.Invoke(null, ["reloadall", (Action<string>)(_ => HotReloadCoordinator.ReloadAllLoadedMods())]);
                register.Invoke(null, ["itest", (Action<string>)(_ => IntegrationTestRunnerManual.Run())]);
                register.Invoke(null, ["modmode", (Action<string>)(s => OnModModeCommand(SplitArgs(s)))]);
                register.Invoke(null, ["modon", (Action<string>)(s => OnModOnCommand(SplitArgs(s)))]);
                register.Invoke(null, ["modoff", (Action<string>)(s => OnModOffCommand(SplitArgs(s)))]);
            }
            else
            {
                return false;
            }

            MainFile.Logger.Info($"[热重载] 控制台: reload / reloadall / itest / modmode / modon / modoff ({hostType.Name}.{register.Name})");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void OnReloadCommand(string[] args)
    {
        if (args.Length == 0)
        {
            MainFile.Logger.Info("[热重载] reload <ModId> | reloadall");
            return;
        }

        Mod? mod = HotReloadCoordinator.FindModByFolder(args[0]);
        if (mod == null)
            return;

        if (GameSafetyGuard.IsDllReloadUnsafe)
            MainFile.Logger.Warn("[热重载] 战斗中 force reload：旧战斗实例仍可能引用旧程序集，建议等战斗结束。");

        HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: true);
    }

    private static void OnModModeCommand(string[] args)
    {
        string action = args.Length == 0 ? "status" : args[0].Trim().ToLowerInvariant();
        switch (action)
        {
            case "status":
                MainFile.Logger.Info($"[热重载] modmode: {RuntimeModModeCoordinator.Status}");
                break;
            case "on":
            case "modded":
                _ = RuntimeModModeCoordinator.SwitchAsync(RuntimeModMode.Modded, continueAfterSwitch: false);
                break;
            case "off":
            case "vanilla":
                _ = RuntimeModModeCoordinator.SwitchAsync(RuntimeModMode.Vanilla, continueAfterSwitch: false);
                break;
            case "toggle":
                _ = RuntimeModModeCoordinator.SwitchAsync(
                    RuntimeModModeCoordinator.IsVanillaMode ? RuntimeModMode.Modded : RuntimeModMode.Vanilla,
                    continueAfterSwitch: false);
                break;
            default:
                MainFile.Logger.Info("[热重载] modmode on|off|toggle|status");
                break;
        }
    }

    private static void OnModOnCommand(string[] args)
    {
        if (args.Length == 0)
            return;

        Mod? mod = HotReloadCoordinator.FindModByFolder(args[0]);
        if (mod != null)
            ModLifecycleCoordinator.ApplyEnabledState(mod, enabled: true, persistSettings: true, reason: "modon");
    }

    private static void OnModOffCommand(string[] args)
    {
        if (args.Length == 0)
            return;

        Mod? mod = HotReloadCoordinator.FindModByFolder(args[0]);
        if (mod != null)
            ModLifecycleCoordinator.ApplyEnabledState(mod, enabled: false, persistSettings: true, reason: "modoff");
    }

    private static string[] SplitArgs(string s) =>
        string.IsNullOrWhiteSpace(s)
            ? []
            : s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int TryInvokeOnTypes(Assembly baseLib, string modId, Assembly? modAssembly, string typeNameHint)
    {
        int count = 0;
        foreach (Type type in SafeGetTypes(baseLib))
        {
            if (!type.FullName?.Contains(typeNameHint, StringComparison.OrdinalIgnoreCase) ?? true)
                continue;

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (!IsCleanupMethod(method.Name))
                    continue;

                if (TryInvoke(method, modId, modAssembly))
                    count++;
            }
        }

        return count;
    }

    private static bool IsCleanupMethod(string name) =>
        name.Contains("Unregister", StringComparison.OrdinalIgnoreCase)
        || name.Contains("RemoveMod", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Deregister", StringComparison.OrdinalIgnoreCase)
        || (name.Contains("Remove", StringComparison.OrdinalIgnoreCase) && name.Contains("Model", StringComparison.OrdinalIgnoreCase))
        || (name.Contains("Clear", StringComparison.OrdinalIgnoreCase) && name.Contains("Mod", StringComparison.OrdinalIgnoreCase));

    private static bool TryInvoke(MethodInfo method, string modId, Assembly? modAssembly)
    {
        if (!method.IsStatic)
            return false;

        try
        {
            ParameterInfo[] ps = method.GetParameters();
            object?[]? args = BuildArgs(ps, modId, modAssembly);
            if (args == null)
                return false;

            method.Invoke(null, args);
            MainFile.Logger.Info($"[热重载] BaseLib.{method.DeclaringType?.Name}.{method.Name}");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] {method.DeclaringType?.Name}.{method.Name}: {ex.InnerException?.Message ?? ex.Message}");
            return false;
        }
    }

    private static object?[]? BuildArgs(ParameterInfo[] ps, string modId, Assembly? modAssembly)
    {
        var args = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            Type pt = ps[i].ParameterType;
            if (pt == typeof(string))
                args[i] = modId;
            else if (pt == typeof(Assembly))
                args[i] = modAssembly!;
            else if (pt == typeof(Mod))
                args[i] = ModManager.Mods.FirstOrDefault(m => m.manifest?.id == modId)!;
            else
                return null;
        }

        return args;
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
