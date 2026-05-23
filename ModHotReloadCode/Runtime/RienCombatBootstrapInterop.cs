using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace ModHotReload.Runtime;

/// <summary>Debug 进战后预热 Rien 视觉与战斗状态（等价于 ScheduleScan + OnRienCombatEnter）。</summary>
internal static class RienCombatBootstrapInterop
{
    private static bool _warmUpDone;

    internal static async Task WarmUpAfterCombatEntryAsync()
    {
        if (_warmUpDone)
            return;

        for (int i = 0; i < 30; i++)
        {
            Player? player = RienCombatPlayerResolver.Resolve();
            if (player != null)
            {
                RienCombatPlayerResolver.TryRememberContext(player);
                await WarmUpPlayerAsync(player).ConfigureAwait(false);
                _warmUpDone = true;
                MainFile.Logger.Info("[RCV] Rien 战斗预热完成。");
                return;
            }

            await Task.Delay(200).ConfigureAwait(false);
        }

        MainFile.Logger.Warn("[RCV] 战斗预热超时：未找到 Rien 玩家。");
    }

    private static async Task WarmUpPlayerAsync(Player player)
    {
        Type? bootstrap = AccessTools.TypeByName("Rien.RienCode.Presentation.RienPlayerVisualBootstrap");
        bootstrap?.GetMethod("ScanAllRienPlayersInTree", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, null);

        NCreature? node = RienSceneLocator.FindCreatureNode(player.Creature);
        if (node != null)
        {
            Type? sd = AccessTools.TypeByName("Rien.RienCode.Presentation.RienLimbusSdSpritePlayer");
            sd?.GetMethod("EnsureUnder", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, [node]);
            MethodInfo? isActive = sd?.GetMethod("IsPresentationActive", BindingFlags.Public | BindingFlags.Static);
            bool active = isActive?.Invoke(null, [node]) is true;
            if (!active)
                sd?.GetMethod("EnsureIdle", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, [node]);
            sd?.GetMethod("ApplyLayout", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, [node]);
        }

        Type? tracker = AccessTools.TypeByName("Rien.RienCode.Combat.RienCombatTracker");
        MethodInfo? getState = tracker?.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
        object? state = getState?.Invoke(null, [player]);

        Type? logic = AccessTools.TypeByName("Rien.RienCode.Combat.RienCombatLogic");
        MethodInfo? onEnter = logic?.GetMethod("OnRienCombatEnter", BindingFlags.Public | BindingFlags.Static);
        if (onEnter?.Invoke(null, [player, state]) is Task enterTask)
            await enterTask.ConfigureAwait(false);

        Type? cmd = AccessTools.TypeByName("Rien.RienCode.Combat.RienCommandSystem");
        MethodInfo? onCombatStart = cmd?.GetMethod("OnCombatStart", BindingFlags.Public | BindingFlags.Static);
        if (onCombatStart?.Invoke(null, [player]) is Task startTask)
            await startTask.ConfigureAwait(false);
    }

}
