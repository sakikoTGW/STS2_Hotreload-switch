namespace ModHotReload.Runtime;

/// <summary>
/// 自动进入 Rien 战斗并写验证报告。触发：环境变量 STS2_MODHOTRELOAD_RIEN_COMBAT_VERIFY=1
/// 或 %LOCALAPPDATA%/STS2_ModHotReload/run-rien-combat-verify.flag
/// </summary>
internal static class RienCombatVerifyMode
{
    private static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "STS2_ModHotReload");

    internal static string FlagFile => Path.Combine(RootDir, "run-rien-combat-verify.flag");
    internal static string ResultsFile => Path.Combine(RootDir, "rien-combat-verify-results.json");
    internal static string ScreenshotFile => Path.Combine(RootDir, "rien-combat-verify.png");

    internal static bool IsRequested =>
        string.Equals(Environment.GetEnvironmentVariable("STS2_MODHOTRELOAD_RIEN_COMBAT_VERIFY"), "1", StringComparison.Ordinal)
        || File.Exists(FlagFile);

    internal static bool QuitWhenDone =>
        string.Equals(Environment.GetEnvironmentVariable("STS2_MODHOTRELOAD_RIEN_COMBAT_VERIFY_QUIT"), "1", StringComparison.Ordinal)
        || File.Exists(FlagFile);

    internal static void ClearFlag()
    {
        try
        {
            if (File.Exists(FlagFile))
                File.Delete(FlagFile);
        }
        catch
        {
            // ignore
        }
    }
}
