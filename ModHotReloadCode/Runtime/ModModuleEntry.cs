using System.Runtime.CompilerServices;

namespace ModHotReload.Runtime;

/// <summary>本程序集唯一的 ModuleInitializer，保证先加载卫星 DLL 再跑其它早期逻辑。</summary>
internal static class ModModuleEntry
{
    [ModuleInitializer]
    internal static void Run()
    {
        ModSatelliteAssemblyLoader.EnsureLoaded();
        string? hookDll = StartupHookPaths.ResolveHookDllPath();
        if (hookDll != null)
            InvokeCoreRuntimeConfigInstaller(hookDll);
        IntegrationTestBootstrap.RunIfRequested();
        ModHotReloadEarlyBootstrap.InstallTryLoadModPatch();
    }

    private static void InvokeCoreRuntimeConfigInstaller(string hookDll)
    {
        Type? installer = Type.GetType("ModHotReload.Core.GameRuntimeConfigInstaller, ModHotReload.Core", throwOnError: false);
        installer?.GetMethod("EnsureStartupHookInstalled", [
            typeof(string),
            typeof(Action<string>),
            typeof(Action<string>)
        ])?.Invoke(null, [hookDll, (Action<string>)EarlyLog.Info, (Action<string>)EarlyLog.Warn]);
    }
}
