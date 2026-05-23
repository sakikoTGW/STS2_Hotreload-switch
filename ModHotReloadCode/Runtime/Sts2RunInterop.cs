using System.Reflection;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace ModHotReload.Runtime;

/// <summary>通过反射调用 Run/Save/主菜单继续（部分 API 为 internal）。</summary>
internal static class Sts2RunInterop
{
    private static readonly PropertyInfo? RunManagerState =
        typeof(RunManager).GetProperty("State", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? MainMenuContinueAsync =
        typeof(NGame).Assembly
            .GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu")
            ?.GetMethod("OnContinueButtonPressedAsync", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    internal static AbstractRoom? GetCurrentRoom()
    {
        var run = RunManager.Instance;
        if (run == null || RunManagerState == null)
            return null;

        if (RunManagerState.GetValue(run) is not RunState state)
            return null;

        return state.CurrentRoom;
    }

    internal static Task SaveCurrentRunAsync(bool saveProgress = true)
    {
        AbstractRoom? room = GetCurrentRoom();
        if (room == null)
            throw new InvalidOperationException("无 CurrentRoom，无法存档。");

        return SaveManager.Instance.SaveRun(room, saveProgress);
    }

    internal static Task ReturnToMainMenuAsync()
    {
        NGame game = NGame.Instance ?? throw new InvalidOperationException("NGame.Instance 为空。");
        return game.ReturnToMainMenu();
    }

    internal static async Task ContinueSavedRunAsync()
    {
        NGame game = NGame.Instance ?? throw new InvalidOperationException("NGame.Instance 为空。");
        var mainMenu = game.MainMenu ?? throw new InvalidOperationException("MainMenu 未就绪。");

        if (MainMenuContinueAsync == null)
            throw new MissingMethodException("NMainMenu.OnContinueButtonPressedAsync 未找到。");

        if (MainMenuContinueAsync.Invoke(mainMenu, null) is not Task task)
            throw new InvalidOperationException("继续游戏未返回 Task。");

        await task;
    }

    internal static bool HasRunSave() => SaveManager.Instance.HasRunSave;
}
