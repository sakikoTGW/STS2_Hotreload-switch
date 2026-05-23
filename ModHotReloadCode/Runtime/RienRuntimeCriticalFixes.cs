using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Sts2Logger = MegaCrit.Sts2.Core.Logging.Logger;

namespace ModHotReload.Runtime;

internal static class RienRuntimeCriticalFixes
{
    private const string RienSpriteTypeName = "Rien.RienCode.Presentation.RienLimbusSdSpritePlayer";
    private const string RienCardUiHelperTypeName = "Rien.RienCode.UI.RienCardUiHelper";
    private const string RienCommandSealFxTypeName = "Rien.RienCode.Presentation.RienCommandSealFx";
    private const string RienPowerBaseTypeName = "Rien.RienCode.Powers.RienPower";
    private const string RienAuraLayerTypeName = "Rien.RienCode.Presentation.RienCreatureAuraLayer";
    private const string RienStringExtensionsTypeName = "Rien.RienCode.Extensions.StringExtensions";

    private static readonly Harmony Harmony = new("ModHotReload.RienCriticalFixes");
    private static MethodInfo? _powerAtlasPathMethod;
    private static Sts2Logger? _logger;
    private static bool _installed;
    internal static bool IsInstalled => _installed;
    private static Assembly? _patchedRienAssembly;
    private static int _attempts;

    internal static void Install(Sts2Logger logger, Assembly? rienAssembly = null)
    {
        _logger = logger;
        TryInstall(rienAssembly);

        if (_installed || Engine.GetMainLoop() is not SceneTree tree)
            return;

        void RetryOnFrame()
        {
            if (_installed || ++_attempts > 600)
            {
                tree.ProcessFrame -= RetryOnFrame;
                if (!_installed)
                    _logger?.Warn("[Rien修复] 10 秒内未发现 Rien 程序集，跳过运行时补丁。");
                return;
            }

            TryInstall(rienAssembly);
            if (_installed)
                tree.ProcessFrame -= RetryOnFrame;
        }

        tree.ProcessFrame += RetryOnFrame;
    }

    private static void TryInstall(Assembly? rienAssembly = null)
    {
        Type? spriteType = ResolveRienType(rienAssembly, RienSpriteTypeName);
        if (_installed && spriteType?.Assembly == _patchedRienAssembly)
            return;

        Type? cardUiType = ResolveRienType(rienAssembly, RienCardUiHelperTypeName);
        Type? sealFxType = ResolveRienType(rienAssembly, RienCommandSealFxTypeName);
        Type? powerBaseType = ResolveRienType(rienAssembly, RienPowerBaseTypeName);
        Type? auraType = ResolveRienType(rienAssembly, RienAuraLayerTypeName);
        if (spriteType == null || cardUiType == null || sealFxType == null || powerBaseType == null)
            return;

        try
        {
            _powerAtlasPathMethod ??= AccessTools.Method(
                ResolveRienType(rienAssembly, RienStringExtensionsTypeName),
                "PowerAtlasPath",
                [typeof(string)]);

            Patch(spriteType, "WantsFaceRight", nameof(WantsFaceRightPrefix));
            Patch(spriteType, "EnsureUnder", nameof(EnsureUnderPrefix));
            Patch(spriteType, "ApplyLayout", postfix: nameof(ApplyLayoutPostfix));
            Patch(spriteType, "TryPlay", postfix: nameof(TryPlayPostfix));
            Patch(cardUiType, "PopulateOverlay", postfix: nameof(PopulateOverlayPostfix));
            Patch(sealFxType, "SyncObjectMarker", postfix: nameof(SyncObjectMarkerPostfix));
            Patch(powerBaseType, "get_CustomPackedIconPath", postfix: nameof(PowerIconPathPostfix));
            Patch(powerBaseType, "get_CustomBigIconPath", postfix: nameof(PowerIconPathPostfix));
            if (auraType != null)
                Patch(auraType, "SetMode", postfix: nameof(AuraSetModePostfix));
            _installed = true;
            _patchedRienAssembly = spriteType.Assembly;
            _logger?.Info("[Rien修复] 已安装：朝向/站位、SD/光环、卡牌局部UI、指令对象、Power图标。");
        }
        catch (Exception ex)
        {
            _logger?.Warn("[Rien修复] 安装失败: " + ex);
        }
    }

    private static Type? ResolveRienType(Assembly? rienAssembly, string fullName) =>
        rienAssembly?.GetType(fullName, throwOnError: false) ?? AccessTools.TypeByName(fullName);

    private static void Patch(Type type, string methodName, string? prefix = null, string? postfix = null)
    {
        MethodInfo? original = AccessTools.Method(type, methodName);
        if (original == null)
            throw new MissingMethodException(type.FullName, methodName);

        Harmony.Patch(
            original,
            prefix == null ? null : new HarmonyMethod(typeof(RienRuntimeCriticalFixes), prefix),
            postfix == null ? null : new HarmonyMethod(typeof(RienRuntimeCriticalFixes), postfix));
    }

