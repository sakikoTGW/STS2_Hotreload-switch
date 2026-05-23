using System.Reflection;

namespace ModHotReload.Runtime;

/// <summary>
/// 在引用 ModHotReload.Core 之前，从 mod 部署目录显式加载卫星程序集。
/// STS2 的 mod 加载上下文不会自动解析同目录依赖。
/// </summary>
internal static class ModSatelliteAssemblyLoader
{
    private static readonly string[] SatelliteDlls = ["ModHotReload.Core.dll"];
    private static bool _resolveHooked;

    internal static void EnsureLoaded()
    {
        if (!_resolveHooked)
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            _resolveHooked = true;
        }

        string? dir = GetModDeployDirectory();
        if (dir == null)
            return;

        foreach (string fileName in SatelliteDlls)
        {
            string path = Path.Combine(dir, fileName);
            if (!File.Exists(path))
                continue;

            string simpleName = Path.GetFileNameWithoutExtension(fileName);
            if (IsLoaded(simpleName))
                continue;

            try
            {
                Assembly.LoadFrom(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ModHotReload] 无法加载 {path}: {ex.Message}");
            }
        }
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        string? requested = new AssemblyName(args.Name).Name;
        if (requested == null || !requested.StartsWith("ModHotReload.", StringComparison.Ordinal))
            return null;

        string? dir = GetModDeployDirectory();
        if (dir == null)
            return null;

        string path = Path.Combine(dir, requested + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    private static bool IsLoaded(string simpleName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => string.Equals(a.GetName().Name, simpleName, StringComparison.Ordinal));

    private static string? GetModDeployDirectory()
    {
        string? loc = typeof(ModSatelliteAssemblyLoader).Assembly.Location;
        if (!string.IsNullOrEmpty(loc))
        {
            string? dir = Path.GetDirectoryName(loc);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                return dir;
        }

        string? envMods = Environment.GetEnvironmentVariable("STS2_MODS_PATH");
        if (!string.IsNullOrWhiteSpace(envMods))
        {
            string hotReload = Path.Combine(Path.GetFullPath(envMods), "ModHotReload");
            if (Directory.Exists(hotReload))
                return hotReload;
        }

        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "mods", "ModHotReload"),
            Path.Combine(baseDir, "..", "mods", "ModHotReload"),
            Path.Combine(baseDir, "..", "..", "mods", "ModHotReload"),
        ];

        foreach (string c in candidates)
        {
            string full = Path.GetFullPath(c);
            if (Directory.Exists(full))
                return full;
        }

        return null;
    }
}
