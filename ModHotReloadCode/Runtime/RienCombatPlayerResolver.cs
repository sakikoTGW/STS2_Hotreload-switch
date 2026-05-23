using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace ModHotReload.Runtime;

/// <summary>解析当前 Rien 玩家（不依赖 LastRienPlayer，Debug 进战在首回合前可能为空）。</summary>
internal static class RienCombatPlayerResolver
{
    private const string RienCharacterTypeName = "Rien.RienCode.Character.Rien";
    private const string TrackerTypeName = "Rien.RienCode.Combat.RienCombatTracker";

    private static readonly FieldInfo? CombatStateField =
        typeof(CombatManager).Assembly
            .GetType("MegaCrit.Sts2.Core.Combat.CombatStateTracker")
            ?.GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static Player? Resolve()
    {
        if (TryLastRienPlayer(out Player? fromTracker))
            return fromTracker;

        if (TryCombatPlayers(out fromTracker))
            return fromTracker;

        if (TryRunPlayers(out fromTracker))
            return fromTracker;

        return null;
    }

    internal static bool TryRememberContext(Player player)
    {
        Type? tracker = AccessTools.TypeByName(TrackerTypeName);
        MethodInfo? remember = tracker?.GetMethod(
            "RememberCombatContext",
            BindingFlags.Public | BindingFlags.Static);
        if (remember == null)
            return false;

        try
        {
            remember.Invoke(null, [null!, player]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLastRienPlayer(out Player? player)
    {
        player = null;
        Type? tracker = AccessTools.TypeByName(TrackerTypeName);
        PropertyInfo? last = tracker?.GetProperty("LastRienPlayer", BindingFlags.Public | BindingFlags.Static);
        player = last?.GetValue(null) as Player;
        return IsRien(player);
    }

    private static bool TryCombatPlayers(out Player? player)
    {
        player = null;
        CombatManager? cm = CombatManager.Instance;
        if (cm == null || CombatStateField == null)
            return false;

        object? tracker = typeof(CombatManager).GetProperty(
                "StateTracker",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(cm);
        if (tracker == null)
            return false;

        if (CombatStateField.GetValue(tracker) is not { } combatState)
            return false;

        PropertyInfo? playersProp = combatState.GetType().GetProperty(
            "Players",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (playersProp?.GetValue(combatState) is not System.Collections.IEnumerable players)
            return false;

        foreach (object? entry in players)
        {
            if (entry is Player p && IsRien(p))
            {
                player = p;
                return true;
            }
        }

        return false;
    }

    private static bool TryRunPlayers(out Player? player)
    {
        player = null;
        RunManager? run = RunManager.Instance;
        PropertyInfo? stateProp = typeof(RunManager).GetProperty(
            "State",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (run == null || stateProp?.GetValue(run) is not { } runState)
            return false;

        PropertyInfo? playersProp = runState.GetType().GetProperty(
            "Players",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (playersProp?.GetValue(runState) is not System.Collections.IEnumerable players)
            return false;

        foreach (object? entry in players)
        {
            if (entry is Player p && IsRien(p))
            {
                player = p;
                return true;
            }
        }

        return false;
    }

    private static bool IsRien(Player? player)
    {
        if (player?.Character == null)
            return false;

        Type? rien = AccessTools.TypeByName(RienCharacterTypeName);
        return rien != null && rien.IsInstanceOfType(player.Character);
    }
}
