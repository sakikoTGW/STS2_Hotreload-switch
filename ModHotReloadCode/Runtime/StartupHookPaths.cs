namespace ModHotReload.Runtime;

internal static class StartupHookPaths
{
    internal static string? ResolveHookDllPath()
    {
        string? dir = Path.GetDirectoryName(typeof(ModSatelliteAssemblyLoader).Assembly.Location);
        if (string.IsNullOrEmpty(dir))
            return null;

        string path = Path.Combine(dir, "ModHotReload.StartupHook.dll");
        return File.Exists(path) ? path : null;
    }
}
