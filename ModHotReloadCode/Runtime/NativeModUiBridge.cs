using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using MegaCrit.Sts2.Core.Saves;
using ModHotReload.Reflection;

namespace ModHotReload.Runtime;

/// <summary>
/// 挂钩 STS2 原生模组界面（NModdingScreen / NModMenuRow），勾选后立即运行期启停，无需重启。
/// </summary>
internal static class NativeModUiBridge
{
    private static readonly FieldInfo? RowIsEnabledField =
        typeof(NModMenuRow).GetField("_isEnabled", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo? RowModProperty =
        typeof(NModMenuRow).GetProperty("Mod", BindingFlags.Public | BindingFlags.Instance);

    private static readonly FieldInfo? ScreenPendingWarningField =
        typeof(NModdingScreen).GetField("_pendingChangesWarning", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? RowScreenField =
        typeof(NModMenuRow).GetField("_screen", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static void OnModRowToggled(NModMenuRow row)
    {
        if (RowModProperty?.GetValue(row) is not Mod mod || IsSelf(mod))
            return;

        bool enabled = RowIsEnabledField != null && (bool)(RowIsEnabledField.GetValue(row) ?? false);
        ModManagerReflection.EnterSuppressModDetectedEvent();
        try
        {
            ModLifecycleCoordinator.ApplyEnabledState(mod, enabled, persistSettings: true, reason: "勾选");
            HidePendingRestartWarning(GetScreenFromRow(row) ?? FindModdingScreen());
            Sts2UiRefreshInterop.AfterModListChanged();
        }
        finally
        {
            ScheduleReleaseSuppressModDetectedEvent();
        }
    }

    internal static void OnModSettingsCommitted(NModdingScreen? screen)
    {
        if (ModManagerReflection.SuppressModDetectedEvent)
            return;

        try
        {
            using (SuppressUiSideEffects())
                ApplyAllFromSettingsSave();

            HidePendingRestartWarning(screen);
            BaseLibInterop.TryRefreshMainMenuInjection();
            Sts2UiRefreshInterop.AfterModListChanged();
            MainFile.Logger.Info("[热重载] 原生模组界面：已应用勾选状态（无需重启）。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[热重载] 原生模组界面应用失败: {ex}");
        }
    }

    private static void ApplyAllFromSettingsSave()
    {
        ModSettings? settings = SaveManager.Instance.SettingsSave?.ModSettings;
        if (settings?.ModList == null)
            return;

        var desired = new Dictionary<(string id, ModSource source), bool>();
        foreach (SettingsSaveMod entry in settings.ModList)
            desired[(entry.Id.ToLowerInvariant(), entry.Source)] = entry.IsEnabled;

        foreach (Mod mod in ModManager.Mods.ToList())
        {
            if (IsSelf(mod) || mod.manifest?.id == null)
                continue;

            bool enabled = desired.TryGetValue((mod.manifest.id.ToLowerInvariant(), mod.modSource), out bool v) && v;
            ModLifecycleCoordinator.ApplyEnabledState(mod, enabled, persistSettings: false, reason: "应用设置");
        }

        bool anyGameplay = ModManager.Mods.Any(m =>
            !IsSelf(m)
            && m.manifest?.affectsGameplay != false
            && desired.TryGetValue((m.manifest!.id.ToLowerInvariant(), m.modSource), out bool on)
            && on);

        RuntimeModModeCoordinator.SyncModeFromUi(anyGameplay ? RuntimeModMode.Modded : RuntimeModMode.Vanilla);
    }

    internal static void ApplyModEnabledState(Mod mod, bool enabled, bool persistSettings) =>
        ModLifecycleCoordinator.ApplyEnabledState(mod, enabled, persistSettings, reason: "ApplyModEnabledState");

    private static void HidePendingRestartWarning(NModdingScreen? screen)
    {
        if (screen == null || ScreenPendingWarningField?.GetValue(screen) is not CanvasItem warning)
            return;

        warning.Visible = false;
    }

    private static NModdingScreen? GetScreenFromRow(NModMenuRow row) =>
        RowScreenField?.GetValue(row) as NModdingScreen;

    private static NModdingScreen? FindModdingScreen()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            return null;

        return FindModdingScreenRecursive(tree.Root);
    }

    private static NModdingScreen? FindModdingScreenRecursive(Node node)
    {
        if (node is NModdingScreen screen)
            return screen;

        foreach (Node child in node.GetChildren())
        {
            NModdingScreen? found = FindModdingScreenRecursive(child);
            if (found != null)
                return found;
        }

        return null;
    }

    private static bool IsSelf(Mod mod) =>
        string.Equals(mod.manifest?.id, MainFile.ModId, StringComparison.OrdinalIgnoreCase);

    internal static bool ShouldSkipNewModDetected(Mod mod)
    {
        if (ModManagerReflection.SuppressModDetectedEvent)
            return true;

        string? id = mod.manifest?.id;
        if (id == null)
            return false;

        return ModManager.Mods.Any(m =>
            m != mod
            && string.Equals(m.manifest?.id, id, StringComparison.OrdinalIgnoreCase)
            && m.modSource == mod.modSource);
    }

    internal static IDisposable SuppressUiSideEffects()
    {
        ModManagerReflection.EnterSuppressModDetectedEvent();
        return new SuppressScope();
    }

    private static void ScheduleReleaseSuppressModDetectedEvent()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            ModManagerReflection.ExitSuppressModDetectedEvent();
            return;
        }

        void OnFrame()
        {
            tree.ProcessFrame -= OnFrame;
            ModManagerReflection.ExitSuppressModDetectedEvent();
        }

        tree.ProcessFrame += OnFrame;
    }

    private sealed class SuppressScope : IDisposable
    {
        public void Dispose() => ModManagerReflection.ExitSuppressModDetectedEvent();
    }
}
