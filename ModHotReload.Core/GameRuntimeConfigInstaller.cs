using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModHotReload.Core;

/// <summary>
/// 将 StartupHook 写入游戏 sts2.runtimeconfig.json，Steam 正常启动即可生效（无需 DOTNET_STARTUP_HOOKS）。
/// </summary>
public static class GameRuntimeConfigInstaller
{
    public static bool EnsureStartupHookInstalled(string startupHookDllPath, Action<string>? log = null, Action<string>? warn = null)
    {
        try
        {
            startupHookDllPath = Path.GetFullPath(startupHookDllPath);
            if (!File.Exists(startupHookDllPath))
            {
                warn?.Invoke($"[ModHotReload] StartupHook 不存在: {startupHookDllPath}");
                return false;
            }

            string? configPath = FindRuntimeConfigPath();
            if (configPath == null)
            {
                warn?.Invoke("[ModHotReload] 未找到 sts2.runtimeconfig.json，无法自动注册 startupHooks");
                return false;
            }

            if (!NeedsInstall(startupHookDllPath, configPath))
            {
                log?.Invoke("[ModHotReload] sts2.runtimeconfig.json 已含 StartupHook（照常启动即可）");
                return true;
            }

            string json = File.ReadAllText(configPath);
            JsonNode? root = JsonNode.Parse(json);
            if (root?["runtimeOptions"] is not JsonObject runtimeOptions)
            {
                warn?.Invoke("[ModHotReload] runtimeconfig 格式异常，跳过 startupHooks");
                return false;
            }

            string hookEntry = ToConfigPath(configPath, startupHookDllPath);
            var hooks = runtimeOptions["startupHooks"] as JsonArray ?? new JsonArray();
            runtimeOptions["startupHooks"] = hooks;

            foreach (JsonNode? item in hooks)
            {
                string? existing = item?.GetValue<string>();
                if (existing != null && PathsEqual(existing, hookEntry, startupHookDllPath))
                {
                    log?.Invoke("[ModHotReload] startupHooks 已存在，无需写入");
                    return true;
                }
            }

            hooks.Add(hookEntry);

            string backup = configPath + ".bak";
            if (!File.Exists(backup))
                File.Copy(configPath, backup, overwrite: false);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, root.ToJsonString(options));
            log?.Invoke("[ModHotReload] 已写入游戏 startupHooks；请完全退出并重新启动一次，之后从 Steam 照常启动即可。");
            return true;
        }
        catch (Exception ex)
        {
            warn?.Invoke($"[ModHotReload] 写入 runtimeconfig 失败: {ex.Message}");
            return false;
        }
    }

    private static bool NeedsInstall(string hookDll, string configPath)
    {
        if (!File.Exists(configPath))
            return true;

        try
        {
            string hookEntry = ToConfigPath(configPath, hookDll);
            JsonNode? root = JsonNode.Parse(File.ReadAllText(configPath));
            if (root?["runtimeOptions"]?["startupHooks"] is not JsonArray hooks)
                return true;

            foreach (JsonNode? item in hooks)
            {
                string? existing = item?.GetValue<string>();
                if (existing != null && PathsEqual(existing, hookEntry, hookDll))
                    return false;
            }

            return true;
        }
        catch
        {
            return true;
        }
    }

    internal static string? FindRuntimeConfigPath()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "sts2.runtimeconfig.json"),
            Path.Combine(baseDir, "data_sts2_windows_x86_64", "sts2.runtimeconfig.json"),
            Path.Combine(baseDir, "..", "data_sts2_windows_x86_64", "sts2.runtimeconfig.json"),
        ];

        foreach (string c in candidates)
        {
            string full = Path.GetFullPath(c);
            if (File.Exists(full))
                return full;
        }

        return null;
    }

    private static string ToConfigPath(string configPath, string hookDll) =>
        Path.GetRelativePath(Path.GetDirectoryName(configPath)!, hookDll);

    private static bool PathsEqual(string a, string b, string hookDllFull)
    {
        try
        {
            string fullA = Path.IsPathRooted(a) ? Path.GetFullPath(a) : a;
            string fullB = Path.IsPathRooted(b) ? Path.GetFullPath(b) : b;
            if (string.Equals(fullA, fullB, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullA, hookDllFull, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullB, hookDllFull, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // ignored
        }

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
