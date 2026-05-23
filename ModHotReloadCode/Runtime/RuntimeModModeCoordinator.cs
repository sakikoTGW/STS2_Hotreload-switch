using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;
using ModHotReload.Reflection;

namespace ModHotReload.Runtime;

internal enum RuntimeModMode
{
    Modded,
    Vanilla
}

internal static class RuntimeModModeCoordinator
{
    private static readonly string ModeFile = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "STS2_ModHotReload",
        "runtime-mode.txt");

    private static int _switching;

    internal static RuntimeModMode CurrentMode { get; private set; } = LoadPersistedMode();

    internal static bool IsVanillaMode => CurrentMode == RuntimeModMode.Vanilla;

    internal static string Status =>
        $"{CurrentMode}，activeGameplayMods={GetActiveGameplayMods().Count}";

    internal static void ApplyStartupMode()
    {
        if (IsVanillaMode)
            DisableAllContentMods(persistSettings: true);
    }

    /// <summary>由原生模组界面同步 Modded/Vanilla 存档根（不写 runtime-mode.txt）。</summary>
    internal static void SyncModeFromUi(RuntimeModMode mode)
    {
        CurrentMode = mode;
    }

    internal static void RollbackFromSnapshot(ModSwitchCleanup.ModSwitchSnapshot snapshot)
    {
        try
        {
            if (!Enum.TryParse<RuntimeModMode>(snapshot.Mode, true, out RuntimeModMode mode))
                return;

            CurrentMode = mode;
            PersistMode(mode);

            foreach (ModSwitchCleanup.ModSwitchSnapshot.Entry entry in snapshot.Mods)
            {
                Mod? mod = ModManager.Mods.FirstOrDefault(m =>
                    string.Equals(m.manifest?.id, entry.Id, StringComparison.OrdinalIgnoreCase));
                if (mod == null)
                    continue;

                try
                {
                    ModLifecycleCoordinator.ApplyEnabledState(
                        mod,
                        entry.Enabled,
                        persistSettings: false,
                        reason: "switch-rollback",
                        refreshIfAlreadyLoaded: false);
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Warn($"[热重载] 回滚 {entry.Id} 失败: {ex.Message}");
                }
            }

            Sts2UiRefreshInterop.ScheduleAfterModListChanged();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[热重载] 切档回滚异常: {ex}");
        }
    }

    internal static async Task SwitchAsync(RuntimeModMode target, bool continueAfterSwitch)
    {
        if (Interlocked.CompareExchange(ref _switching, 1, 0) != 0)
        {
            MainFile.Logger.Warn("[热重载] Mod 模式切换正在进行，忽略重复请求。");
            return;
        }

        ModSwitchCleanup.BeginModeSwitch();
        ModSwitchCleanup.ModSwitchSnapshot snapshot = ModSwitchCleanup.TakeSnapshot();
        RuntimeModMode previousMode = CurrentMode;

        try
        {
            await ModSwitchCleanup.WaitForQuiescenceAsync();

            MainFile.Logger.Info($"[热重载] 模式切换 >>> {CurrentMode} -> {target}");

            bool hadRun = Sts2RunInterop.GetCurrentRoom() != null;
            if (hadRun)
            {
                await Sts2RunInterop.SaveCurrentRunAsync(saveProgress: true);
                await AwaitFrames(2);
                await Sts2RunInterop.ReturnToMainMenuAsync();
                await AwaitFrames(3);
            }

            CurrentMode = target;
            PersistMode(target);

            if (target == RuntimeModMode.Vanilla)
                DisableAllContentMods(persistSettings: true);
            else
                EnableAllContentMods(persistSettings: true);

            RefreshMainMenu();

            if (continueAfterSwitch && SaveManager.Instance.HasRunSave)
            {
                await AwaitFrames(2);
                await Sts2RunInterop.ContinueSavedRunAsync();
            }

            ModSwitchCleanup.CommitSnapshot();
            MainFile.Logger.Info($"[热重载] 模式切换 <<< {Status}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[热重载] 模式切换失败: {ex}");
            CurrentMode = previousMode;
            PersistMode(previousMode);
            RestoreFromSnapshot(snapshot);
        }
        finally
        {
            ModSwitchCleanup.EndModeSwitch();
            Interlocked.Exchange(ref _switching, 0);
        }
    }

    internal static void DisableMod(Mod mod, bool persistSettings)
    {
        string? modId = mod.manifest?.id;
        if (string.IsNullOrEmpty(modId) || IsSelf(mod))
            return;

        try
        {
            Assembly? oldAssembly = mod.assembly;
            ModStagingStore.ClearPending(modId);
            HotReloadCoordinator.ClearThrottle(modId);

            HarmonyUnpatchUtil.UnpatchMod(mod);
            BaseLibInterop.TryUnregisterModContent(modId, oldAssembly);
            ModelDbCleanup.RemoveModModels(modId, oldAssembly);
            GodotScriptRegistrationInterop.UnregisterPathsForMod(modId, oldAssembly);

            Sts2AssetInterop.PurgeModAssets(modId);
            GodotResourceInterop.VirtualUnmountResourcePack(modId);

            mod.state = ModLoadState.Disabled;
            mod.assembly = null;
            mod.errors = null;
            if (oldAssembly != null)
                OfficialModLoader.UnloadAssemblyContext(modId);

            ModSwitchCleanup.TeardownMod(modId, oldAssembly);

            if (persistSettings)
                ModManagerReflection.SetModEnabled(mod, false);

            Sts2UiRefreshInterop.ScheduleAfterModListChanged();
            MainFile.Logger.Info($"[热重载] 已关闭 mod: {modId}（ModelDb/Harmony/PCK/选角 UI 已更新）");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[热重载] 关闭 {modId} 失败: {ex}");
        }
        finally
        {
            ModelDbCleanup.InvalidateListCaches();
            ModManagerReflection.InvalidateHarmonyCache();
        }
    }

    internal static void EnableMod(Mod mod, bool persistSettings)
    {
        string? modId = mod.manifest?.id;
        if (string.IsNullOrEmpty(modId) || IsSelf(mod))
            return;

        try
        {
            if (persistSettings)
                ModManagerReflection.SetModEnabled(mod, true);

            ModLifecycleCoordinator.PreparePayloadFromDisk(mod, modId);

            mod.state = ModLoadState.None;
            mod.assembly = null;
            mod.errors = null;
            OfficialModLoader.LoadMod(mod);

            if (mod.state == ModLoadState.Loaded)
                ModLifecycleCoordinator.AfterSuccessfulLoad(mod);

            MainFile.Logger.Info($"[热重载] 已开启 mod: {modId} state={mod.state}");
        }
        catch (Exception ex)
        {
            mod.state = ModLoadState.Failed;
            MainFile.Logger.Error($"[热重载] 开启 {modId} 失败: {ex}");
        }
        finally
        {
            ModelDbCleanup.InvalidateListCaches();
            ModManagerReflection.InvalidateHarmonyCache();
            if (mod.state == ModLoadState.Loaded)
                Sts2UiRefreshInterop.ScheduleAfterModListChanged();
        }
    }

    internal static List<Mod> GetActiveGameplayMods() =>
        ModManager.Mods
            .Where(m => !IsSelf(m))
            .Where(m => m.state == ModLoadState.Loaded)
            .Where(m => m.manifest?.affectsGameplay ?? true)
            .ToList();

    internal static List<string>? GetGameplayRelevantModNameList()
    {
        if (IsVanillaMode)
            return null;

        List<string> mods = GetActiveGameplayMods()
            .Select(m => $"{m.manifest!.id}-{m.manifest.version}")
            .ToList();

        return mods.Count == 0 ? null : mods;
    }

    internal static bool IsRunningModded() =>
        !IsVanillaMode && GetActiveGameplayMods().Count > 0;

    internal static void RestoreFromSnapshot(ModSwitchCleanup.ModSwitchSnapshot snapshot)
    {
        MainFile.Logger.Warn($"[热重载] 按快照恢复 mod 状态（{snapshot.Mods.Count} 条）…");

        if (IsVanillaMode)
        {
            DisableAllContentMods(persistSettings: false);
            return;
        }

        foreach (ModSwitchCleanup.ModSwitchSnapshot.Entry entry in snapshot.Mods)
        {
            Mod? mod = ModManager.Mods.FirstOrDefault(m =>
                string.Equals(m.manifest?.id, entry.Id, StringComparison.OrdinalIgnoreCase));
            if (mod == null)
                continue;

            try
            {
                ModLifecycleCoordinator.ApplyEnabledState(
                    mod,
                    entry.Enabled,
                    persistSettings: false,
                    reason: "switch-rollback",
                    refreshIfAlreadyLoaded: false);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[热重载] 回滚 {entry.Id} 失败: {ex.Message}");
            }
        }

        Sts2UiRefreshInterop.ScheduleAfterModListChanged();
    }

    private static void DisableAllContentMods(bool persistSettings)
    {
        foreach (Mod mod in ModManager.Mods.Reverse().Where(m => !IsSelf(m)).ToList())
            ModLifecycleCoordinator.ApplyEnabledState(
                mod, enabled: false, persistSettings, reason: "DisableAllContentMods");
    }

    private static void EnableAllContentMods(bool persistSettings)
    {
        foreach (Mod mod in ModManager.Mods.Where(m => !IsSelf(m)).ToList())
            ModLifecycleCoordinator.ApplyEnabledState(
                mod,
                enabled: true,
                persistSettings,
                reason: "EnableAllContentMods",
                refreshIfAlreadyLoaded: false);

        Sts2UiRefreshInterop.ScheduleAfterModListChanged();
    }

    private static bool IsSelf(Mod mod) =>
        string.Equals(mod.manifest?.id, MainFile.ModId, StringComparison.OrdinalIgnoreCase);

    private static RuntimeModMode LoadPersistedMode()
    {
        try
        {
            if (!File.Exists(ModeFile))
                return RuntimeModMode.Modded;

            string text = File.ReadAllText(ModeFile).Trim();
            return text.Equals("vanilla", StringComparison.OrdinalIgnoreCase)
                ? RuntimeModMode.Vanilla
                : RuntimeModMode.Modded;
        }
        catch
        {
            return RuntimeModMode.Modded;
        }
    }

    private static void PersistMode(RuntimeModMode mode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModeFile)!);
        File.WriteAllText(ModeFile, mode == RuntimeModMode.Vanilla ? "vanilla" : "modded");
    }

    private static void RefreshMainMenu()
    {
        try
        {
            object? mainMenu = typeof(MegaCrit.Sts2.Core.Nodes.NGame)
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null)
                ?.GetType()
                .GetProperty("MainMenu", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(MegaCrit.Sts2.Core.Nodes.NGame.Instance);

            mainMenu?.GetType()
                .GetMethod("RefreshButtons", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(mainMenu, null);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] 刷新主菜单失败: {ex.Message}");
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
