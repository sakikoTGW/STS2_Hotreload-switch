using System.Collections;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;

namespace ModHotReload.Runtime;

/// <summary>游戏进程内集成测试：模拟战斗排队、ModelDb/卡牌注册、实机重载。</summary>
public partial class IntegrationTestRunner : Node
{
    private const int WarmupFrames = 300;
    private const int TimeoutFrames = 3600;
    private int _frames;
    private bool _ran;

    public override void _Process(double delta)
    {
        if (_ran)
            return;

        _frames++;

        if (_frames >= WarmupFrames && IsReady())
        {
            FinishTests();
            return;
        }

        if (_frames < TimeoutFrames)
            return;

        _ran = true;
        Record("ready", false, DescribeModStates());
        try { WriteResults(); }
        finally
        {
            IntegrationTestMode.ClearFlag();
            QuitIfRequested();
        }
    }

    private static bool IsReady()
    {
        bool modLoaded = ModManager.Mods.Any(m =>
            string.Equals(m.manifest?.id, MainFile.ModId, StringComparison.OrdinalIgnoreCase)
            && m.state == ModLoadState.Loaded);

        return modLoaded && ModHotReloadWatcherExists();
    }

    private static string DescribeModStates()
    {
        var lines = ModManager.Mods
            .Where(m => m.manifest?.id != null)
            .Select(m => $"{m.manifest!.id}={m.state}");
        return "超时: " + string.Join(", ", lines);
    }

    private void FinishTests()
    {
        _ran = true;
        try
        {
            RunAll();
        }
        catch (Exception ex)
        {
            Record("runner_crash", false, ex.ToString());
        }
        finally
        {
            WriteResults();
            IntegrationTestMode.ClearFlag();
            QuitIfRequested();
        }
    }

    private static void QuitIfRequested()
    {
        bool liveMode = string.Equals(
            System.Environment.GetEnvironmentVariable("STS2_MODHOTRELOAD_ITEST_LIVE"),
            "1",
            StringComparison.Ordinal);

        if (!IntegrationTestMode.QuitWhenDone || liveMode)
        {
            if (liveMode)
                MainFile.Logger.Info("[ITEST] LIVE: run 'itest' in combat for live_hand_cards.");
            return;
        }

        if (Engine.GetMainLoop() is SceneTree tree)
        {
            MainFile.Logger.Info("[ITEST] done, quitting.");
            tree.Quit();
        }
    }

    private static bool ModHotReloadWatcherExists()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            return false;

        foreach (Node child in tree.Root.GetChildren())
        {
            if (child is ModHotReloadWatcher)
                return true;
        }

