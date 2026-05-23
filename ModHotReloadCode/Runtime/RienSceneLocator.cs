using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace ModHotReload.Runtime;

/// <summary>在场景树中定位 NCreature（兼容 internal RienCombatRoomLocator）。</summary>
internal static class RienSceneLocator
{
    internal static NCreature? FindCreatureNode(Creature creature)
    {
        Type? locator = AccessTools.TypeByName("Rien.RienCode.Presentation.RienCombatRoomLocator");
        MethodInfo? internalMethod = locator == null
            ? null
            : AccessTools.Method(locator, "FindCreatureNode");
        if (internalMethod?.Invoke(null, [creature]) is NCreature fromRien)
            return fromRien;

        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            return null;

        NCreature? byEntity = FindCreatureRecursive(tree.Root, creature);
        if (byEntity != null)
            return byEntity;

        Player? owner = creature.Player;
        if (owner == null)
            return null;

        return FindPlayerCreatureRecursive(tree.Root, owner);
    }

    internal static Node? FindNodeByName(Node root, string name)
    {
        if (root.Name == name)
            return root;

        foreach (Node child in root.GetChildren())
        {
            Node? found = FindNodeByName(child, name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static NCreature? FindCreatureRecursive(Node node, Creature creature)
    {
        if (node is NCreature nCreature && ReferenceEquals(nCreature.Entity, creature))
            return nCreature;

        foreach (Node child in node.GetChildren())
        {
            NCreature? found = FindCreatureRecursive(child, creature);
            if (found != null)
                return found;
        }

        return null;
    }

    private static NCreature? FindPlayerCreatureRecursive(Node node, Player player)
    {
        if (node is NCreature nCreature && nCreature.Entity?.Player == player)
            return nCreature;

        foreach (Node child in node.GetChildren())
        {
            NCreature? found = FindPlayerCreatureRecursive(child, player);
            if (found != null)
                return found;
        }

        return null;
    }
}
