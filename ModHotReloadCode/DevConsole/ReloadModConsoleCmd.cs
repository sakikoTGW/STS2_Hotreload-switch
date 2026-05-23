using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using ModHotReload.Runtime;

namespace ModHotReload.DevConsole;

internal sealed class ReloadModConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "reload";

    public override string Args => "modId";

    public override string Description => "热重载指定 mod（例: reload Rien）";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return new CmdResult(false, "用法: reload <ModId>");

        string modId = args[0];
        Mod? mod = HotReloadCoordinator.FindModByFolder(modId);
        if (mod == null)
            return new CmdResult(false, $"未找到已加载 mod: {modId}");

        HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: true);
        return new CmdResult(true, $"已触发 {modId} 热重载，请看日志 [热重载] <<<");
    }
}

internal sealed class ReloadAllConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "reloadall";

    public override string Args => "";

    public override string Description => "热重载所有已加载 mod";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player issuingPlayer, string[] args)
    {
        HotReloadCoordinator.ReloadAllLoadedMods();
        return new CmdResult(true, "已触发 reloadall");
    }
}

internal sealed class ModModeConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "modmode";

    public override string Args => "on|off|toggle|status";

    public override string Description => "游戏内切换 Mod/无 Mod 模式，并使用对应存档";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player issuingPlayer, string[] args)
    {
        string action = args.Length == 0 ? "status" : args[0].Trim().ToLowerInvariant();
        switch (action)
        {
            case "status":
                return new CmdResult(true, RuntimeModModeCoordinator.Status);
            case "on":
            case "modded":
                _ = RuntimeModModeCoordinator.SwitchAsync(RuntimeModMode.Modded, continueAfterSwitch: false);
                return new CmdResult(true, "正在切换到 Mod 模式；若当前在 Run 中会先保存并回主菜单。");
            case "off":
            case "vanilla":
                _ = RuntimeModModeCoordinator.SwitchAsync(RuntimeModMode.Vanilla, continueAfterSwitch: false);
                return new CmdResult(true, "正在切换到无 Mod 模式；若当前在 Run 中会先保存并回主菜单。");
            case "toggle":
                RuntimeModMode target = RuntimeModModeCoordinator.IsVanillaMode
                    ? RuntimeModMode.Modded
                    : RuntimeModMode.Vanilla;
                _ = RuntimeModModeCoordinator.SwitchAsync(target, continueAfterSwitch: false);
                return new CmdResult(true, $"正在切换到 {target} 模式。");
            default:
                return new CmdResult(false, "用法: modmode on|off|toggle|status");
        }
    }
}

internal sealed class ModOnConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "modon";

    public override string Args => "modId";

    public override string Description => "游戏内开启指定 mod";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return new CmdResult(false, "用法: modon <ModId>");

        Mod? mod = HotReloadCoordinator.FindModByFolder(args[0]);
        if (mod == null)
            return new CmdResult(false, $"未找到 mod: {args[0]}");

        ModLifecycleCoordinator.ApplyEnabledState(mod, enabled: true, persistSettings: true, reason: "modon");
        return new CmdResult(mod.state == ModLoadState.Loaded, $"modon {args[0]} -> {mod.state}");
    }
}

internal sealed class ModOffConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "modoff";

    public override string Args => "modId";

    public override string Description => "游戏内关闭指定 mod";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return new CmdResult(false, "用法: modoff <ModId>");

        Mod? mod = HotReloadCoordinator.FindModByFolder(args[0]);
        if (mod == null)
            return new CmdResult(false, $"未找到 mod: {args[0]}");

        ModLifecycleCoordinator.ApplyEnabledState(mod, enabled: false, persistSettings: true, reason: "modoff");
        return new CmdResult(true, $"modoff {args[0]} -> {mod.state}");
    }
}
