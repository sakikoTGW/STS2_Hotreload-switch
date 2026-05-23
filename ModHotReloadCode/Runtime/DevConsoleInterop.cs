using System.Reflection;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Nodes.Debug;
using ModHotReload.DevConsole;
using StsDevConsole = MegaCrit.Sts2.Core.DevConsole.DevConsole;

namespace ModHotReload.Runtime;

internal static class DevConsoleInterop
{
    private static bool _registered;

    internal static void TryRegisterConsoleCommands()
    {
        BaseLibInterop.TryRegisterConsoleCommands();
        TryRegisterNativeConsoleCommands();
    }

    internal static bool TryRegisterNativeConsoleCommands()
    {
        if (_registered)
            return true;

        try
        {
            NDevConsole? node = NDevConsole.Instance;
            if (node == null)
                return false;

            FieldInfo? field = typeof(NDevConsole).GetField(
                "_devConsole",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(node) is not StsDevConsole devConsole)
                return false;

            RegisterCommand(devConsole, new ReloadModConsoleCmd());
            RegisterCommand(devConsole, new ReloadAllConsoleCmd());
            RegisterCommand(devConsole, new ModModeConsoleCmd());
            RegisterCommand(devConsole, new ModOnConsoleCmd());
            RegisterCommand(devConsole, new ModOffConsoleCmd());
            _registered = true;
            MainFile.Logger.Info("[热重载] 官方 DevConsole: reload / reloadall / modmode / modon / modoff 已注册");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] DevConsole 注册失败: {ex.Message}");
            return false;
        }
    }

    private static void RegisterCommand(StsDevConsole devConsole, AbstractConsoleCmd cmd)
    {
        MethodInfo? method = typeof(StsDevConsole).GetMethod(
            "RegisterCommand",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
            throw new MissingMethodException(nameof(StsDevConsole), "RegisterCommand");
        method.Invoke(devConsole, [cmd]);
    }
}
