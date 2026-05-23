using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace ModHotReload.Runtime;

internal static class RienCombatVerifier
{
    private const string RienTrackerTypeName = "Rien.RienCode.Combat.RienCombatTracker";
    private const string RienSpriteTypeName = "Rien.RienCode.Presentation.RienLimbusSdSpritePlayer";
    private const string HermesRelicTypeName = "Rien.RienCode.Relics.HermesCommandTerminalRelic";
    private const string PresentationTypeName = "Rien.RienCode.Presentation.RienCombatPresentation";
    private const string WeaponCatalogTypeName = "Rien.RienCode.Combat.WeaponCatalog";
    private const string RienFileLoggerTypeName = "Rien.RienCode.Diagnostics.RienFileLogger";

    internal static void RunAll(List<RienCombatVerifyRunner.ScenarioResult> results)
    {
        Record(results, "rien_mod_loaded", IsRienModLoaded(), DescribeRienMod());
        Record(results, "critical_fixes", RienRuntimeCriticalFixes.IsInstalled, "ModHotReload Rien 运行时补丁");
        Record(results, "combat_in_progress", CombatManager.Instance?.IsInProgress == true, "CombatManager.IsInProgress");

        if (!TryGetRienPlayer(out Player? player, out object? state, out string? playerDetail))
        {
            Record(results, "rien_player", false, playerDetail ?? "无 Rien 玩家");
            return;
        }

        Record(results, "rien_player", true, playerDetail!);
        Record(results, "hermes_relic", HasHermesRelic(player!), DescribeRelics(player!));
        Record(results, "sd_sprite", HasSdSprite(player!), DescribeSd(player!));
        Record(results, "facing_enemy", IsFacingEnemy(player!), DescribeFacing(player!));
        Record(results, "card_overlay_ui", HasRienCardOverlayInScene(), "场景树 RienCardOverlay 节点");
        Record(results, "hand_rien_cards", HasRienCardsInHand(player!), DescribeHand(player!));
        Record(results, "power_icon_path", CheckPowerIconPaths(player!), "Rien Power 自定义图标路径");
        Record(results, "weapon_catalog_9", CheckWeaponCatalogCount(), "WeaponCatalog.All 数量");
        Record(results, "rien_file_log", !string.IsNullOrEmpty(FindRienLogPath()), FindRienLogPath() ?? "未找到 rien-*.log");

        _ = state;
    }

    internal static async Task<(bool ok, string detail)> ProbeWeaponStrikesAsync(Player player, object? state)
    {
        Type? presentation = ResolveRienType(PresentationTypeName);
        MethodInfo? play = presentation?.GetMethod("PlayWeaponStrike", BindingFlags.Public | BindingFlags.Static);
        Type? catalog = ResolveRienType(WeaponCatalogTypeName);
        FieldInfo? allField = catalog?.GetField("All", BindingFlags.Public | BindingFlags.Static);
        if (play == null || allField?.GetValue(null) is not Array weapons)
            return (false, "PlayWeaponStrike 或 WeaponCatalog 不可用");

        Creature? target = player.Creature.CombatState?.HittableEnemies.FirstOrDefault(e => e.IsAlive);
        if (target == null)
            return (false, "无存活敌人");

        RecordAllWeapons(state, weapons);

        bool fullProbe = string.Equals(
            System.Environment.GetEnvironmentVariable("STS2_MODHOTRELOAD_RIEN_WEAPON_PROBE_FULL"),
            "1",
            StringComparison.Ordinal);

        IEnumerable<object> probeSet = fullProbe
            ? weapons.Cast<object>()
            : weapons.Cast<object>().Take(2);

        int invoked = 0;
        var errors = new List<string>();
        foreach (object weapon in probeSet)
        {
            try
            {
                if (play.Invoke(null, [player, target, weapon, false, false]) is Task task)
                    await task.ConfigureAwait(false);
                invoked++;
                if (!fullProbe)
                    await Task.Delay(120).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                errors.Add($"{weapon}={ex.Message}");
            }
        }

        int expected = fullProbe ? weapons.Length : Math.Min(2, weapons.Length);
        bool ok = invoked >= expected && errors.Count == 0;
        string detail = $"invoked={invoked}/{expected}{(fullProbe ? "" : " sample")}"
            + (errors.Count > 0 ? " errs=" + string.Join("; ", errors) : "");
        return (ok, detail);
    }

