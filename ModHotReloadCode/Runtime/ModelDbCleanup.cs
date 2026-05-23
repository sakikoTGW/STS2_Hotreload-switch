using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;

namespace ModHotReload.Runtime;

/// <summary>
/// 官方 ModelDb.Remove(Type) + 扫 _contentById + 清空 _all* 缓存。
/// 对 mods 下任意 mod 热重载生效，避免重复卡/遗物/角色与陈旧列表。
/// </summary>
internal static class ModelDbCleanup
{
    private static readonly FieldInfo? ContentByIdField = typeof(ModelDb).GetField(
        "_contentById",
        BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly FieldInfo[] ListCacheFields = typeof(ModelDb)
        .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
        .Where(f =>
            f.Name.StartsWith("_all", StringComparison.Ordinal) ||
            f.Name.Equals("_achievements", StringComparison.Ordinal))
        .ToArray();

    internal static int RemoveAssemblyModels(Assembly? assembly) =>
        RemoveModModels(assembly?.GetName().Name ?? "", assembly);

    /// <summary>关闭 mod：卸程序集类型 + 按 ModelId 前缀扫 _contentById，并重建 netId 表。</summary>
    internal static int RemoveModModels(string modId, Assembly? assembly)
    {
        int removed = 0;
        if (assembly != null)
        {
            foreach (Type type in SafeGetTypes(assembly))
            {
                if (!ModelDbTypeFilter.IsLikelyModelType(type))
                    continue;

                try
                {
                    if (!ModelDb.Contains(type))
                        continue;

                    ModelDb.Remove(type);
                    removed++;
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Warn($"[热重载] ModelDb.Remove({type.Name}): {ex.Message}");
                }
            }

            removed += PurgeContentByIdForAssembly(assembly);
        }

        int byId = PurgeContentByModId(modId);
        InvalidateListCaches();

        if (removed > 0 || byId > 0)
        {
            try
            {
                ModelIdSerializationCacheInterop.RefreshFromModelDb();
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[热重载] 关闭 {modId} 后重建 ModelIdSerializationCache: {ex.Message}");
            }

            MainFile.Logger.Info(
                $"[热重载] ModelDb 清理 {modId}: Remove={removed}, 前缀扫表={byId}");
        }

        return removed + byId;
    }

    private static int PurgeContentByModId(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId) || ContentByIdField?.GetValue(null) is not IDictionary dict)
            return 0;

        string upper = modId.ToUpperInvariant();
        var keysToRemove = new List<object>();
        foreach (DictionaryEntry entry in dict)
        {
            string keyText = entry.Key?.ToString() ?? "";
            if (keyText.Contains(modId, StringComparison.OrdinalIgnoreCase)
                || keyText.Contains(upper, StringComparison.Ordinal))
                keysToRemove.Add(entry.Key);
        }

        foreach (object key in keysToRemove)
        {
            try
            {
                dict.Remove(key);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[热重载] _contentById.Remove({key}): {ex.Message}");
            }
        }

        return keysToRemove.Count;
    }

    /// <summary>热重载加载新程序集后，把其中的 AbstractModel 子类重新注入 ModelDb。</summary>
    internal static int InjectAssemblyModels(Assembly? assembly)
    {
        if (assembly == null)
            return 0;

        int injected = 0;
        foreach (Type type in SafeGetTypes(assembly))
        {
            if (!ModelDbTypeFilter.IsLikelyModelType(type))
                continue;

            try
            {
                if (ModelDb.Contains(type))
                    continue;

                ModelDb.Inject(type);
                injected++;
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[热重载] ModelDb.Inject({type.Name}): {ex.Message}");
            }
        }

        if (injected > 0)
        {
            ModelIdSerializationCacheInterop.RefreshFromModelDb();
            ModelDb.InitIds();
            InvalidateListCaches();
            MainFile.Logger.Info($"[热重载] ModelDb 注入 {assembly.GetName().Name}: {injected}");
        }

        return injected;
    }

    /// <summary>按程序集扫 _contentById，卸掉 Remove(Type) 漏掉的条目（同 ModelId 换程序集时）。</summary>
    private static int PurgeContentByIdForAssembly(Assembly assembly)
    {
        if (ContentByIdField?.GetValue(null) is not IDictionary dict)
            return 0;

        var keysToRemove = new List<object>();
        foreach (DictionaryEntry entry in dict)
        {
            object? value = entry.Value;
            if (value == null)
                continue;

            Type valueType = value.GetType();
            if (valueType.Assembly == assembly)
                keysToRemove.Add(entry.Key);
        }

        foreach (object key in keysToRemove)
        {
            try
            {
                dict.Remove(key);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[热重载] _contentById.Remove: {ex.Message}");
            }
        }

        return keysToRemove.Count;
    }

    internal static void InvalidateListCaches()
    {
        foreach (FieldInfo field in ListCacheFields)
        {
            try
            {
                if (field.GetValue(null) != null)
                    field.SetValue(null, null);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[热重载] 清空 ModelDb.{field.Name}: {ex.Message}");
            }
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
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
}
