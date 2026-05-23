using System.Reflection;
using System.Collections;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;

namespace ModHotReload.Reflection;

internal static class ModManagerReflection
{
    private static readonly FieldInfo InitializedField =
        typeof(ModManager).GetField("_initialized", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly FieldInfo HasHarmonyPatchesField =
        typeof(ModManager).GetField("_hasHarmonyPatches", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly FieldInfo FileIoField =
        typeof(ModManager).GetField("_fileIo", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly FieldInfo SettingsField =
        typeof(ModManager).GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly FieldInfo OnModDetectedField =
        typeof(ModManager).GetField("OnModDetected", BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static bool Initialized
    {
        get => (bool)(InitializedField.GetValue(null) ?? false);
        set => InitializedField.SetValue(null, value);
    }

    internal static void InvalidateHarmonyCache() => HasHarmonyPatchesField.SetValue(null, null);

    internal static bool FileExists(string path)
    {
        object? fileIo = FileIoField.GetValue(null);
        if (fileIo == null)
            return File.Exists(path);

        MethodInfo? m = fileIo.GetType().GetMethod("FileExists", [typeof(string)]);
        if (m == null)
            return File.Exists(path);

        return (bool)(m.Invoke(fileIo, [path]) ?? false);
    }

    internal static bool IsModDisabled(string modId, ModSource source)
    {
        object? settings = SettingsField.GetValue(null);
        if (settings == null)
            return false;

        MethodInfo? m = settings.GetType().GetMethod("IsModDisabled", [typeof(string), typeof(ModSource)]);
        if (m == null)
            return false;

        return (bool)(m.Invoke(settings, [modId, source]) ?? false);
    }

    internal static void SetModEnabled(Mod mod, bool enabled)
    {
        string? modId = mod.manifest?.id;
        if (string.IsNullOrEmpty(modId))
            return;

        object? settings = SettingsField.GetValue(null);
        object? list = settings?.GetType().GetProperty("ModList")?.GetValue(settings);
        if (list is not System.Collections.IEnumerable entries)
            return;

        foreach (object entry in entries)
        {
            Type type = entry.GetType();
            string? id = type.GetProperty("Id")?.GetValue(entry) as string;
            object? source = type.GetProperty("Source")?.GetValue(entry);
            if (!string.Equals(id, modId, StringComparison.OrdinalIgnoreCase)
                || source is not ModSource entrySource
                || entrySource != mod.modSource)
                continue;

            type.GetProperty("IsEnabled")?.SetValue(entry, enabled);
            try
            {
                SaveManager.Instance.SaveSettings();
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[热重载] 保存 Mod 设置失败: {ex.Message}");
            }
            return;
        }
    }

    internal static void EnsureModHotReloadFirstInSettings()
    {
        object? settings = SettingsField.GetValue(null);
        object? listObj = settings?.GetType().GetProperty("ModList")?.GetValue(settings);
        if (listObj is not IList list || list.Count <= 1)
            return;

        int index = -1;
        for (int i = 0; i < list.Count; i++)
        {
            object? entry = list[i];
            string? id = entry?.GetType().GetProperty("Id")?.GetValue(entry) as string;
            if (string.Equals(id, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index <= 0)
            return;

        object? self = list[index];
        list.RemoveAt(index);
        list.Insert(0, self);

        try
        {
            SaveManager.Instance.SaveSettings();
            MainFile.Logger.Warn("[热重载] 已把 ModHotReload 写到 Mod 顺序首位；若本次已有内容 DLL 先进入 Default ALC，下次启动后才能完全隔离卸载。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[热重载] 保存 Mod 顺序失败: {ex.Message}");
        }
    }

    /// <summary>原生 UI 勾选/运行期启停时抑制 OnModDetected 与 OnNewModDetected 副作用。</summary>
    internal static bool SuppressModDetectedEvent => _suppressModDetectedDepth > 0;

    private static int _suppressModDetectedDepth;

    internal static void EnterSuppressModDetectedEvent() => _suppressModDetectedDepth++;

    internal static void ExitSuppressModDetectedEvent()
    {
        if (_suppressModDetectedDepth > 0)
            _suppressModDetectedDepth--;
    }

    internal static void RaiseOnModDetected(Mod mod)
    {
        if (SuppressModDetectedEvent)
            return;

        if (OnModDetectedField.GetValue(null) is not Action<Mod> handler)
            return;

        handler.Invoke(mod);
    }
}