    private static bool WantsFaceRightPrefix(object __0, ref bool __result)
    {
        if (__0 is not NCreature self)
            return true;

        if (TryResolveFacingByRelativePosition(self, out bool faceRight))
        {
            __result = faceRight;
            return false;
        }

        float scaleX = ReadScaleX(self);
        if (Math.Abs(scaleX) > 0.001f)
        {
            __result = scaleX > 0f;
            return false;
        }

        return true;
    }

    private static bool EnsureUnderPrefix(object __0, ref object? __result)
    {
        if (__0 is not NCreature nCreature)
            return true;

        Type? spriteType = AccessTools.TypeByName(RienSpriteTypeName);
        if (spriteType == null)
            return true;

        try
        {
            if (FindExistingSprite(nCreature, spriteType) is { } existing)
            {
                AccessTools.Method(spriteType, "ApplyLayout")?.Invoke(null, [nCreature]);
                __result = existing;
                return false;
            }

            Node parent = ResolveSdAnchor(nCreature, spriteType);
            Node sprite = (Node)Activator.CreateInstance(spriteType)!;
            sprite.Name = "RienLimbusSdAnim";
            if (sprite is CanvasItem canvas)
            {
                canvas.ZIndex = 4;
                canvas.ZAsRelative = true;
                canvas.Visible = true;
            }
            if (sprite is AnimatedSprite2D animated)
                animated.Centered = true;

            try
            {
                parent.AddChild(sprite);
            }
            catch (Exception ex) when (ex.Message.Contains("busy", StringComparison.OrdinalIgnoreCase))
            {
                parent.CallDeferred(Node.MethodName.AddChild, sprite);
                _logger?.Warn("[Rien修复] SD节点遇到 Parent busy，已改为 deferred AddChild。");
            }

            AccessTools.Method(spriteType, "ApplyLayout")?.Invoke(null, [nCreature]);
            __result = sprite;
            return false;
        }
        catch (Exception ex)
        {
            _logger?.Warn("[Rien修复] EnsureUnder 热修复失败，回退原逻辑: " + ex.Message);
            return true;
        }
    }

    private static void PopulateOverlayPostfix(object __0)
    {
        if (__0 is not Control root)
            return;

        root.ClipContents = false;
        Control? overlay = root.GetNodeOrNull<Control>("RienCardOverlay");
        if (overlay == null)
            return;

        overlay.TopLevel = false;
        overlay.ZAsRelative = true;
        overlay.ZIndex = 30;
        overlay.ClipContents = false;
        foreach (Node child in SnapshotChildren(overlay))
            NormalizeCardOverlayNode(child);
    }

    private static void ApplyLayoutPostfix(object __0)
    {
        if (__0 is not NCreature nCreature)
            return;

        Type? spriteType = AccessTools.TypeByName(RienSpriteTypeName);
        if (spriteType == null || FindExistingSprite(nCreature, spriteType) is not CanvasItem canvas)
            return;

        canvas.ZIndex = 4;
        canvas.ZAsRelative = true;
        canvas.Visible = true;
    }

    private static void TryPlayPostfix(object __0, ref bool __result)
    {
        if (!__result || __0 is not NCreature nCreature)
            return;

        Type? spriteType = AccessTools.TypeByName(RienSpriteTypeName);
        AccessTools.Method(spriteType, "ApplyLayout")?.Invoke(null, [nCreature]);
        if (FindExistingSprite(nCreature, spriteType!) is AnimatedSprite2D anim)
        {
            anim.Visible = true;
            anim.ZAsRelative = true;
        }
    }

    private static void AuraSetModePostfix(object __instance, bool heartActive, bool furiosoReady, bool blackNightmare)
    {
        if (__instance is not Node2D layer)
            return;

        layer.Visible = true;
        layer.ZAsRelative = true;
        layer.ZIndex = Math.Max(layer.ZIndex, 8);

        foreach (Node child in SnapshotChildren(layer))
        {
            if (child is CpuParticles2D particles)
            {
                particles.Emitting = heartActive || furiosoReady || blackNightmare;
                particles.Visible = particles.Emitting;
            }
            else if (child is CanvasItem item)
            {
                item.Visible = true;
            }
        }
    }

    private static void SyncObjectMarkerPostfix(object __0)
    {
        if (__0 is not Creature target)
            return;

        NCreature? nCreature = FindCreatureNode(target);
        Node2D? marker = nCreature?.GetNodeOrNull<Node2D>("RienDirectiveObjectMarker");
        if (marker == null)
            return;

        marker.Position = new Vector2(0f, -112f);
        marker.ZAsRelative = true;
        marker.ZIndex = 80;
        marker.Visible = true;
        foreach (Node child in SnapshotChildren(marker))
        {
            if (child is Sprite2D sprite)
            {
                sprite.Centered = true;
                sprite.Scale = new Vector2(0.82f, 0.82f);
                sprite.Modulate = new Color(1f, 0.93f, 0.35f, 1f);
            }
        }
    }