    private static bool IsRienModLoaded() =>
        ModManager.Mods.Any(m =>
            string.Equals(m.manifest?.id, "Rien", StringComparison.OrdinalIgnoreCase)
            && m.state == ModLoadState.Loaded);

    private static string DescribeRienMod()
    {
        Mod? m = ModManager.Mods.FirstOrDefault(x =>
            string.Equals(x.manifest?.id, "Rien", StringComparison.OrdinalIgnoreCase));
        return m == null ? "Rien 未加载" : $"Rien state={m.state}";
    }

    private static bool TryGetRienPlayer(out Player? player, out object? state, out string? detail)
    {
        player = null;
        state = null;
        detail = null;

        player = RienCombatPlayerResolver.Resolve();
        if (player == null)
        {
            detail = "未在 Run/Combat 中解析到 Rien 玩家";
            return false;
        }

        RienCombatPlayerResolver.TryRememberContext(player);

        Type? tracker = ResolveRienType(RienTrackerTypeName);
        MethodInfo? getState = tracker?.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
        state = getState?.Invoke(null, [player]);
        detail = $"NetId={player.NetId}";
        return true;
    }

    private static bool HasHermesRelic(Player player)
    {
        Type? relicType = ResolveRienType(HermesRelicTypeName);
        if (relicType == null)
            return false;

        return player.Relics.Any(relicType.IsInstanceOfType);
    }

    private static string DescribeRelics(Player player) =>
        string.Join(", ", player.Relics.Select(r => r.GetType().Name));

    private static bool HasSdSprite(Player player)
    {
        NCreature? node = FindCreatureNode(player.Creature);
        Type? spriteType = ResolveRienType(RienSpriteTypeName);
        if (spriteType != null && node != null
            && (FindNodeOfType((Node)node, spriteType) != null || node.GetNodeOrNull("RienLimbusSdAnim") != null))
            return true;

        if (Engine.GetMainLoop() is SceneTree tree && tree.Root != null)
            return RienSceneLocator.FindNodeByName(tree.Root, "RienLimbusSdAnim") != null;

        return false;
    }

    private static string DescribeSd(Player player)
    {
        NCreature? node = FindCreatureNode(player.Creature);
        if (node == null)
            return "NCreature 未找到";

        AnimatedSprite2D? anim = node.GetNodeOrNull<AnimatedSprite2D>("RienLimbusSdAnim");
        if (anim == null)
            return "无 RienLimbusSdAnim 节点";

        return anim.IsPlaying()
            ? $"SD 播放中 frame={anim.Frame} anim={anim.Animation}"
            : $"SD 已挂载 visible={anim.Visible} anim={anim.Animation}";
    }

    private static bool IsFacingEnemy(Player player)
    {
        NCreature? self = FindCreatureNode(player.Creature);
        Creature? enemy = player.Creature.CombatState?.HittableEnemies.FirstOrDefault(e => e.IsAlive);
        NCreature? enemyNode = enemy == null ? null : FindCreatureNode(enemy);
        if (self == null || enemyNode == null)
            return false;

        float selfX = self.GlobalPosition.X;
        float enemyX = enemyNode.GlobalPosition.X;
        if (Math.Abs(enemyX - selfX) < 1f)
            return false;

        bool shouldFaceRight = enemyX > selfX;
        float scaleX = ReadSdScaleX(self);
        return Math.Abs(scaleX) > 0.001f && (scaleX > 0f) == shouldFaceRight;
    }

    private static float ReadSdScaleX(NCreature self)
    {
        Type? spriteType = ResolveRienType(RienSpriteTypeName);
        if (spriteType != null)
        {
            Node? sd = FindNodeOfType((Node)self, spriteType) ?? self.GetNodeOrNull("RienLimbusSdAnim");
            if (sd is Node2D sd2d && Math.Abs(sd2d.Scale.X) > 0.001f)
                return sd2d.Scale.X;
        }

        return ReadScaleX(self);
    }

    private static string DescribeFacing(Player player)
    {
        NCreature? self = FindCreatureNode(player.Creature);
        Creature? enemy = player.Creature.CombatState?.HittableEnemies.FirstOrDefault(e => e.IsAlive);
        NCreature? enemyNode = enemy == null ? null : FindCreatureNode(enemy);
        float scaleX = self == null ? 0f : ReadSdScaleX(self);
        return $"selfX={self?.GlobalPosition.X:F0} enemyX={enemyNode?.GlobalPosition.X:F0} scaleX={scaleX:F2}";
    }

