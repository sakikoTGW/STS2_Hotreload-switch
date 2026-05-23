using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;

namespace ModHotReload.Runtime;

/// <summary>自动开 Rien 局 + Debug 进弱怪战，收集战斗内视觉/逻辑证据。</summary>
public partial class RienCombatVerifyRunner : Node
{
    private const int WarmupFrames = 420;
    private const int PostCombatSettleFrames = 420;
    private const int TimeoutFrames = 9000;

    private enum Phase
    {
        WaitingReady,
        BootstrappingRun,
        WaitingCombat,
        SettlingVisuals,
        Verifying,
        Done
    }

    private Phase _phase = Phase.WaitingReady;
    private int _frames;
    private int _combatSettleFrames;
    private bool _bootstrapStarted;
    private string? _bootstrapError;
    private readonly List<ScenarioResult> _results = [];

    public override void _Process(double delta)
    {
        _frames++;

        switch (_phase)
        {
            case Phase.WaitingReady:
                if (_frames >= WarmupFrames && IsReady())
                    BeginBootstrap();
                else if (_frames >= TimeoutFrames)
                    FailEarly("超时：游戏/mod 未就绪");
                break;

            case Phase.WaitingCombat:
                if (CombatManager.Instance?.IsInProgress == true)
                {
                    _phase = Phase.SettlingVisuals;
                    _combatSettleFrames = 0;
                    MainFile.Logger.Info("[RCV] 已进入战斗，等待演出稳定…");
                }
                else if (_frames >= TimeoutFrames)
                    FailEarly("超时：未进入战斗（EnterRoomDebug 可能失败）");
                break;

            case Phase.SettlingVisuals:
                _combatSettleFrames++;
                if (_combatSettleFrames >= PostCombatSettleFrames)
                {
                    _phase = Phase.Verifying;
                    _ = RunVerifyAsync();
                }
                break;

            case Phase.Done:
                break;
        }
    }

    private void BeginBootstrap()
    {
        if (_bootstrapStarted)
            return;

        _bootstrapStarted = true;
        _phase = Phase.BootstrappingRun;
        Callable.From(BootstrapAsync).CallDeferred();
    }

    private async void BootstrapAsync()
    {
        try
        {
            IntegrationTestBootstrap.RunIfRequested();
            await Task.Delay(2000);
            MainFile.Logger.Info("[RCV] StartNewSingleplayerRun (Rien)…");
            await Sts2CombatDebugInterop.StartRienSingleplayerRunAsync();
            await Task.Delay(4000);
            MainFile.Logger.Info("[RCV] EnterRoomDebug TOADPOLES_WEAK…");
            await Sts2CombatDebugInterop.EnterWeakMonsterCombatAsync();
            _phase = Phase.WaitingCombat;
            MainFile.Logger.Info("[RCV] 等待 CombatManager.IsInProgress…");
        }
        catch (Exception ex)
        {
            Exception root = ex;
            while (root.InnerException != null)
                root = root.InnerException;

            _bootstrapError = root.ToString();
            MainFile.Logger.Error("[RCV] 引导异常: " + root);
            FailEarly("引导失败: " + root.Message);
        }
    }