    private static void PowerIconPathPostfix(object __instance, ref string __result)
    {
        string? atlasKey = __instance.GetType().Name switch
        {
            "AgentHermesPower" => "agent_hermes_power",
            "DirectiveObjectPower" => "directive_object",
            "DirectiveAegisPower" => "directive_aegis",
            "TerminalDirectivePower" => "terminal_directive_power",
            "HeartLienPower" => "heart_lien_power",
            "LienMaskPower" => "lien_mask_power",
            "BurningWoundRienPower" => "burning_wound_rien_power",
            "KarmaFortunaPower" => "karma_fortuna_power",
            _ => null
        };

        string? resolved = atlasKey == null ? null : ResolvePowerAtlasPath(atlasKey);
        if (!string.IsNullOrEmpty(resolved))
            __result = resolved;
        else if (string.IsNullOrEmpty(__result) || !__result.StartsWith("res://Rien/", StringComparison.OrdinalIgnoreCase))
            __result = $"res://Rien/images/powers/{atlasKey?.Replace("_power", "", StringComparison.OrdinalIgnoreCase)}.png";
    }

    private static string? ResolvePowerAtlasPath(string atlasKey)
    {
        if (_powerAtlasPathMethod == null)
            return null;

        try
        {
            return _powerAtlasPathMethod.Invoke(null, [atlasKey]) as string;
        }
        catch
        {
            return null;
        }
    }

    private static void NormalizeCardOverlayNode(Node node)
    {
        if (node is CanvasItem item)
        {
            item.ZAsRelative = true;
            item.ZIndex = Math.Max(item.ZIndex, 31);
        }

        if (node is Control control)
        {
            control.TopLevel = false;
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
            control.ClipContents = false;
            if (control is TextureRect textureRect)
            {
                textureRect.CustomMinimumSize = new Vector2(58f, 58f);
                textureRect.OffsetLeft = -64f;
                textureRect.OffsetRight = -6f;
                textureRect.OffsetTop = 6f;
                textureRect.OffsetBottom = 64f;
            }
            else if (control.CustomMinimumSize.X is > 0f and < 42f)
            {
                control.CustomMinimumSize = new Vector2(46f, 46f);
            }
        }

        foreach (Node child in SnapshotChildren(node))
            NormalizeCardOverlayNode(child);
    }

    private static Node[] SnapshotChildren(Node parent)
    {
        var snapshot = new List<Node>();
        foreach (Node child in parent.GetChildren())
            snapshot.Add(child);
        return snapshot.ToArray();
    }

    private static bool TryResolveFacingByRelativePosition(NCreature self, out bool faceRight)
    {
        faceRight = true;
        Creature? entity = self.Entity;
        Creature? target = ResolveFacingTarget(entity);
        NCreature? targetNode = target == null ? null : FindCreatureNode(target);
        if (targetNode == null)
            return false;

        float selfX = ((Control)self).GlobalPosition.X;
        float targetX = ((Control)targetNode).GlobalPosition.X;
        if (Math.Abs(targetX - selfX) < 1f)
            return false;

        faceRight = targetX > selfX;
        return true;
    }

    private static Creature? ResolveFacingTarget(Creature? entity)
    {
        if (entity == null)
            return null;

        if (entity.IsPlayer)
            return entity.CombatState?.HittableEnemies.FirstOrDefault(e => e.IsAlive);

        Creature? playerCreature = entity.Player?.Creature;
        if (playerCreature?.IsAlive == true)
            return playerCreature;

        return null;
    }

    private static float ReadScaleX(NCreature nCreature)
    {
        if (Math.Abs(((Control)nCreature).Scale.X) > 0.001f)
            return ((Control)nCreature).Scale.X;
        if (nCreature.Visuals is Node2D visuals && Math.Abs(visuals.Scale.X) > 0.001f)
            return visuals.Scale.X;
        if (nCreature.Body is { } body && Math.Abs(body.Scale.X) > 0.001f)
            return body.Scale.X;
        return 0f;
    }

    private static Node? FindExistingSprite(NCreature nCreature, Type spriteType)
    {
        foreach (Node child in SnapshotChildren((Node)nCreature))
        {
            if (spriteType.IsInstanceOfType(child))
                return child;
            Node? nested = FindExistingSpriteRecursive(child, spriteType);
            if (nested != null)
                return nested;
        }
        return null;
    }

    private static Node? FindExistingSpriteRecursive(Node parent, Type spriteType)
    {
        foreach (Node child in SnapshotChildren(parent))
        {
            if (spriteType.IsInstanceOfType(child))
                return child;
            Node? nested = FindExistingSpriteRecursive(child, spriteType);
            if (nested != null)
                return nested;
        }
        return null;
    }

    private static Node ResolveSdAnchor(NCreature nCreature, Type spriteType)
    {
        try
        {
            if (AccessTools.Method(spriteType, "ResolveSdAnchor")?.Invoke(null, [nCreature]) is Node anchor)
                return anchor;
        }
        catch
        {
            // Fall through to a stable creature-local parent.
        }

        return ((Node)nCreature).GetNodeOrNull("DefaultEffectPivot") ?? nCreature;
    }

    private static NCreature? FindCreatureNode(Creature creature) =>
        RienSceneLocator.FindCreatureNode(creature);
}
