namespace ModHotReload.Runtime;

/// <summary>ModuleInitializer / StartupHook 阶段使用，不触发 MainFile.Logger（依赖 Godot）。</summary>
internal static class EarlyLog
{
    internal static void Info(string msg) =>
        TryMainFileLogger(() => MainFile.Logger.Info(msg), msg);

    internal static void Warn(string msg) =>
        TryMainFileLogger(() => MainFile.Logger.Warn(msg), "[WARN] " + msg);

    private static void TryMainFileLogger(Action write, string fallback)
    {
        try
        {
            write();
        }
        catch
        {
            Console.WriteLine(fallback);
        }
    }
}
