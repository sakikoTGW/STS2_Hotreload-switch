using System.Reflection;
using ModHotReload.Core;

namespace ModHotReload.StartupHook;

/// <summary>
/// 由环境变量 DOTNET_STARTUP_HOOKS 指向本 DLL，在任意 mod 进 Default 之前安装 collectible 重定向。
/// </summary>
internal static class StartupHook
{
    private static readonly string BootLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "STS2_ModHotReload",
        "startup-hook.log");

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BootLog)!);
            File.AppendAllText(BootLog, $"[{DateTime.UtcNow:O}] StartupHook 开始\n");

            string? modsRoot = ResolveModsRoot();
            ModBootstrapEntry.Install(
                modsRoot,
                msg => File.AppendAllText(BootLog, msg + "\n"),
                msg => File.AppendAllText(BootLog, "[WARN] " + msg + "\n"));

            ModLoadSettingsGate.Install(
                msg => File.AppendAllText(BootLog, msg + "\n"),
                msg => File.AppendAllText(BootLog, "[WARN] " + msg + "\n"));
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(BootLog, $"[{DateTime.UtcNow:O}] StartupHook 失败: {ex}\n");
            }
            catch
            {
                // ignored
            }
        }
    }

    private static string? ResolveModsRoot()
    {
        string? env = Environment.GetEnvironmentVariable("STS2_MODS_PATH");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        string baseDir = AppContext.BaseDirectory;
        string hookDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? baseDir;
        string[] candidates =
        [
            Path.Combine(hookDir, ".."),
            Path.Combine(baseDir, "mods"),
            Path.Combine(baseDir, "..", "mods"),
        ];

        foreach (string c in candidates)
        {
            string mods = Path.GetFullPath(c);
            if (Path.GetFileName(mods).Equals("ModHotReload", StringComparison.OrdinalIgnoreCase))
                mods = Path.GetDirectoryName(mods) ?? mods;

            if (Directory.Exists(mods) && Directory.Exists(Path.Combine(mods, "ModHotReload")))
                return mods;
        }

        return null;
    }
}
