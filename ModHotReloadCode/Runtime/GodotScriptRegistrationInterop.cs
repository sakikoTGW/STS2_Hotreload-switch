using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload.Runtime;

/// <summary>
/// Godot 4.5+ 使用 <c>ScriptManagerBridge._pathTypeBiMap</c>（非旧版 _pathScriptTypeBiMap）。
/// 热重载须先 <see cref="RemoveByScriptType"/> / 清路径，再 Lookup，否则 duplicate key。
/// </summary>
internal static class GodotScriptRegistrationInterop
{
    private static readonly object Sync = new();
    private static object? _pathTypeBiMap;
    private static MethodInfo? _biMapAdd;
    private static MethodInfo? _biMapTryGetScriptType;
    private static MethodInfo? _biMapRemoveByScriptType;
    private static MethodInfo? _lookupScriptsInAssembly;
    private static FieldInfo? _pathTypeMapField;
    private static FieldInfo? _typePathMapField;
    private static int _hotReloadDepth;

    private static readonly Dictionary<string, string[]> KnownGodotPathPrefixes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["BaseLib"] = ["BaseLibScenes", "BaseLib", "res://BaseLib"],
            ["Rien"] = ["Rien", "res://Rien"],
        };

    internal static bool InHotReloadScope => _hotReloadDepth > 0;

    internal static IDisposable BeginHotReloadScope()
    {
        Interlocked.Increment(ref _hotReloadDepth);
        return new HotReloadScope();
    }

    /// <summary>热重载前：按 mod 提示 + 旧程序集类型清 Godot 脚本表。</summary>
    internal static void PrepareForModReload(string modId, Assembly? previousAssembly)
    {
        EnsureBiMapResolved();
        if (_pathTypeBiMap == null)
            return;

        int removed = UnregisterByPathHints(modId, previousAssembly);
        if (previousAssembly != null)
            removed += UnregisterPathsFromAssembly(previousAssembly);

        if (removed > 0)
            MainFile.Logger.Info($"[热重载] 已清除 {removed} 条 Godot 脚本映射（{modId}）");
    }

    internal static void UnregisterPathsForMod(string modId, Assembly? previousAssembly) =>
        PrepareForModReload(modId, previousAssembly);

    internal static int UnregisterPathsFromAssembly(Assembly assembly)
    {
        EnsureBiMapResolved();
        if (_pathTypeBiMap == null || _biMapRemoveByScriptType == null)
            return 0;

        int removed = 0;
        foreach (Type type in SafeTypes(assembly))
        {
            if (type == null || type.IsAbstract || type.IsInterface)
                continue;

            if (!LooksLikeGodotScript(type))
                continue;

            try
            {
                _biMapRemoveByScriptType.Invoke(_pathTypeBiMap, [type]);
                removed++;
            }
            catch
            {
                // 类型可能从未注册
            }
        }

        return removed;
    }

    /// <summary>在 PathScriptTypeBiMap.Add 之前调用：若路径已存在则先移除。</summary>
    internal static void EnsurePathFreeBeforeAdd(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        EnsureBiMapResolved();
        if (_pathTypeBiMap == null)
            return;

        if (!TryGetTypeForPath(path, out Type? existing) || existing == null)
            return;

        try
        {
            _biMapRemoveByScriptType?.Invoke(_pathTypeBiMap, [existing]);
        }
        catch
        {
            RemovePathFromMaps(path);
        }
    }

    internal static void LookupScriptsInAssemblySafe(Assembly assembly)
    {
        EnsureBiMapResolved();
        if (_lookupScriptsInAssembly == null)
            return;

        UnregisterPathsFromAssembly(assembly);

        using (BeginHotReloadScope())
        {
            try
            {
                _lookupScriptsInAssembly.Invoke(null, [assembly]);
            }
            catch (TargetInvocationException ex) when (IsDuplicateScriptPath(ex.InnerException))
            {
                MainFile.Logger.Warn(
                    $"[热重载] LookupScriptsInAssembly 路径冲突（{assembly.GetName().Name}），已吞掉 duplicate key。");
            }
            catch (Exception ex) when (IsDuplicateScriptPath(ex))
            {
                MainFile.Logger.Warn($"[热重载] LookupScriptsInAssembly: {ex.Message}");
            }
        }
    }

    /// <summary>Initializer 内通常会自行 Lookup；避免 LoadMod 里重复 Lookup。</summary>
    internal static bool InitializerRegistersGodotScripts(Assembly assembly)
    {
        foreach (Type type in SafeTypes(assembly))
        {
            if (type.GetCustomAttribute(typeof(ModInitializerAttribute)) == null)
                continue;

            if (type.Name.Contains("Main", StringComparison.OrdinalIgnoreCase)
                || type.FullName?.Contains("BaseLib", StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        string? name = assembly.GetName().Name;
        return name != null
            && KnownGodotPathPrefixes.ContainsKey(name);
    }

    internal static bool IsDuplicateScriptPath(Exception? ex)
    {
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
        {
            if (cur is ArgumentException ae
                && ae.Message.Contains("same key", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int UnregisterByPathHints(string modId, Assembly? previousAssembly)
    {
        List<string> keys = CollectPathKeys();
        if (keys.Count == 0)
            return 0;

        HashSet<string> hints = BuildPathHints(modId, previousAssembly);
        int removed = 0;
        foreach (string key in keys)
        {
            if (!key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!hints.Any(h => key.Contains(h, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (TryRemovePath(key))
                removed++;
        }

        return removed;
    }

    private static bool TryRemovePath(string path)
    {
        if (!TryGetTypeForPath(path, out Type? type) || type == null)
            return RemovePathFromMaps(path);

        try
        {
            _biMapRemoveByScriptType?.Invoke(_pathTypeBiMap, [type]);
            return true;
        }
        catch
        {
            return RemovePathFromMaps(path);
        }
    }

    private static bool TryGetTypeForPath(string path, out Type? type)
    {
        type = null;
        if (_pathTypeBiMap == null || _biMapTryGetScriptType == null)
            return false;

        object?[] args = [path, null];
        bool ok = (bool)(_biMapTryGetScriptType.Invoke(_pathTypeBiMap, args) ?? false);
        type = args[1] as Type;
        return ok && type != null;
    }

    private static bool RemovePathFromMaps(string path)
    {
        if (_pathTypeBiMap == null || _pathTypeMapField == null)
            return false;

        object? pathMapObj = _pathTypeMapField.GetValue(_pathTypeBiMap);
        if (pathMapObj is not IDictionary pathMap || !pathMap.Contains(path))
            return false;

        object? typeObj = pathMap[path];
        pathMap.Remove(path);

        if (typeObj is Type t && _typePathMapField?.GetValue(_pathTypeBiMap) is IDictionary typeMap)
            typeMap.Remove(t);

        return true;
    }

    private static HashSet<string> BuildPathHints(string modId, Assembly? previousAssembly)
    {
        var hints = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { modId };

        string? asmName = previousAssembly?.GetName().Name;
        if (!string.IsNullOrEmpty(asmName))
        {
            hints.Add(asmName);
            if (KnownGodotPathPrefixes.TryGetValue(asmName, out string[]? extra))
            {
                foreach (string p in extra)
                    hints.Add(p);
            }
        }

        if (KnownGodotPathPrefixes.TryGetValue(modId, out string[]? modExtra))
        {
            foreach (string p in modExtra)
                hints.Add(p);
        }

        hints.Add($"{modId}Scenes");
        if (!string.IsNullOrEmpty(asmName))
            hints.Add($"{asmName}Scenes");

        return hints;
    }

    private static List<string> CollectPathKeys()
    {
        var keys = new List<string>();
        if (_pathTypeBiMap == null || _pathTypeMapField == null)
            return keys;

        if (_pathTypeMapField.GetValue(_pathTypeBiMap) is not IDictionary dict)
            return keys;

        foreach (object key in dict.Keys)
        {
            if (key is string s)
                keys.Add(s);
        }

        return keys;
    }

    private static bool LooksLikeGodotScript(Type type)
    {
        for (Type? cur = type; cur != null; cur = cur.BaseType)
        {
            if (cur.FullName?.StartsWith("Godot.", StringComparison.Ordinal) == true)
                return true;
        }

        return type.GetCustomAttributes(inherit: true)
            .Any(a => a.GetType().FullName?.Contains("Godot", StringComparison.Ordinal) == true);
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
    }

    private static void EnsureBiMapResolved()
    {
        if (_pathTypeBiMap != null)
            return;

        lock (Sync)
        {
            if (_pathTypeBiMap != null)
                return;

            Type? bridge = Type.GetType("Godot.Bridge.ScriptManagerBridge, GodotSharp");
            if (bridge == null)
                return;

            FieldInfo? field = bridge.GetField("_pathTypeBiMap", BindingFlags.Static | BindingFlags.NonPublic)
                ?? bridge.GetField("_pathScriptTypeBiMap", BindingFlags.Static | BindingFlags.NonPublic)
                ?? bridge.GetField("PathScriptTypeBiMap", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            _pathTypeBiMap = field?.GetValue(null);
            if (_pathTypeBiMap == null)
                return;

            Type biMapType = _pathTypeBiMap.GetType();
            _pathTypeMapField = biMapType.GetField("_pathTypeMap", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? biMapType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType.IsGenericType
                        && f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                        && f.Name.Contains("path", StringComparison.OrdinalIgnoreCase));

            _typePathMapField = biMapType.GetField("_typePathMap", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? biMapType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType.IsGenericType
                        && f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                        && f.Name.Contains("type", StringComparison.OrdinalIgnoreCase)
                        && !ReferenceEquals(f, _pathTypeMapField));

            _biMapAdd = biMapType.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _biMapTryGetScriptType = biMapType.GetMethod(
                "TryGetScriptType",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _biMapRemoveByScriptType = biMapType.GetMethod(
                "RemoveByScriptType",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                [typeof(Type)],
                null);

            _lookupScriptsInAssembly = bridge.GetMethod(
                "LookupScriptsInAssembly",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                [typeof(Assembly)],
                null);
        }
    }

    private sealed class HotReloadScope : IDisposable
    {
        public void Dispose()
        {
            if (_hotReloadDepth > 0)
                Interlocked.Decrement(ref _hotReloadDepth);
        }
    }

    internal sealed class NoOpDisposable : IDisposable
    {
        internal static readonly NoOpDisposable Instance = new();
        public void Dispose() { }
    }
}
