using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload.Runtime;

/// <summary>任意 mod 重载后，级联重载依赖它的其它已加载 mod（通用，不限于单个项目）。</summary>
internal static class ModDependencyCascade
{
    /// <summary>按依赖拓扑排序的已加载 mod Id（依赖方在后）。</summary>
    internal static List<string> GetReloadOrder()
    {
        var ids = ModManager.Mods
            .Where(m => m.state == ModLoadState.Loaded && m.manifest?.id != null)
            .Where(m => !string.Equals(m.manifest!.id, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.manifest!.id!)
            .ToList();

        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sorted = new List<string>(ids.Count);

        void Visit(string id)
        {
            if (!visited.Add(id))
                return;

            Mod? mod = ModManager.Mods.FirstOrDefault(m => m.manifest?.id == id);
            if (mod?.manifest?.dependencies != null)
            {
                foreach (string dep in mod.manifest.dependencies)
                {
                    if (idSet.Contains(dep))
                        Visit(dep);
                }
            }

            sorted.Add(id);
        }

        foreach (string id in ids.OrderBy(id =>
                 ModManager.Mods.FirstOrDefault(m => m.manifest?.id == id)?.manifest?.dependencies?.Count ?? 0))
            Visit(id);

        return sorted;
    }

    internal static void ReloadDependents(string changedModId, bool force)
    {
        List<Mod> dependents = ModManager.Mods
            .Where(m => m.state == ModLoadState.Loaded)
            .Where(m => m.manifest?.id != null)
            .Where(m => !string.Equals(m.manifest!.id, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
            .Where(m => !string.Equals(m.manifest!.id, changedModId, StringComparison.OrdinalIgnoreCase))
            .Where(m => m.manifest!.dependencies?.Contains(changedModId) == true)
            .OrderBy(m => m.manifest!.dependencies!.Count)
            .ToList();

        if (dependents.Count == 0)
            return;

        var depIds = new HashSet<string>(
            dependents.Select(d => d.manifest!.id!),
            StringComparer.OrdinalIgnoreCase);

        MainFile.Logger.Info($"[热重载] {changedModId} 变更 → 级联重载 {dependents.Count} 个依赖方: {string.Join(", ", depIds)}");

        foreach (string id in GetReloadOrder())
        {
            if (!depIds.Contains(id))
                continue;

            Mod? dep = ModManager.Mods.FirstOrDefault(m => m.manifest?.id == id);
            if (dep != null)
                HotReloadCoordinator.Reload(dep, ReloadChangeKind.DllOrJson, force);
        }
    }

    /// <summary>reloadall 前清空所有已加载 mod 在 ModelDb 中的条目，避免任意 mod 重复注册。</summary>
    internal static void ClearAllLoadedModModels()
    {
        int total = 0;
        foreach (Mod mod in ModManager.Mods)
        {
            if (mod.state != ModLoadState.Loaded)
                continue;
            if (string.Equals(mod.manifest?.id, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
                continue;

            total += ModelDbCleanup.RemoveAssemblyModels(mod.assembly);
        }

        ModelDbCleanup.InvalidateListCaches();

        if (total > 0)
            MainFile.Logger.Info($"[热重载] reloadall 前 ModelDb 共移除 {total} 项。");
    }
}
