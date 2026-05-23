using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace ModHotReload.Runtime;

/// <summary>关闭/开启 mod 后刷新仍停留在选角等界面上的 UI。</summary>
internal static class Sts2UiRefreshInterop
{
    internal static void AfterModListChanged()
    {
        RefreshMainMenuButtons();
        RefreshCharacterSelectButtons();
    }

    private static void RefreshMainMenuButtons()
    {
        try
        {
            object? mainMenu = typeof(NGame)
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null)
                ?.GetType()
                .GetProperty("MainMenu", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(NGame.Instance);

            mainMenu?.GetType()
                .GetMethod("RefreshButtons", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(mainMenu, null);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] 刷新主菜单失败: {ex.Message}");
        }
    }

    private static void RefreshCharacterSelectButtons()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            return;

        NCharacterSelectScreen? screen = FindCharacterSelectScreen(tree.Root);
        if (screen == null)
            return;

        try
        {
            MethodInfo? init = typeof(NCharacterSelectScreen).GetMethod(
                "InitCharacterButtons",
                BindingFlags.Instance | BindingFlags.NonPublic);
            init?.Invoke(screen, null);
            MainFile.Logger.Info("[热重载] 已刷新选角界面（InitCharacterButtons）。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] 刷新选角界面失败: {ex.Message}");
        }
    }

    private static NCharacterSelectScreen? FindCharacterSelectScreen(Node node)
    {
        if (node is NCharacterSelectScreen screen)
            return screen;

        foreach (Node child in node.GetChildren())
        {
            NCharacterSelectScreen? found = FindCharacterSelectScreen(child);
            if (found != null)
                return found;
        }

        return null;
    }
}
