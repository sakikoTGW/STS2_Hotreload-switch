using Godot;

namespace ModHotReload.Runtime;

/// <summary>
/// Godot 4 公开 API 只有 LoadResourcePack，没有对应 unload。
/// 这里做可逆的“虚拟卸载”：关闭 mod 后阻断后续 res://ModId/... 的 ResourceLoader 访问，
/// 并配合 STS2/Godot 缓存清理让已缓存资源尽快失效。
/// </summary>
internal static class PckVirtualUnmountRegistry
{
    private static readonly HashSet<string> DisabledModIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    internal static void Disable(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            return;

        lock (Gate)
            DisabledModIds.Add(modId.Trim());

        Sts2AssetInterop.PurgeModAssets(modId);
        GodotResourceInterop.ClearResourceCache();
        MainFile.Logger.Info($"[热重载] PCK 虚拟卸载: res://{modId}/ 已对后续 ResourceLoader 访问隐藏。");
    }

    internal static void Enable(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            return;

        lock (Gate)
            DisabledModIds.Remove(modId.Trim());

        MainFile.Logger.Info($"[热重载] PCK 虚拟卸载解除: res://{modId}/");
    }

    internal static bool IsBlocked(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            return false;

        lock (Gate)
        {
            foreach (string modId in DisabledModIds)
            {
                string prefix = $"res://{modId}/";
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    internal static bool IsDirectoryBlocked(string? path) =>
        IsBlocked(NormalizeDirectoryProbe(path));

    private static string? NormalizeDirectoryProbe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        string normalized = path.Replace('\\', '/');
        return normalized.EndsWith("/", StringComparison.Ordinal)
            ? normalized + "__probe__"
            : normalized + "/__probe__";
    }
}
