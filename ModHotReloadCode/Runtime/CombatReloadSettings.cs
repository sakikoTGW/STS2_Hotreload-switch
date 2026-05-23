namespace ModHotReload.Runtime;

/// <summary>
/// 战斗内 DLL 重载策略。默认立即重载；设环境变量 STS2_MODHOTRELOAD_COMBAT_DEFER=1
/// 或创建 %LOCALAPPDATA%/STS2_ModHotReload/combat-defer.flag 可恢复旧行为（排队至战斗结束）。
/// </summary>
internal static class CombatReloadSettings
{
    private static bool? _testDeferOverride;
    private static bool? _testSlOverride;

    internal static bool DeferDllReloadUntilCombatEnds
    {
        get
        {
            if (_testDeferOverride.HasValue)
                return _testDeferOverride.Value;

            string? env = Environment.GetEnvironmentVariable("STS2_MODHOTRELOAD_COMBAT_DEFER");
            if (string.Equals(env, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase))
                return true;

            string flag = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "STS2_ModHotReload",
                "combat-defer.flag");

            return File.Exists(flag);
        }
    }

    /// <summary>战斗内 DLL：保存→主菜单→重载→继续（默认开）。</summary>
    internal static bool UseSaveLoadReloadInCombat
    {
        get
        {
            if (_testSlOverride.HasValue)
                return _testSlOverride.Value;
            // 强制策略：战斗内 DLL 热重载恒定走 SL 管道（保存→主菜单→重载→继续）。
            // 不再允许通过环境变量或 flag 关闭，避免回退到不稳定的战斗内即时重载。
            return true;
        }
    }

    internal static void SetDeferOverrideForTests(bool? defer) => _testDeferOverride = defer;

    internal static void SetSlOverrideForTests(bool? sl) => _testSlOverride = sl;
}
