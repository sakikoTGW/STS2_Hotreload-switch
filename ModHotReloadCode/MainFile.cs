using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using ModHotReload.Patches;
using ModHotReload.Reflection;
using ModHotReload.Runtime;

namespace ModHotReload;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "ModHotReload";
    public const string Version = "1.6.9";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, LogType.Generic);

    private static Harmony? _harmony;
    private static ModHotReloadWatcher? _watcher;
    private static int _startAttempts;
    private static int _consoleRegisterAttempts;

    public static void Initialize()
    {
        ModSatelliteAssemblyLoader.EnsureLoaded();
        if (StartupHookPaths.ResolveHookDllPath() is { } hookDll)
            InvokeCoreRuntimeConfigInstaller(hookDll);

        InvokeCoreBootstrapEntry();

        _harmony = new Harmony($"{ModId}.patches");
        try
        {
            HarmonyInstaller.ApplyAllSafe(_harmony);
        }
        catch (Exception ex)
        {
            Logger.Error($"[热重载] 关键 Harmony 补丁失败，启停/热重载可能不可用: {ex.Message}");
        }

        ModStartupReconciler.ReconcileDisabledButLoadedMods();
        ModManagerReflection.EnsureModHotReloadFirstInSettings();
        DefaultAlcMigration.ScheduleAfterSceneReady();
        TryStartWatcher();
        DevConsoleInterop.TryRegisterConsoleCommands();
        TryRegisterConsoleDeferred();
        TryStartIntegrationTests();
        TryStartRienCombatVerify();
        RienRuntimeCriticalFixes.Install(Logger);

        Logger.Info($"Mod Hot Reload v{Version} 已启动。");
        if (IntegrationTestMode.IsRequested)
            Logger.Info("[ITEST] enabled -> %LOCALAPPDATA%/STS2_ModHotReload/itest-results.json");
        if (RienCombatVerifyMode.IsRequested)
            Logger.Info("[RCV] enabled -> %LOCALAPPDATA%/STS2_ModHotReload/rien-combat-verify-results.json");
        Logger.Info("通用：mods 下任意已加载 mod；ModelDb 全量清理；依赖级联；外置暂存。");
        Logger.Info("战斗中：PCK/图即时；DLL 默认 SL 模式（保存→主菜单→重载→继续）。");
        Logger.Info("可选：STS2_MODHOTRELOAD_COMBAT_DEFER=1 战后排队；combat-sl-off.flag 关闭 SL。");
        Logger.Info("模组界面：勾选即时启停；控制台 reload/reloadall/hotreload/modon/modoff。");
        Logger.Info($"[热重载] 配置: {ModHotReloadSettings.ConfigPath}");
        Logger.Info($"[热重载] {ModHotReloadSettings.Current.Describe()}");
        Logger.Info("ALC：mod DLL 仅 collectible；首次安装见 Install.bat + config.example.json");
    }

    private static void TryStartWatcher()
    {
        if (_watcher != null)
            return;

        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            Logger.Warn("SceneTree 不可用，1 秒后重试启动监视。");
            return;
        }

        void OnFrame()
        {
            if (_watcher != null)
            {
                tree.ProcessFrame -= OnFrame;
                return;
            }

            if (tree.Root == null)
            {
                if (++_startAttempts > 600)
                    Logger.Error("[热重载] 无法挂载 Watcher（Root 为空）。");
                return;
            }

            _watcher = new ModHotReloadWatcher();
            tree.Root.AddChild(_watcher);
            tree.ProcessFrame -= OnFrame;
            Logger.Info("[热重载] Watcher 已挂载到场景树。");
        }

        tree.ProcessFrame += OnFrame;
    }

    private static void TryRegisterConsoleDeferred()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        void OnConsoleFrame()
        {
            if (DevConsoleInterop.TryRegisterNativeConsoleCommands())
            {
                tree.ProcessFrame -= OnConsoleFrame;
                return;
            }

            if (++_consoleRegisterAttempts > 600)
                tree.ProcessFrame -= OnConsoleFrame;
        }

        tree.ProcessFrame += OnConsoleFrame;
    }

    private static void TryStartIntegrationTests()
    {
        if (!IntegrationTestMode.IsRequested)
            return;

        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        void OnTestFrame()
        {
            if (tree.Root == null)
                return;

            tree.ProcessFrame -= OnTestFrame;
            var runner = new IntegrationTestRunner();
            tree.Root.AddChild(runner);
            Logger.Info("[ITEST] IntegrationTestRunner 已挂载。");
        }

        tree.ProcessFrame += OnTestFrame;
    }

    private static void TryStartRienCombatVerify()
    {
        if (!RienCombatVerifyMode.IsRequested)
            return;

        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        void OnVerifyFrame()
        {
            if (tree.Root == null)
                return;

            tree.ProcessFrame -= OnVerifyFrame;
            var runner = new RienCombatVerifyRunner();
            tree.Root.AddChild(runner);
            Logger.Info("[RCV] RienCombatVerifyRunner 已挂载。");
        }

        tree.ProcessFrame += OnVerifyFrame;
    }

    private static void InvokeCoreRuntimeConfigInstaller(string hookDll)
    {
        Type? installer = Type.GetType("ModHotReload.Core.GameRuntimeConfigInstaller, ModHotReload.Core", throwOnError: false);
        installer?.GetMethod("EnsureStartupHookInstalled", [
            typeof(string),
            typeof(Action<string>),
            typeof(Action<string>)
        ])?.Invoke(null, [hookDll, (Action<string>)(msg => Logger.Info(msg)), (Action<string>)(msg => Logger.Warn(msg))]);
    }

    private static void InvokeCoreBootstrapEntry()
    {
        Type? bootstrap = Type.GetType("ModHotReload.Core.ModBootstrapEntry, ModHotReload.Core", throwOnError: false);
        bootstrap?.GetMethod("Install")?.Invoke(null, [
            null,
            (Action<string>)(msg => Logger.Info(msg)),
            (Action<string>)(msg => Logger.Warn(msg))
        ]);
    }
}
