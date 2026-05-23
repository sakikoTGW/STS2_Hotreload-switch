using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace ModHotReload.Core;

/// <summary>所有内容 mod DLL 唯一入口：collectible ALC，可 Unload 后 GC 释放。</summary>
public static class ModCollectibleHost
{
    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object QuarantineSync = new();
    private static Action<string>? _logInfo;
    private static Action<string>? _logWarn;

    public static void SetLoggers(Action<string>? info, Action<string>? warn)
    {
        _logInfo = info;
        _logWarn = warn;
    }

    public static void RegisterDefaultAlcQuarantine()
    {
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
    }

    public static bool TryGetLoaded(string modId, out Assembly? assembly)
    {
        if (Entries.TryGetValue(modId, out Entry? e) && e.Assembly != null)
        {
            assembly = e.Assembly;
            return true;
        }

        assembly = null;
        return false;
    }

    public static Assembly GetOrLoad(string modId, string assemblyPath, params string[] probeDirs)
    {
        string full = Path.GetFullPath(assemblyPath);
        if (Entries.TryGetValue(modId, out Entry? existing)
            && existing.Assembly != null
            && string.Equals(existing.SourcePath, full, StringComparison.OrdinalIgnoreCase))
            return existing.Assembly;

        return Reload(modId, full, probeDirs);
    }

    public static Assembly Reload(string modId, string assemblyPath, params string[] probeDirs)
    {
        Unload(modId);

        var ctx = new CollectibleModLoadContext(modId, assemblyPath, probeDirs);
        Assembly? asm = TryLoadMain(ctx, assemblyPath);
        if (asm == null)
            throw new InvalidOperationException($"collectible 加载失败: {assemblyPath}");

        var entry = new Entry(ctx, asm, Path.GetFullPath(assemblyPath));
        Entries[modId] = entry;
        LogInfo($"[ALC] {modId} → collectible ({asm.GetName().Version})");
        return asm;
    }

    public static void Unload(string modId)
    {
        if (!Entries.TryRemove(modId, out Entry? entry))
            return;

        try
        {
            entry.Context.Unload();
            for (int i = 0; i < 5; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            if (entry.Weak.IsAlive)
                LogWarn($"[ALC] {modId} collectible 仍被引用，可能有 Godot/静态事件持有旧类型。");
            else
                LogInfo($"[ALC] {modId} collectible 已卸载。");
        }
        catch (Exception ex)
        {
            LogWarn($"[ALC] 卸载 {modId}: {ex.Message}");
        }
    }

    public static bool IsCollectible(Assembly? assembly)
    {
        if (assembly == null)
            return false;
        AssemblyLoadContext? ctx = AssemblyLoadContext.GetLoadContext(assembly);
        return ctx != null && ctx != AssemblyLoadContext.Default && ctx.IsCollectible;
    }

    public static bool IsDefault(Assembly? assembly) =>
        assembly != null && AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default;

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        Assembly asm = args.LoadedAssembly;
        if (!IsDefault(asm))
            return;

        string? name = asm.GetName().Name;
        if (string.IsNullOrEmpty(name) || !ModDllPathRegistry.TryGetModIdGuessFromName(name, out string modId))
            return;

        lock (QuarantineSync)
        {
            if (Entries.ContainsKey(modId) && IsCollectible(Entries[modId].Assembly))
                return;
        }

        LogWarn($"[ALC] 检测到 {modId} 误入 Default ALC，将尝试迁入 collectible…");
        TryQuarantineMigrate(modId, asm);
    }

    private static void TryQuarantineMigrate(string modId, Assembly defaultAsm)
    {
        try
        {
            string? path = defaultAsm.Location;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            Assembly collectible = Reload(modId, path, Path.GetDirectoryName(path) ?? "");
            LogInfo($"[ALC] {modId} 已从 Default 迁入 collectible（Default 副本无法卸载，但已不再使用 {collectible.GetName().Name}）");
        }
        catch (Exception ex)
        {
            LogWarn($"[ALC] {modId} Default 迁入失败: {ex.Message}");
        }
    }

    private static Assembly? TryLoadMain(CollectibleModLoadContext ctx, string path)
    {
        try
        {
            return ctx.LoadFromAssemblyPath(path);
        }
        catch (Exception ex1)
        {
            LogWarn($"[ALC] LoadFromAssemblyPath: {ex1.Message}");
        }

        try
        {
            return ctx.LoadFromStream(new MemoryStream(File.ReadAllBytes(path)));
        }
        catch (Exception ex2)
        {
            LogWarn($"[ALC] LoadFromStream: {ex2.Message}");
            return null;
        }
    }

    private static void LogInfo(string msg) => _logInfo?.Invoke(msg);
    private static void LogWarn(string msg) => _logWarn?.Invoke(msg);

    private sealed class Entry(CollectibleModLoadContext context, Assembly assembly, string sourcePath)
    {
        internal CollectibleModLoadContext Context { get; } = context;
        internal Assembly Assembly { get; } = assembly;
        internal string SourcePath { get; } = sourcePath;
        internal WeakReference Weak { get; } = new(context, trackResurrection: false);
    }

    private sealed class CollectibleModLoadContext : AssemblyLoadContext
    {
        private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
        {
            "sts2", "0Harmony", "GodotSharp", "GodotSharpEditor", "Steamworks.NET", "BaseLib", "ModHotReload"
        };

        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _modId;
        private readonly string _mainName;
        private readonly string[] _probeDirs;

        internal CollectibleModLoadContext(string modId, string mainPath, params string[] probeDirs)
            : base($"ModHotReload:{modId}:{DateTime.UtcNow.Ticks}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(mainPath);
            _modId = modId;
            _mainName = AssemblyName.GetAssemblyName(mainPath).Name ?? modId;
            _probeDirs = probeDirs.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? name = assemblyName.Name;
            if (string.IsNullOrEmpty(name))
                return null;

            if (!name.Equals(_mainName, StringComparison.OrdinalIgnoreCase)
                && (Shared.Contains(name) || name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)))
            {
                Assembly? shared = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(a => GetLoadContext(a) != Default)
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
                if (shared != null)
                    return shared;
            }

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path != null)
                return LoadFromAssemblyPath(path);

            foreach (string dir in _probeDirs)
            {
                string probe = Path.Combine(dir, name + ".dll");
                if (File.Exists(probe))
                    return LoadFromAssemblyPath(probe);
            }

            return null;
        }
    }
}
