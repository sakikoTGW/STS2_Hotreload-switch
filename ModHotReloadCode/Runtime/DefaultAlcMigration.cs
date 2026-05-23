using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload.Runtime;

/// <summary>
/// 把已卡在 Default ALC 的内容 mod 迁到 collectible，无需重启整局游戏（.NET 仍无法卸载 Default 里的旧程序集，但新逻辑走 collectible）。
/// </summary>
internal static class DefaultAlcMigration
{
    private static int _scheduled;

    internal static void ScheduleAfterSceneReady()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) != 0)
            return;

        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        void OnFrame()
        {
            if (tree.Root == null)
                return;

            tree.ProcessFrame -= OnFrame;
            MigrateAllLoadedModsInDefaultAlc();
        }

        tree.ProcessFrame += OnFrame;
    }

    internal static void MigrateAllLoadedModsInDefaultAlc()
    {
        var targets = new List<Mod>();
        foreach (Mod mod in ModManager.Mods)
        {
            string? id = mod.manifest?.id;
            if (string.IsNullOrEmpty(id) || string.Equals(id, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (mod.state != ModLoadState.Loaded && mod.state != ModLoadState.Failed)
                continue;

            if (mod.assembly == null || !ModAssemblyLoader.IsDefaultAlc(mod.assembly))
                continue;

            if (mod.manifest?.hasDll != true)
                continue;

            targets.Add(mod);
        }

        if (targets.Count == 0)
            return;

        MainFile.Logger.Info(
            $"[热重载] 检测到 {targets.Count} 个模组仍在 Default ALC，正在迁到 collectible（无需重启游戏）…");

        foreach (Mod mod in targets)
        {
            string id = mod.manifest!.id;
            try
            {
                HotReloadCoordinator.Reload(mod, ReloadChangeKind.DllOrJson, force: true);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Error($"[热重载] {id} Default→collectible 迁移失败: {ex.Message}");
            }
        }
    }
}
