namespace ModHotReload.Runtime;

/// <summary>
/// 由环境变量或 %LOCALAPPDATA%/STS2_ModHotReload/run-itest.flag 触发；
/// 结果写入 itest-results.json，供 scripts/run-integration-test.ps1 解析。
/// </summary>
internal static class IntegrationTestMode
{
    private static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "STS2_ModHotReload");

    internal static string FlagFile => Path.Combine(RootDir, "run-itest.flag");
    internal static string ResultsFile => Path.Combine(RootDir, "itest-results.json");

    internal static bool IsRequested =>
        string.Equals(System.Environment.GetEnvironmentVariable("STS2_MODHOTRELOAD_ITEST"), "1", StringComparison.Ordinal)
        || File.Exists(FlagFile);

    internal static bool QuitWhenDone =>
        string.Equals(System.Environment.GetEnvironmentVariable("STS2_MODHOTRELOAD_ITEST_QUIT"), "1", StringComparison.Ordinal)
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