    private static bool HasRienCardsInHand(Player player)
    {
        CardPile? hand = player.Piles.FirstOrDefault(p => p.Type == PileType.Hand);
        if (hand == null || hand.Cards.Count == 0)
            return false;

        return hand.Cards.Any(c => c.GetType().Namespace?.Contains("Rien", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string DescribeHand(Player player)
    {
        CardPile? hand = player.Piles.FirstOrDefault(p => p.Type == PileType.Hand);
        if (hand == null)
            return "无 Hand 牌堆";

        int rien = hand.Cards.Count(c => c.GetType().Namespace?.Contains("Rien", StringComparison.OrdinalIgnoreCase) == true);
        return $"hand={hand.Cards.Count} rien_cards={rien}";
    }

    private static bool HasRienCardOverlayInScene()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            return false;

        return RienSceneLocator.FindNodeByName(tree.Root, "RienCardOverlay") != null;
    }

    private static bool CheckPowerIconPaths(Player player)
    {
        bool any = false;
        bool ok = true;
        foreach (PowerModel power in player.Creature.Powers)
        {
            string? path = TryReadCustomIconPath(power);
            if (string.IsNullOrEmpty(path))
                continue;

            any = true;
            if (!path.StartsWith("res://Rien/", StringComparison.OrdinalIgnoreCase))
                ok = false;
        }

        return !any || ok;
    }

    private static string? TryReadCustomIconPath(PowerModel power) =>
        power.GetType().GetProperty("CustomPackedIconPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(power) as string;

    private static bool CheckWeaponCatalogCount()
    {
        Type? catalog = ResolveRienType(WeaponCatalogTypeName);
        if (catalog == null)
            return false;

        FieldInfo? allField = catalog.GetField("All", BindingFlags.Public | BindingFlags.Static);
        if (allField?.GetValue(null) is Array all && all.Length >= 9)
            return true;

        FieldInfo? strike = catalog.GetField("StrikeWeapons", BindingFlags.Public | BindingFlags.Static);
        FieldInfo? pierce = catalog.GetField("PierceWeapons", BindingFlags.Public | BindingFlags.Static);
        FieldInfo? slash = catalog.GetField("SlashWeapons", BindingFlags.Public | BindingFlags.Static);
        int count = 0;
        if (strike?.GetValue(null) is Array s)
            count += s.Length;
        if (pierce?.GetValue(null) is Array p)
            count += p.Length;
        if (slash?.GetValue(null) is Array sl)
            count += sl.Length;
        return count >= 9;
    }

    private static void RecordAllWeapons(object? state, Array weapons)
    {
        if (state == null)
            return;

        MethodInfo? record = state.GetType().GetMethod("RecordWeapon", BindingFlags.Public | BindingFlags.Instance);
        if (record == null)
            return;

        foreach (object weapon in weapons)
        {
            try { record.Invoke(state, [weapon]); }
            catch { /* ignore */ }
        }
    }

    private static string? FindRienLogPath()
    {
        Type? logger = ResolveRienType(RienFileLoggerTypeName);
        return logger?.GetProperty("LogPath", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
    }

    private static NCreature? FindCreatureNode(Creature creature) =>
        RienSceneLocator.FindCreatureNode(creature);

    private static Type? ResolveRienType(string fullName) =>
        AccessTools.TypeByName(fullName)
        ?? Type.GetType($"{fullName}, Rien", throwOnError: false)
        ?? AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(fullName, throwOnError: false))
            .FirstOrDefault(t => t != null);

    private static Node? FindNodeOfType(Node root, Type type)
    {
        if (type.IsInstanceOfType(root))
            return root;

        foreach (Node child in root.GetChildren())
        {
            Node? found = FindNodeOfType(child, type);
            if (found != null)
                return found;
        }

        return null;
    }

    private static float ReadScaleX(NCreature nCreature)
    {
        if (Math.Abs(nCreature.Scale.X) > 0.001f)
            return nCreature.Scale.X;
        if (nCreature.Visuals is Node2D visuals && Math.Abs(visuals.Scale.X) > 0.001f)
            return visuals.Scale.X;
        if (nCreature.Body is { } body && Math.Abs(body.Scale.X) > 0.001f)
            return body.Scale.X;
        return 0f;
    }

    private static void Record(List<RienCombatVerifyRunner.ScenarioResult> results, string name, bool? pass, string detail)
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
