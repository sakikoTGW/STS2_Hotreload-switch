namespace ModHotReloadVerify;

internal static class HotReloadAuditReport
{
    internal static void Print()
    {
        Console.WriteLine();
        Console.WriteLine("=== 热重载能力审计（静态） ===");
        PrintRow("文件监视 .dll/.pck/.json", true, "ModHotReloadWatcher + 2.4s 去抖");
        PrintRow("控制台 reload / reloadall", true, "ReloadModConsoleCmd");
        PrintRow("模组 UI 勾选启停", true, "ModLifecycleCoordinator + NativeModUiBridge");
        PrintRow("关 mod 清 ModelDb/PCK/Harmony", true, "DisableMod + RemoveModModels");
        PrintRow("collectible ALC 重载", true, "ModCollectibleHost + OfficialModLoader");
        PrintRow("Godot 脚本 duplicate key", true, "PathScriptTypeBiMap scriptPath 补丁");
        PrintRow("依赖级联重载", true, "ModDependencyCascade");
        PrintRow("外置暂存 staging", true, "ModStagingStore");
        PrintRow("startupHooks 照常启动", true, "GameRuntimeConfigInstaller + StartupHook");
        PrintRow("战斗中 DLL", true, "仅 SL 管道：保存→主菜单→重载→继续（非战斗内原地换 DLL）");
        PrintRow("战斗中 defer 模式", true, "STS2_MODHOTRELOAD_COMBAT_DEFER=1 时排队至战后");
        PrintRow("热重载 ModHotReload 自身", false, "设计禁止");
        PrintRow("已在 Default ALC 的 mod", false, "需重启游戏；见 DefaultAlcMigration");
        PrintRow("无 runtimeconfig 的首次启动", false, "首启写入配置，需重启一次后 hook 生效");
        PrintRow("PCK 单独热更", true, "ReloadChangeKind.PckOnly");
        Console.WriteLine();
        Console.WriteLine("结论：菜单/主界面热更已实现；战斗内是 SL 式重载，不是「边打边换逻辑」。");
        Console.WriteLine("进程内验证：%LOCALAPPDATA%\\STS2_ModHotReload\\run-itest.flag 后启动游戏，或 scripts\\run-integration-test.ps1");
    }

    private static void PrintRow(string feature, bool ok, string note)
    {
        string mark = ok ? "OK " : "LIM";
        Console.WriteLine($"{mark}  {feature,-28} {note}");
    }
}
