using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload.Runtime;

/// <summary>
/// 解析 mod 的 DLL/PCK/JSON 路径：支持 mods/{id}/、mods/{id}.pck 等常见布局（GitHub 第三方 mod 混用）。
/// </summary>
internal static class ModPayloadPaths
{
    internal static string? GetModsRoot(Mod mod)
    {
        if (string.IsNullOrEmpty(mod.path))
            return null;

        string full = Path.GetFullPath(mod.path);
        if (Directory.Exists(Path.Combine(full, MainFile.ModId)))
            return full;

        string? parent = Directory.GetParent(full)?.FullName;
        if (parent != null && Directory.Exists(Path.Combine(parent, MainFile.ModId)))
            return parent;

        return parent ?? full;
    }

    /// <summary>按优先级列出 live 候选路径（去重，忽略大小写）。</summary>
    internal static IEnumerable<string> EnumerateLiveCandidates(Mod mod, string fileName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? modId = mod.manifest?.id;
        string ext = Path.GetExtension(fileName);

        foreach (string? raw in CollectCandidates(mod, modId, fileName, ext))
        {
            if (string.IsNullOrEmpty(raw))
                continue;
            string full = Path.GetFullPath(raw);
            if (seen.Add(full))
                yield return full;
        }
    }

    internal static string? ResolveFirstExisting(Mod mod, string fileName)
    {
        foreach (string path in EnumerateLiveCandidates(mod, fileName))
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    internal static bool Exists(Mod mod, string fileName) =>
        ResolveFirstExisting(mod, fileName) != null;

    private static IEnumerable<string?> CollectCandidates(Mod mod, string? modId, string fileName, string ext)
    {
        if (!string.IsNullOrEmpty(mod.path))
        {
            yield return Path.Combine(mod.path, fileName);
            if (modId != null && !fileName.Equals(modId + ext, StringComparison.OrdinalIgnoreCase))
                yield return Path.Combine(mod.path, modId + ext);
        }

        if (modId == null)
            yield break;

        string? root = GetModsRoot(mod);
        if (root != null)
        {
            yield return Path.Combine(root, modId + ext);
            yield return Path.Combine(root, fileName);
        }

        if (!string.IsNullOrEmpty(mod.path))
            yield return Path.GetFullPath(Path.Combine(mod.path, "..", modId + ext));
    }
}
