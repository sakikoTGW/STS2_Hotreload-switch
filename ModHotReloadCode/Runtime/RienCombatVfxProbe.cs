using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace ModHotReload.Runtime;

/// <summary>通过反射触发 Rien 战斗特效并做场景/日志断言（不修改 Rien.dll）。须在 Godot 主线程调用。</summary>
internal static class RienCombatVfxProbe
{
    private const string LogicTypeName = "Rien.RienCode.Combat.RienCombatLogic";
    private const string PresentationTypeName = "Rien.RienCode.Presentation.RienCombatPresentation";
    private const string HeartLienPowerTypeName = "Rien.RienCode.Powers.HeartLienPower";
    private const string LienMaskPowerTypeName = "Rien.RienCode.Powers.LienMaskPower";
    private const string GlyphFxTypeName = "Rien.RienCode.Presentation.RienLiberationBlueGlyphFx";
    private const string FileLoggerTypeName = "Rien.RienCode.Diagnostics.RienFileLogger";

    internal static void RunAllOnMainThread(
        Player player,
        object state,
        List<RienCombatVerifyRunner.ScenarioResult> results)
    {
        try { ProbeBlackFog(player, state, results); }
        catch (Exception ex) { Record(results, "vfx_black_fog", false, ex.Message); }

        try { ProbeLiberationGlyph(player, results); }
        catch (Exception ex) { Record(results, "vfx_liberation_glyph", false, ex.Message); }

        try { ProbeHeartBloomGold(player, state, results); }
        catch (Exception ex) { Record(results, "vfx_heart_bloom_gold", false, ex.Message); }

        try { ProbePhase2Unmask(player, state, results); }
        catch (Exception ex) { Record(results, "vfx_phase2_unmask", false, ex.Message); }
    }

    private static void ProbeBlackFog(Player player, object state, List<RienCombatVerifyRunner.ScenarioResult> results)
    {
        SetStateProperty(state, "FuriosoTurnStartAuraActive", true);
        SetStateProperty(state, "SuppressNextFuriosoReplicaGrant", false);
        SyncPowers(player, state);

        bool fogNode = FindUnderCreature(player, "FuriosoBlackFog") != null;
        bool auraAnchor = FindUnderCreature(player, "RienAuraAnchor") != null;
        bool ok = fogNode || auraAnchor;
        Record(results, "vfx_black_fog", ok,
            ok
                ? $"FuriosoBlackFog={fogNode} RienAuraAnchor={auraAnchor}"
                : "未找到黑雾粒子节点");
    }

