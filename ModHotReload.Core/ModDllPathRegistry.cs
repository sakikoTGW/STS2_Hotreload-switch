namespace ModHotReload.Core;

/// <summary>识别 mods 目录下的内容 mod DLL，供加载重定向使用。</summary>
public static class ModDllPathRegistry
{
    private static string? _modsRoot;
    private static readonly HashSet<string> ExcludedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModHotReload",
        "ModHotReload.Core",
        "ModHotReload.StartupHook",
        "sts2",
        "0Harmony",
        "GodotSharp",
        "GodotSharpEditor",
        "Steamworks.NET"
    };

    public static void Initialize(string? modsRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(modsRoot))
        {
            _modsRoot = Path.GetFullPath(modsRoot);
            return;
        }

        string? env = Environment.GetEnvironmentVariable("STS2_MODS_PATH");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
        {
            _modsRoot = Path.GetFullPath(env);
            return;
        }

        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "mods"),
            Path.Combine(baseDir, "..", "mods"),
            Path.Combine(baseDir, "..", "..", "mods"),
        ];

        foreach (string c in candidates)
        {
            string full = Path.GetFullPath(c);
            if (Directory.Exists(full))
            {
                _modsRoot = full;
                return;
            }
        }
    }

    public static bool TryGetModId(string assemblyPath, out string modId)
    {
        modId = "";
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return false;

        if (!assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return false;

        string full = Path.GetFullPath(assemblyPath);
        if (_modsRoot == null)
            Initialize();

        if (_modsRoot == null || !full.StartsWith(_modsRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        string fileName = Path.GetFileNameWithoutExtension(full);
        if (ExcludedAssemblyNames.Contains(fileName))
            return false;

        if (fileName.StartsWith("ModHotReload.", StringComparison.OrdinalIgnoreCase))
            return false;

        string? parent = Path.GetDirectoryName(full);
        if (parent == null)
            return false;

        string folderName = Path.GetFileName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(folderName))
            return false;

        modId = folderName;
        return true;
    }

    public static string? ModsRoot => _modsRoot;

    internal static bool TryGetModIdGuessFromName(string assemblyName, out string modId)
    {
        modId = assemblyName;
        if (assemblyName.Equals("ModHotReload", StringComparison.OrdinalIgnoreCase))
            return false;
        if (assemblyName.StartsWith("ModHotReload.", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
