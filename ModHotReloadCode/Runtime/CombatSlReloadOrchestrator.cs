using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload.Runtime;

/// <summary>
/// 战斗内 DLL 热重载：保存并退出 → 重载程序集 → 继续存档（约等于 SL 之间热更）。
/// 默认开启；设 STS2_MODHOTRELOAD_COMBAT_SL=0 或创建 combat-sl-off.flag 可关闭。
/// </summary>
internal static class CombatSlReloadOrchestrator
{
    private static int _pipelineRunning;
    private static readonly Queue<SlReloadRequest> Queue = new();
    private static readonly HashSet<string> QueuedModIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RerunRequested = new(StringComparer.OrdinalIgnoreCase);
    private static string? _currentModId;

    private readonly record struct SlReloadRequest(Mod Mod, ReloadChangeKind Kind, bool Force);

    internal static bool TrySchedule(Mod mod, ReloadChangeKind kind, bool force)
    {
        if (!CombatReloadSettings.UseSaveLoadReloadInCombat)
        {
            string? blockedId = mod.manifest?.id;
            if (!string.IsNullOrEmpty(blockedId))
                MainFile.Logger.Warn($"[热重载] {blockedId} 战斗中禁止非 SL 重载；请开启 SL（移除 combat-sl-off.flag）。");
            return false;
        }

        if (!GameSafetyGuard.IsInCombat)
            return false;

        if (kind != ReloadChangeKind.DllOrJson)
            return false;

        string? modId = mod.manifest?.id;
        if (string.IsNullOrEmpty(modId))
            return false;

        lock (Queue)
        {
            bool alreadyQueued = QueuedModIds.Contains(modId);
            bool runningCurrent = string.Equals(_currentModId, modId, StringComparison.OrdinalIgnoreCase);
            if (alreadyQueued || runningCurrent)
            {
                RerunRequested.Add(modId);
                MainFile.Logger.Info($"[热重载] {modId} 已在 SL 队列/执行中，合并为一次追加重载。");
                return true;
            }

            Queue.Enqueue(new SlReloadRequest(mod, kind, force));
            QueuedModIds.Add(modId);
        }
        MainFile.Logger.Info($"[热重载] {modId} 战斗内 → SL 模式（保存→主菜单→重载→继续）。");

        if (Interlocked.CompareExchange(ref _pipelineRunning, 1, 0) == 0)
            _ = RunPipelineLoopAsync();

        return true;
    }

    private static async Task RunPipelineLoopAsync()
    {
        try
        {
            while (true)
            {
                SlReloadRequest request;
                lock (Queue)
                {
                    if (!Queue.TryDequeue(out request))
                        break;
                    _currentModId = request.Mod.manifest?.id;
                    if (_currentModId != null)
                        QueuedModIds.Remove(_currentModId);
                }

                await RunSingleAsync(request);

                string? finishedModId = _currentModId;
                _currentModId = null;
                if (!string.IsNullOrEmpty(finishedModId))
                {
                    bool shouldRerun;
                    lock (Queue)
                    {
                        shouldRerun = RerunRequested.Remove(finishedModId) && !QueuedModIds.Contains(finishedModId);
                        if (shouldRerun)
                        {
                            Queue.Enqueue(new SlReloadRequest(request.Mod, request.Kind, request.Force));
                            QueuedModIds.Add(finishedModId);
                            MainFile.Logger.Info($"[热重载] {finishedModId} 合并追加 SL 重载已入队。");
                        }
                    }
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _pipelineRunning, 0);
            if (Queue.Count > 0 && Interlocked.CompareExchange(ref _pipelineRunning, 1, 0) == 0)
                _ = RunPipelineLoopAsync();
        }
    }

    private static async Task RunSingleAsync(SlReloadRequest request)
    {
        string? modId = request.Mod.manifest?.id;
        if (string.IsNullOrEmpty(modId))
            return;

        try
        {
            MainFile.Logger.Info($"[热重载] SL >>> {modId}：保存当前 Run…");
            await Sts2RunInterop.SaveCurrentRunAsync(saveProgress: true);
            await AwaitFrames(2);

            if (!Sts2RunInterop.HasRunSave())
                throw new InvalidOperationException("SaveRun 后仍无 run 存档。");

            MainFile.Logger.Info($"[热重载] SL {modId}：返回主菜单…");
            await Sts2RunInterop.ReturnToMainMenuAsync();
            await AwaitFrames(3);

            MainFile.Logger.Info($"[热重载] SL {modId}：重载 DLL…");
            bool ok = HotReloadCoordinator.ExecuteFullReloadForSl(request.Mod, modId, request.Kind, request.Force);
            if (!ok)
                throw new InvalidOperationException($"FullReload 失败 state={request.Mod.state}");

            await AwaitFrames(2);

            MainFile.Logger.Info($"[热重载] SL {modId}：继续存档…");
            await Sts2RunInterop.ContinueSavedRunAsync();
            await AwaitFrames(2);

            MainFile.Logger.Info($"[热重载] SL <<< 成功 {modId}（已回归战斗/Run）。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[热重载] SL {modId} 失败: {ex.Message}");
            MainFile.Logger.Warn("[热重载] 若卡主菜单，请手动点「继续」；若存档损坏请检查日志。");
        }
    }

    private static async Task AwaitFrames(int count)
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        for (int i = 0; i < count; i++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}