    private static void ProbeLiberationGlyph(Player player, List<RienCombatVerifyRunner.ScenarioResult> results)
    {
        NCreature? creature = RienSceneLocator.FindCreatureNode(player.Creature);
        if (creature == null)
        {
            Record(results, "vfx_liberation_glyph", false, "无 NCreature");
            return;
        }

        Type? glyphFx = ResolveType(GlyphFxTypeName);
        MethodInfo? play = glyphFx?.GetMethod(
            "Play",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (play == null)
        {
            Record(results, "vfx_liberation_glyph", false, "RienLiberationBlueGlyphFx.Play 不可用");
            return;
        }

        play.Invoke(null, [creature, 2]);

        Node? glyphRoot = FindUnderCreature(player, "RienLiberationBlueGlyphFx");
        int labelCount = CountLabels(glyphRoot);
        bool hasGlyphText = HasGlyphLabelText(glyphRoot);
        bool ok = labelCount >= 8 && hasGlyphText;
        Record(results, "vfx_liberation_glyph", ok,
            $"labels={labelCount} glyphText={hasGlyphText}");
    }

    private static void ProbeHeartBloomGold(Player player, object state, List<RienCombatVerifyRunner.ScenarioResult> results)
    {
        InvokeState(state, "TriggerHeartBloom");
        SyncPowers(player, state);

        bool bloomed = ReadBoolProperty(state, "HeartLienBloomed");
        bool heartPower = HasPower(player, HeartLienPowerTypeName);
        bool heartFx = FindUnderCreature(player, "HeartAura") != null
            || FindUnderCreature(player, "shin_rien") != null
            || FindInScene("RienProduceOverlay") != null;
        bool logOk = TailRienLogContains("心绽", "Phase2");

        bool ok = bloomed && heartPower && (heartFx || logOk);
        Record(results, "vfx_heart_bloom_gold", ok,
            $"bloomed={bloomed} HeartLienPower={heartPower} fx={heartFx} log={logOk}");
    }

    private static void ProbePhase2Unmask(Player player, object state, List<RienCombatVerifyRunner.ScenarioResult> results)
    {
        bool unmasked = !ReadBoolProperty(state, "HasLienMask");
        bool noMaskPower = !HasPower(player, LienMaskPowerTypeName);
        NCreature? node = RienSceneLocator.FindCreatureNode(player.Creature);
        bool phase2Anim = false;
        if (node?.GetNodeOrNull<AnimatedSprite2D>("RienLimbusSdAnim") is { } anim)
            phase2Anim = anim.Animation.ToString().Contains("Phase2", StringComparison.OrdinalIgnoreCase);

        bool ok = unmasked && noMaskPower;
        Record(results, "vfx_phase2_unmask", ok,
            $"HasLienMask={!unmasked} LienMaskPower={!noMaskPower} sdPhase2={phase2Anim}");
    }

    private static void SyncPowers(Player player, object state)
    {
        // 勿在主线程 Task.GetResult() 等待 SyncPowers（会死锁 Godot 主循环，导致 verify 无结果文件）
        Type? presentation = ResolveType(PresentationTypeName);
        presentation?.GetMethod("SyncAuras", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, [player, state]);

        Type? spinePlayer = AccessTools.TypeByName("Rien.RienCode.Presentation.RienLiberationSpinePlayer");
        spinePlayer?.GetMethod("Sync", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, [player, state]);
    }

    private static void SetStateProperty(object state, string propertyName, object value) =>
        state.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.SetValue(state, value);

    private static bool ReadBoolProperty(object state, string name) =>
        state.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(state) is true;

    private static void InvokeState(object state, string methodName, params object[] args) =>
        state.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?.Invoke(state, args);

    private static bool HasPower(Player player, string powerTypeName)
    {
        Type? t = ResolveType(powerTypeName);
        return t != null && player.Creature.Powers.Any(p => t.IsInstanceOfType(p));
    }

    private static Node? FindUnderCreature(Player player, string nodeName)
    {
        NCreature? creature = RienSceneLocator.FindCreatureNode(player.Creature);
        return creature == null ? null : FindNodeRecursive(creature, nodeName);
    }

    private static Node? FindInScene(string nodeName)
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            return null;

        return RienSceneLocator.FindNodeByName(tree.Root, nodeName);
    }

    private static Node? FindNodeRecursive(Node root, string name)
    {
        if (root.Name == name)
            return root;

        foreach (Node child in root.GetChildren())
        {
            Node? found = FindNodeRecursive(child, name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static bool HasGlyphLabelText(Node? root)
    {
        if (root == null)
            return false;

        if (root is Label label)
        {
            string t = label.Text;
            if (t.Contains("解放", StringComparison.Ordinal)
                || t.Contains("PRESCRIPT", StringComparison.OrdinalIgnoreCase)
                || t.Contains("_CLEAR_", StringComparison.Ordinal))
                return true;
        }

        foreach (Node child in root.GetChildren())
        {
            if (HasGlyphLabelText(child))
                return true;
        }

        return false;
    }

    private static int CountLabels(Node? root)
    {
        if (root == null)
            return 0;

        int count = root is Label ? 1 : 0;
        foreach (Node child in root.GetChildren())
            count += CountLabels(child);
        return count;
    }

    private static bool TailRienLogContains(params string[] needles)
    {
        try
        {
            Type? logger = ResolveType(FileLoggerTypeName);
            string? path = logger?.GetProperty("LogPath", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) as string;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            string[] lines = File.ReadAllLines(path);
            int start = Math.Max(0, lines.Length - 80);
            string tail = string.Join('\n', lines, start, lines.Length - start);
            return needles.Any(n => tail.Contains(n, StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private static Type? ResolveType(string fullName) =>
        AccessTools.TypeByName(fullName)
        ?? Type.GetType($"{fullName}, Rien", throwOnError: false);

    private static void Record(
        List<RienCombatVerifyRunner.ScenarioResult> results,
        string name,
        bool? pass,
        string detail)
    {
        string status = pass switch
        {
            true => "pass",
            false => "fail",
            _ => "skip"
        };
        results.Add(new RienCombatVerifyRunner.ScenarioResult(name, status, detail));
        MainFile.Logger.Info($"[RCV] {status.ToUpperInvariant()} {name}: {detail}");
    }
}