        return false;
    }

    private static readonly List<ScenarioResult> Results = [];

    private static void RunAll()
    {
        Results.Clear();
        ModStagingStore.ClearAllPending();

        Record("watcher_ready", ModHotReloadWatcherExists(), "Watcher 已挂载");

        Mod? target = FindContentMod();
        if (target == null)
        {
            Record("target_mod", false, "无已加载内容 mod（除 ModHotReload 外）。请在模组界面启用 Rien/BaseLib 等。");
            return;
        }

        Record("target_mod", true, target.manifest!.id);
        TestCombatDllReload(target);
        TestCombatPropertyOverride(target);
        TestReloadSmoke(target);
        TestCardRegistrationStable(target);
        TestNoDuplicateAfterDoubleReload(target);
        TestModelDbCleanup(target);
        TestLiveCombatHandCards(target);
    }

    private static void TestCombatDllReload(Mod mod)
    {
        string modId = mod.manifest!.id;

        // 默认：战斗内走 SL 管道（不写入 staging pending，不原地 FullReload）
        CombatReloadSettings.SetDeferOverrideForTests(false);
        ModStagingStore.ClearAllPending();
        GameSafetyGuard.TestOverrideInCombat = true;
        try
        {
            HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: false);
            bool pending = ModStagingStore.LoadPending().Any(p =>
                p.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase));
            bool loaded = mod.state == ModLoadState.Loaded;
            Record("combat_sl_path", !pending && loaded,
                $"staging_pending={pending} still_loaded={loaded} (SL 队列由 CombatSlReloadOrchestrator 处理)");
        }
        finally
        {
            GameSafetyGuard.TestOverrideInCombat = null;
        }

        // Defer 模式：战斗中排队，战后 flush
        CombatReloadSettings.SetDeferOverrideForTests(true);
        ModStagingStore.ClearAllPending();
        GameSafetyGuard.TestOverrideInCombat = true;
        try
        {
            HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: false);
            bool queued = ModStagingStore.LoadPending().Any(p =>
                p.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase));
            Record("combat_defer_queue", queued, queued ? "pending 已写入" : "defer 模式未排队");
        }
        finally
        {
            GameSafetyGuard.TestOverrideInCombat = null;
            CombatReloadSettings.SetDeferOverrideForTests(null);
        }

        GameSafetyGuard.OnCombatEndedFlush();
        bool cleared = !ModStagingStore.LoadPending().Any(p =>
            p.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase));
        bool loadedAfterFlush = mod.state == ModLoadState.Loaded;
        Record("combat_defer_flush", cleared && loadedAfterFlush,
            $"pending清除={cleared} loaded={loadedAfterFlush}");
    }

    private static void TestCombatPropertyOverride(Mod mod)
    {
        CombatManager? cm = CombatManager.Instance;
        if (cm == null)
        {
            Record("combat_property", null, "CombatManager.Instance 为空（主菜单），跳过");
            return;
        }

        PropertyInfo? prop = typeof(CombatManager).GetProperty("IsInProgress");
        if (prop?.SetMethod == null)
        {
            Record("combat_property", null, "无法设置 IsInProgress");
            return;
        }

        string modId = mod.manifest!.id;
        ModStagingStore.ClearAllPending();
        CombatReloadSettings.SetDeferOverrideForTests(false);
        try
        {
            prop.SetValue(cm, true);
            HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: false);
            bool queued = ModStagingStore.LoadPending().Any(p =>
                p.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase));
            prop.SetValue(cm, false);
            bool ok = !queued && mod.state == ModLoadState.Loaded;
            Record("combat_property", ok, $"真实 IsInProgress 立即重载 queued={queued} loaded={mod.state == ModLoadState.Loaded}");
        }
        catch (Exception ex)
        {
            Record("combat_property", false, ex.Message);
        }
        finally
        {
            CombatReloadSettings.SetDeferOverrideForTests(null);
        }
    }

    private static void TestModelDbCleanup(Mod mod)
    {
        try
        {
            Assembly? asm = mod.assembly;
            Type? type = FindRegisteredType(asm);
            if (type == null)
            {
                Record("modeldb_cleanup", null, "no registered model type in assembly");
                return;
            }

            bool had = ModelDb.Contains(type);
            int removed = ModelDbCleanup.RemoveAssemblyModels(asm);
            bool gone = !ModelDb.Contains(type);
            Record("modeldb_cleanup", had && removed > 0 && gone, $"had={had} removed={removed} gone={gone}");

            HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: true);
        }
        catch (Exception ex)
        {
            Record("modeldb_cleanup", false, ex.Message);
        }
    }

    private static void TestReloadSmoke(Mod mod)
    {
        HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: true);
        Record("reload_smoke", mod.state == ModLoadState.Loaded, $"state={mod.state}");
    }

    private static void TestCardRegistrationStable(Mod mod)
    {
        Type? type = FindRegisteredType(mod.assembly);
        if (type == null)
        {
            Record("card_model_id", null, "无已注册模型类型");
            return;
        }

        ModelId idBefore = ModelDb.GetId(type);
        HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: true);

        Type? typeAfter = FindRegisteredType(mod.assembly);
        if (typeAfter == null)
        {
            Record("card_model_id", false, "重载后未找到注册类型");
            return;
        }

        ModelId idAfter = ModelDb.GetId(typeAfter);
        int dup = CountContentById(idAfter);
        bool ok = idBefore.Equals(idAfter) && dup <= 1 && !ModelDb.Contains(type);
        Record("card_model_id", ok, $"id稳定={idBefore.Equals(idAfter)} dup={dup} oldType残留={ModelDb.Contains(type)}");
    }

    private static void TestNoDuplicateAfterDoubleReload(Mod mod)
    {
        Type? type = FindRegisteredType(mod.assembly);
        if (type == null)
        {
            Record("no_duplicate", null, "无注册类型");
            return;
        }

        HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: true);
        ModelId id = ModelDb.GetId(FindRegisteredType(mod.assembly)!);
        HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: true);
        int dup = CountContentById(id);
        Record("no_duplicate", dup <= 1, $"ModelId 条目数={dup}");
    }

    private static void TestLiveCombatHandCards(Mod mod)
    {
        CombatManager? cm = CombatManager.Instance;
        if (cm?.IsInProgress != true)
        {
            Record("live_hand_cards", null, "未在真实战斗中（跳过；可用 -LiveCombat 人工进战后重跑）");
            return;
        }

        object? handInfo = TrySampleHandCardType(cm);
        if (handInfo == null)
        {
            Record("live_hand_cards", null, "无法读取手牌类型");
            return;
        }

        var (cardType, label) = ((Type, string))handInfo;
        bool fromMod = cardType.Assembly == mod.assembly;
        if (!fromMod)
        {
            Record("live_hand_cards", null, $"手牌 {label} 非目标 mod 类型，跳过");
            return;
        }

        HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: true);
        bool stillInDb = ModelDb.Contains(FindRegisteredType(mod.assembly)!);
        Record("live_hand_cards", stillInDb, $"战斗中重载后 ModelDb 仍注册={stillInDb}（实例仍可能为旧 CLR 类型）");
    }

    private static (Type type, string label)? TrySampleHandCardType(CombatManager cm)
    {
        foreach (PropertyInfo prop in typeof(CombatManager).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!prop.Name.Contains("Hand", StringComparison.OrdinalIgnoreCase)
                && !prop.Name.Contains("Card", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                object? val = prop.GetValue(cm);
                if (val == null)
                    continue;

                Type? t = ExtractCardType(val);
                if (t != null)
                    return (t, prop.Name);
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    private static Type? ExtractCardType(object collection)
    {
        if (collection is IEnumerable en)
        {
            foreach (object? item in en)
            {
                if (item == null)
                    continue;
                Type t = item.GetType();
                if (t.Name.Contains("Card", StringComparison.OrdinalIgnoreCase))
                    return t;
            }
        }

        return null;
    }

    private static int CountContentById(ModelId id)
    {
        FieldInfo? field = typeof(ModelDb).GetField("_contentById", BindingFlags.Static | BindingFlags.NonPublic);
        if (field?.GetValue(null) is not IDictionary dict)
            return 0;

        int count = 0;
        foreach (DictionaryEntry entry in dict)
        {
            if (entry.Key is ModelId mid && mid.Equals(id))
                count++;
        }

        return count;
    }

    private static Type? FindRegisteredType(Assembly? asm)
    {
        if (asm == null)
            return null;

        foreach (Type t in SafeTypes(asm))
        {
            if (!ModelDbTypeFilter.IsLikelyModelType(t))
                continue;

            try
            {
                _ = ModelDb.GetId(t);
                return t;
            }
            catch
            {
                // 未注册进 ModelDb
            }
        }

        return null;
    }

    private static IEnumerable<Type> SafeTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(x => x != null)!;
        }
    }

    private static Mod? FindContentMod()
    {
        Mod? rien = ModManager.Mods.FirstOrDefault(m =>
            m.state == ModLoadState.Loaded
            && string.Equals(m.manifest?.id, "Rien", StringComparison.OrdinalIgnoreCase));
        if (rien != null)
            return rien;

        return ModManager.Mods.FirstOrDefault(m =>
            m.state == ModLoadState.Loaded
            && m.manifest?.id != null
            && !string.Equals(m.manifest.id, MainFile.ModId, StringComparison.OrdinalIgnoreCase)
            && m.assembly != null);
    }

    private static void Record(string name, bool? pass, string detail)
    {
        string status = pass switch
        {
            true => "pass",
            false => "fail",
            _ => "skip"
        };

        Results.Add(new ScenarioResult(name, status, detail));
        MainFile.Logger.Info($"[ITEST] {status.ToUpperInvariant()} {name}: {detail}");
    }

    private static void WriteResults()
    {
        int passed = Results.Count(r => r.Status == "pass");
        int failed = Results.Count(r => r.Status == "fail");
        int skipped = Results.Count(r => r.Status == "skip");

        var report = new IntegrationTestReport(
            MainFile.Version,
            DateTime.UtcNow,
            passed,
            failed,
            skipped,
            Results.ToArray());

        Directory.CreateDirectory(Path.GetDirectoryName(IntegrationTestMode.ResultsFile)!);
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(IntegrationTestMode.ResultsFile, json);
        MainFile.Logger.Info($"[ITEST] 报告: {IntegrationTestMode.ResultsFile} pass={passed} fail={failed} skip={skipped}");
    }

    private sealed record ScenarioResult(string Name, string Status, string Detail);

    private sealed record IntegrationTestReport(
        string Version,
        DateTime FinishedUtc,
        int Passed,
        int Failed,
        int Skipped,
        ScenarioResult[] Scenarios);

    /// <summary>控制台 itest 命令：立即跑一轮并写报告。</summary>
    internal static void RunManual()
    {
        RunAll();
        WriteResults();
    }
}
