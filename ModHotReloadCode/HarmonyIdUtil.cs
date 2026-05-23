using MegaCrit.Sts2.Core.Modding;

namespace ModHotReload;

internal static class HarmonyIdUtil
{
    /// <summary>与 ModManager.TryLoadMod 中 Harmony 构造一致：author + "." + modId。</summary>
    internal static string GetHarmonyId(Mod mod)
    {
        string author = mod.manifest?.author ?? "unknown";
        string modId = mod.manifest?.id ?? "unknown";
        return $"{author}.{modId}";
    }
}
