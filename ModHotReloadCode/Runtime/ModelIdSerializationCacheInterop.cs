using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace ModHotReload.Runtime;

/// <summary>
/// 运行期向 ModelDb 注入新 Model 后，必须重建静态 netId 映射表，否则 InitIds 会抛
/// <c>ModelId entry … could not be mapped to any net ID</c>。
/// </summary>
internal static class ModelIdSerializationCacheInterop
{
    private static readonly FieldInfo[] CacheMapFields = typeof(ModelIdSerializationCache)
        .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
        .Where(f =>
            f.FieldType.Name.Contains("Dictionary", StringComparison.Ordinal)
            || f.FieldType.Name.Contains("List`1", StringComparison.Ordinal))
        .ToArray();

    internal static void RefreshFromModelDb()
    {
        try
        {
            ClearStaticMaps();
            ModelIdSerializationCache.Init();
            MainFile.Logger.Info(
                $"[热重载] ModelIdSerializationCache 已重建（Entries≈{ModelIdSerializationCache.MaxEntryId} Hash={ModelIdSerializationCache.Hash}）");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[热重载] ModelIdSerializationCache.Init 失败: {ex}");
            throw;
        }
    }

    private static void ClearStaticMaps()
    {
        foreach (FieldInfo field in CacheMapFields)
        {
            try
            {
                object? value = field.GetValue(null);
                switch (value)
                {
                    case IDictionary dict:
                        dict.Clear();
                        break;
                    case IList list:
                        list.Clear();
                        break;
                }
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[热重载] 清空 ModelIdSerializationCache.{field.Name}: {ex.Message}");
            }
        }
    }
}