    private async Task RunVerifyAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(_bootstrapError))
                Record("bootstrap", false, _bootstrapError);

            TryCaptureScreenshot();
            RienCombatVerifier.RunAll(_results);
            await ProbeWeaponStrikesAsync().ConfigureAwait(false);
            await ProbePresentationVfxAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Record("runner_crash", false, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            WriteResults();
            RienCombatVerifyMode.ClearFlag();
            QuitIfRequested();
            _phase = Phase.Done;
        }
    }

    private async Task ProbePresentationVfxAsync()
    {
        Player? player = RienCombatPlayerResolver.Resolve();
        Type? tracker = AccessTools.TypeByName("Rien.RienCode.Combat.RienCombatTracker");
        MethodInfo? getState = tracker?.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
        if (player == null || getState == null)
        {
            Record("vfx_probe_ready", false, "无 Rien 玩家/状态");
            return;
        }

        object? state = getState.Invoke(null, [player]);
        if (state == null)
        {
            Record("vfx_probe_ready", false, "RienCombatState 为空");
            return;
        }

        var tcs = new TaskCompletionSource();
        Callable.From(() =>
        {
            try
            {
                RienCombatVfxProbe.RunAllOnMainThread(player, state, _results);
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }).CallDeferred();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(8000)).ConfigureAwait(false);
        if (completed != tcs.Task)
            Record("vfx_probe_ready", false, "VfxProbe 主线程超时");
        else
            await tcs.Task.ConfigureAwait(false);
    }

    private async Task ProbeWeaponStrikesAsync()
    {
        Player? player = RienCombatPlayerResolver.Resolve();
        Type? tracker = AccessTools.TypeByName("Rien.RienCode.Combat.RienCombatTracker");
        MethodInfo? getState = tracker?.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
        if (player == null || getState == null)
        {
            Record("weapon_strike_probe", false, "无 Rien 玩家");
            return;
        }

        object? state = getState.Invoke(null, [player]);
        var (ok, detail) = await RienCombatVerifier.ProbeWeaponStrikesAsync(player, state).ConfigureAwait(false);
        Record("weapon_strike_probe", ok, detail);
    }

    private static bool IsReady()
    {
        bool modLoaded = ModManager.Mods.Any(m =>
            string.Equals(m.manifest?.id, "Rien", StringComparison.OrdinalIgnoreCase)
            && m.state == ModLoadState.Loaded);

        NGame? game = NGame.Instance;
        return modLoaded
            && game != null
            && game.MainMenu != null
            && Engine.GetMainLoop() is SceneTree { Root: not null };
    }

    private void FailEarly(string message)
    {
        Record("ready", false, message);
        WriteResults();
        RienCombatVerifyMode.ClearFlag();
        QuitIfRequested();
        _phase = Phase.Done;
    }

    private static void TryCaptureScreenshot()
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
                return;

            Viewport vp = tree.Root.GetViewport();
            Image? image = vp.GetTexture()?.GetImage();
            if (image == null)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(RienCombatVerifyMode.ScreenshotFile)!);
            Error err = image.SavePng(RienCombatVerifyMode.ScreenshotFile);
            MainFile.Logger.Info(err == Error.Ok
                ? $"[RCV] 截图: {RienCombatVerifyMode.ScreenshotFile}"
                : $"[RCV] 截图失败: {err}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn("[RCV] 截图异常: " + ex.Message);
        }
    }

    private void Record(string name, bool? pass, string detail)
    {
        string status = pass switch
        {
            true => "pass",
            false => "fail",
            _ => "skip"
        };
        _results.Add(new ScenarioResult(name, status, detail));
        MainFile.Logger.Info($"[RCV] {status.ToUpperInvariant()} {name}: {detail}");
    }

    private void WriteResults()
    {
        int passed = _results.Count(r => r.Status == "pass");
        int failed = _results.Count(r => r.Status == "fail");
        int skipped = _results.Count(r => r.Status == "skip");

        var report = new VerifyReport(
            MainFile.Version,
            DateTime.UtcNow,
            passed,
            failed,
            skipped,
            RienCombatVerifyMode.ScreenshotFile,
            FindLatestRienLog(),
            _results.ToArray());

        Directory.CreateDirectory(Path.GetDirectoryName(RienCombatVerifyMode.ResultsFile)!);
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(RienCombatVerifyMode.ResultsFile, json);
        MainFile.Logger.Info($"[RCV] 报告: {RienCombatVerifyMode.ResultsFile} pass={passed} fail={failed} skip={skipped}");
    }

    private static string? FindLatestRienLog()
    {
        try
        {
            string dir = ProjectSettings.GlobalizePath("user://Rien/logs");
            if (!Directory.Exists(dir))
                return null;

            return Directory.GetFiles(dir, "rien-*.log")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()
                ?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static void QuitIfRequested()
    {
        if (!RienCombatVerifyMode.QuitWhenDone)
            return;

        if (Engine.GetMainLoop() is SceneTree tree)
        {
            MainFile.Logger.Info("[RCV] 完成，退出游戏。");
            tree.Quit();
        }
    }

    internal sealed record ScenarioResult(string Name, string Status, string Detail);

    private sealed record VerifyReport(
        string Version,
        DateTime FinishedUtc,
        int Passed,
        int Failed,
        int Skipped,
        string? ScreenshotPath,
        string? RienLogPath,
        ScenarioResult[] Scenarios);

    // weapon probe moved to public for runner
}
