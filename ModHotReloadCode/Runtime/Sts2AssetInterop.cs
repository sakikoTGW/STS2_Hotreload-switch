using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;

namespace ModHotReload.Runtime;

/// <summary>
/// 热重载后清理 STS2 PreloadManager / AssetCache 里该 mod 的陈旧与失败条目，
/// 避免 <c>res://ModId/...</c> 被永久标记为 failed（回主菜单 Preload 仍报错）。
/// </summary>
internal static class Sts2AssetInterop
{
    private static readonly Lazy<PreloadReflection> Ref = new(() => new PreloadReflection());

    internal static void PurgeModAssets(string modId)
    {
        string prefix = ModResourcePrefix(modId);
        try
        {
            var r = Ref.Value;
            if (!r.IsAvailable)
                return;

            int removed = r.PurgeByPrefix(prefix);
            if (removed > 0)
                MainFile.Logger.Info($"[热重载] AssetCache 已清理 {modId}: {removed} 项");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] AssetCache 清理 {modId} 失败: {ex.Message}");
        }
    }

    internal static void RefreshModelPreload()
    {
        try
        {
            MethodInfo? preload = typeof(ModelDb).GetMethod(
                "Preload",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            preload?.Invoke(null, null);
            MainFile.Logger.Info("[热重载] ModelDb.Preload 已刷新");
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            MainFile.Logger.Warn($"[热重载] ModelDb.Preload 刷新失败: {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] ModelDb.Preload 刷新失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 热重载后清理 mod 资源缓存。默认不调用 <see cref="RefreshModelPreload"/>——
    /// 全量 Preload 仅适合冷启动；回主菜单时游戏会自行跑 Deferred Preload。
    /// </summary>
    internal static void AfterModPayloadReload(string modId, bool refreshPreload = false)
    {
        PurgeModAssets(modId);
        if (refreshPreload)
            RefreshModelPreload();
    }

    internal static string ModResourcePrefix(string modId) =>
        $"res://{modId.Trim()}/";

    private sealed class PreloadReflection
    {
        private readonly Type? _preloadManager;
        private readonly PropertyInfo? _cacheProp;
        private readonly object? _cache;
        private readonly MethodInfo? _getKeys;
        private readonly MethodInfo? _unloadAssets;
        private readonly MethodInfo? _removeOne;
        private readonly FieldInfo? _failedAssets;
        private readonly FieldInfo? _missedAssets;

        internal bool IsAvailable => _cache != null;

        internal PreloadReflection()
        {
            _preloadManager = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("MegaCrit.Sts2.Core.Assets.PreloadManager"))
                .FirstOrDefault(t => t != null);

            if (_preloadManager == null)
                return;

            _cacheProp = _preloadManager.GetProperty("Cache", BindingFlags.Public | BindingFlags.Static);
            _cache = _cacheProp?.GetValue(null);
            if (_cache == null)
                return;

            Type cacheType = _cache.GetType();
            _getKeys = cacheType.GetMethod("GetCacheKeys", BindingFlags.Public | BindingFlags.Instance);
            _unloadAssets = cacheType.GetMethod(
                "UnloadAssets",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: [typeof(IEnumerable<string>)],
                modifiers: null);
            _removeOne = cacheType.GetMethod("RemoveAndGetResource", BindingFlags.Public | BindingFlags.Instance);
            _failedAssets = cacheType.GetField("_failedAssets", BindingFlags.NonPublic | BindingFlags.Instance);
            _missedAssets = cacheType.GetField("_missedCacheAssets", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        internal int PurgeByPrefix(string prefix)
        {
            if (_cache == null)
                return 0;

            int count = 0;
            var keys = CollectKeys(prefix).ToList();
            if (keys.Count > 0)
            {
                if (_unloadAssets != null)
                {
                    _unloadAssets.Invoke(_cache, [keys]);
                    count += keys.Count;
                }
                else if (_removeOne != null)
                {
                    foreach (string key in keys)
                    {
                        _removeOne.Invoke(_cache, [key]);
                        count++;
                    }
                }
            }

            count += PurgeHashSet(_failedAssets, prefix);
            count += PurgeHashSet(_missedAssets, prefix);
            return count;
        }

        private IEnumerable<string> CollectKeys(string prefix)
        {
            if (_getKeys == null || _cache == null)
                yield break;

            if (_getKeys.Invoke(_cache, null) is not IEnumerable raw)
                yield break;

            foreach (object? item in raw)
            {
                if (item is string s && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    yield return s;
            }
        }

        private int PurgeHashSet(FieldInfo? field, string prefix)
        {
            if (field?.GetValue(_cache) is not IEnumerable collection)
                return 0;

            var toRemove = new List<string>();
            foreach (object? item in collection)
            {
                if (item is string s && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    toRemove.Add(s);
            }

            if (toRemove.Count == 0)
                return 0;

            MethodInfo? remove = collection.GetType().GetMethod("Remove");
            if (remove == null)
                return 0;

            foreach (string s in toRemove)
                remove.Invoke(collection, [s]);
            return toRemove.Count;
        }
    }
}
