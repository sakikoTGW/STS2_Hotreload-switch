using System.Reflection;
using HarmonyLib;

namespace ModHotReload.Core;

/// <summary>
/// StartupHook 阶段安装：设置里已禁用的 mod 在 TryLoadMod 时被拦截（不依赖 ModHotReload 主 DLL 加载顺序）。
/// </summary>
public static class ModLoadSettingsGate
{
    private const string HarmonyId = "ModHotReload.Core.settings-gate";
    private static int _installed;

    public static void Install(Action<string>? logInfo = null, Action<string>? logWarn = null)
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        try
        {
            Assembly? sts2 = ResolveSts2Assembly();
            Type? modManager = sts2?.GetType("MegaCrit.Sts2.Core.Modding.ModManager");
            MethodInfo? tryLoadMod = modManager?
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "TryLoadMod" && m.GetParameters().Length == 1);

            if (tryLoadMod == null)
            {
                logWarn?.Invoke("[ModHotReload] SettingsGate: 未找到 ModManager.TryLoadMod");
                Interlocked.Exchange(ref _installed, 0);
                return;
            }

            var harmony = new Harmony(HarmonyId);
            MethodInfo prefix = typeof(ModLoadSettingsGate).GetMethod(
                nameof(TryLoadModPrefix),
                BindingFlags.Static | BindingFlags.NonPublic)!;
            harmony.Patch(tryLoadMod, new HarmonyMethod(prefix));
            logInfo?.Invoke("[ModHotReload] SettingsGate: TryLoadMod 禁用门闩已安装");
        }
        catch (Exception ex)
        {
            logWarn?.Invoke($"[ModHotReload] SettingsGate 安装失败: {ex.Message}");
            Interlocked.Exchange(ref _installed, 0);
        }
    }

    private static bool TryLoadModPrefix(object[] __args)
    {
        if (__args.Length < 1 || __args[0] == null)
            return true;

        object mod = __args[0];
        try
        {
            string? modId = mod.GetType().GetProperty("manifest")?.GetValue(mod) is { } manifest
                ? manifest.GetType().GetProperty("id")?.GetValue(manifest) as string
                : null;

            if (string.IsNullOrEmpty(modId)
                || modId.Equals("ModHotReload", StringComparison.OrdinalIgnoreCase))
                return true;

            object? modSource = mod.GetType().GetField("modSource")?.GetValue(mod)
                ?? mod.GetType().GetProperty("modSource")?.GetValue(mod);

            if (!IsModDisabled(modId, modSource))
                return true;

            Type modType = mod.GetType();
            FieldInfo? stateField = modType.GetField("state");
            if (stateField?.FieldType is { } stateType && stateType.IsEnum)
                stateField.SetValue(mod, Enum.Parse(stateType, "Disabled"));

            modType.GetField("assembly")?.SetValue(mod, null);
            modType.GetField("errors")?.SetValue(mod, null);
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsModDisabled(string modId, object? modSource)
    {
        Assembly? sts2 = ResolveSts2Assembly();
        FieldInfo? settingsField = sts2?
            .GetType("MegaCrit.Sts2.Core.Modding.ModManager")?
            .GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic);

        object? settings = settingsField?.GetValue(null);
        if (settings == null || modSource == null)
            return false;

        MethodInfo? m = settings.GetType().GetMethod(
            "IsModDisabled",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            [typeof(string), modSource.GetType()],
            null);

        return m != null && (bool)(m.Invoke(settings, [modId, modSource]) ?? false);
    }

    private static Assembly? ResolveSts2Assembly()
    {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(asm.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase))
                return asm;
        }

        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "sts2.dll"),
            Path.Combine(baseDir, "SlayTheSpire2_Data", "Managed", "sts2.dll"),
            Path.Combine(baseDir, "..", "SlayTheSpire2_Data", "Managed", "sts2.dll"),
        ];

        foreach (string path in candidates)
        {
            string full = Path.GetFullPath(path);
            if (!File.Exists(full))
                continue;

            try
            {
                return Assembly.LoadFrom(full);
            }
            catch
            {
                // ignored
            }
        }

        return null;
    }
}
