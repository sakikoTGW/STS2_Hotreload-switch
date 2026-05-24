using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload.Runtime;

/// <summary>首次重载某 mod 时输出布局与级联提示，便于排查第三方 mod 兼容性。</summary>
internal static class ModCompatibilityHints
{
    private static readonly HashSet<string> Logged = new(StringComparer.OrdinalIgnoreCase);

    internal static void LogOnce(Mod mod)
    {
        string? modId = mod.manifest?.id;
        if (modId == null || !Logged.Add(modId))
            return;

        bool hasDll = ModPayloadPaths.Exists(mod, modId + ".dll");
        bool hasPck = ModPayloadPaths.Exists(mod, modId + ".pck");
        int deps = mod.manifest?.dependencies?.Count ?? 0;
        string? pckLive = ModPayloadPaths.ResolveFirstExisting(mod, modId + ".pck");
        string? dllLive = ModPayloadPaths.ResolveFirstExisting(mod, modId + ".dll");

        MainFile.Logger.Info(
            $"[热重载][{modId}] 布局: dll={hasDll} pck={hasPck} deps={deps} " +
            $"path={mod.path} state={mod.state}");

        if (hasPck && pckLive != null && !pckLive.StartsWith(mod.path, StringComparison.OrdinalIgnoreCase))
            MainFile.Logger.Info($"[热重载][{modId}] PCK 非子目录内: {pckLive}");

        if (hasDll && dllLive != null && !dllLive.StartsWith(mod.path, StringComparison.OrdinalIgnoreCase))
            MainFile.Logger.Info($"[热重载][{modId}] DLL 非子目录内: {dllLive}");

        var s = ModHotReloadSettings.Current;
        if (string.Equals(modId, "BaseLib", StringComparison.OrdinalIgnoreCase) && !s.CascadeReloadAllOnBaseLib)
            MainFile.Logger.Info(
                $"[热重载][BaseLib] cascadeReloadAllOnBaseLib=false，更新后不会自动 reloadall；依赖方请设 cascadeDependentsOnReload 或手动 reloadall。");
    }
}
