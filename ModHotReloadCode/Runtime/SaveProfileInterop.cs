using System.Reflection;
using MegaCrit.Sts2.Core.Saves;

namespace ModHotReload.Runtime;

/// <summary>模式切换后重新绑定 profile 存档根（vanilla: profileN / modded: modded/profileN）。</summary>
internal static class SaveProfileInterop
{
    private static readonly PropertyInfo? CurrentProfileIdProp =
        typeof(SaveManager).GetProperty(nameof(SaveManager.CurrentProfileId),
            BindingFlags.Public | BindingFlags.Instance);

    private static readonly MethodInfo? SwitchProfileIdMethod =
        typeof(SaveManager).GetMethod(nameof(SaveManager.SwitchProfileId),
            BindingFlags.Public | BindingFlags.Instance, [typeof(int)]);

    internal static void RebindCurrentProfile()
    {
        try
        {
            SaveManager sm = SaveManager.Instance;
            if (CurrentProfileIdProp?.GetValue(sm) is not int profileId)
                return;

            if (SwitchProfileIdMethod == null)
            {
                MainFile.Logger.Warn("[热重载] SaveManager.SwitchProfileId 未找到，无法重绑存档路径。");
                return;
            }

            SwitchProfileIdMethod.Invoke(sm, [profileId]);
            MainFile.Logger.Info(
                $"[热重载] 存档路径已按 {RuntimeModModeCoordinator.CurrentMode} 重绑（profile{profileId}）。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] 重绑存档路径失败: {ex.Message}");
        }
    }

    /// <summary>无 Mod 模式下去掉路径中的 modded/ 段（兼容已缓存的旧路径）。</summary>
    internal static string NormalizeScopedPathForMode(string path, bool vanilla)
    {
        if (!vanilla || string.IsNullOrEmpty(path))
            return path;

        string normalized = path.Replace('\\', '/');
        const string segment = "/modded/";
        while (normalized.Contains(segment, StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Replace(segment, "/", StringComparison.OrdinalIgnoreCase);

        if (normalized.StartsWith("modded/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["modded/".Length..];

        return normalized;
    }
}
