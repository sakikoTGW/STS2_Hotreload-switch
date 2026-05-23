using System.Reflection;
using System.Runtime.Loader;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using ModHotReload.Core;
using ModHotReload.Reflection;

namespace ModHotReload.Runtime;

/// <summary>按官方 ModManager.TryLoadMod 逻辑热重载单个模组。</summary>
internal static class OfficialModLoader
{
    private static readonly MethodInfo CallModInitializerMethod = typeof(ModManager).GetMethod(
        "CallModInitializer",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static void LoadMod(Mod mod)
    {
        if (mod.manifest == null)
            throw new InvalidOperationException("manifest 为空");

        string modId = mod.manifest.id;
        mod.path = Path.GetFullPath(mod.path);

        bool wasInitialized = ModManagerReflection.Initialized;
        ModManagerReflection.Initialized = false;

        try
        {
            if (!CanLoad(mod, modId, out string? blockReason))
            {
                MainFile.Logger.Warn($"[热重载] {modId} 无法加载: {blockReason}");
                mod.state = ModLoadState.Failed;
                return;
            }

            Assembly? assembly = null;
            bool loadedPayload = false;

            // PCK 先于 DLL：后续 Initializer / ModelDb.Preload 能读到 res://ModId/ 资源
            string? pckPath = ModStagingStore.ResolvePayloadPath(mod, modId + ".pck", preferLive: true);
            if (mod.manifest.hasPck && pckPath != null && ModManagerReflection.FileExists(pckPath))
            {
                Log.Info($"Loading Godot PCK {pckPath}");
                GodotResourceInterop.ReloadResourcePack(pckPath, modId);
                loadedPayload = true;
            }
            else if (mod.manifest.hasPck)
            {
                MainFile.Logger.Error($"[热重载] 缺少 PCK: {modId}.pck");
            }

            string? dllPath = ModStagingStore.ResolvePayloadPath(mod, modId + ".dll", preferLive: false);
            if (mod.manifest.hasDll && dllPath != null && ModManagerReflection.FileExists(dllPath))
            {
                if (!ModFileUtil.WaitForStableFile(dllPath))
                    MainFile.Logger.Warn($"[热重载] DLL 未稳定: {dllPath}");

                string shadowDll = ModFileUtil.ShadowCopyDll(dllPath, modId);
                assembly = ModAssemblyLoader.LoadHotReload(mod, modId, shadowDll);
                loadedPayload = true;
                Log.Info($"Loading assembly DLL {dllPath} (shadow: {shadowDll})");

                if (!GodotScriptRegistrationInterop.InitializerRegistersGodotScripts(assembly))
                    GodotScriptRegistrationInterop.LookupScriptsInAssemblySafe(assembly);
                else
                    MainFile.Logger.Info(
                        $"[热重载] {modId} 由 ModInitializer 注册 Godot 脚本，跳过 LoadMod 内 Lookup（避免 duplicate key）");
            }
            else if (mod.manifest.hasDll)
            {
                MainFile.Logger.Error($"[热重载] 缺少 DLL: {modId}.dll");
            }

            if (!loadedPayload)
                MainFile.Logger.Warn($"[热重载] {modId} 未加载 DLL 或 PCK");

            bool? initOk;
            using IDisposable initScope = HotReloadCoordinator.IsReloading(modId)
                ? GodotScriptRegistrationInterop.BeginHotReloadScope()
                : GodotScriptRegistrationInterop.NoOpDisposable.Instance;
            {
                initOk = assembly != null ? RunInitializers(assembly, mod) : true;
            }

            if (initOk == false)
            {
                mod.state = ModLoadState.Failed;
                mod.assembly = assembly;
            }
            else
            {
                mod.state = ModLoadState.Loaded;
                mod.assembly = assembly;
                ModelDbCleanup.InjectAssemblyModels(assembly);
                Sts2AssetInterop.AfterModPayloadReload(modId, refreshPreload: false);
                Log.Info($"Finished mod initialization for '{mod.manifest.name}' ({modId}).");
                HotReloadCoordinator.SeedDllTimestamp(modId, dllPath != null && File.Exists(dllPath)
                    ? File.GetLastWriteTimeUtc(dllPath).Ticks
                    : 0);
                BaseLibInterop.TryRefreshMainMenuInjection();
            }

            mod.errors = null;
            if (!HotReloadCoordinator.IsReloading(modId))
                ModManagerReflection.RaiseOnModDetected(mod);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[热重载] 加载 {modId} 异常: {ex}");
            mod.state = ModLoadState.Failed;
        }
        finally
        {
            ModManagerReflection.Initialized = wasInitialized;
            ModManagerReflection.InvalidateHarmonyCache();
        }
    }

    internal static void UnloadCollectibleContext(string modId) => ModCollectibleHost.Unload(modId);

    internal static void UnloadAssemblyContext(string modId) => ModCollectibleHost.Unload(modId);

    private static bool CanLoad(Mod mod, string modId, out string? reason)
    {
        reason = null;

        if (ModManagerReflection.IsModDisabled(modId, mod.modSource))
        {
            reason = "模组在设置中已禁用";
            return false;
        }

        if (!ModManager.PlayerAgreedToModLoading)
        {
            reason = "玩家未同意加载模组";
            return false;
        }

        if (mod.manifest!.dependencies is { Count: > 0 } deps)
        {
            foreach (string dep in deps)
            {
                bool ok = ModManager.Mods.Any(m =>
                    m.manifest?.id == dep && m.state == ModLoadState.Loaded);
                if (!ok)
                {
                    reason = $"缺少已加载依赖 {dep}";
                    return false;
                }
            }
        }

        return true;
    }

    private static bool? RunInitializers(Assembly assembly, Mod mod)
    {
        string modId = mod.manifest!.id;
        List<Type> initializers = SafeGetTypes(assembly)
            .Where(t => t.GetCustomAttribute<ModInitializerAttribute>() != null)
            .ToList();

        if (initializers.Count == 0)
        {
            foreach (Type t in SafeGetTypes(assembly))
            {
                if (t.Name != "MainFile")
                    continue;
                if (t.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static) is not { } mi
                    || mi.GetParameters().Length != 0)
                    continue;

                initializers.Add(t);
                MainFile.Logger.Warn(
                    $"[热重载] {modId} ModInitializer 扫描失败，回退 {t.FullName}.Initialize");
                break;
            }
        }

        if (initializers.Count > 0)
        {
            bool allOk = true;
            foreach (Type type in initializers)
            {
                Log.Info($"Calling initializer method of type {type} for {assembly}");
                bool ok = InvokeInitializerOnce(type);
                if (!ok && HotReloadCoordinator.IsReloading(modId))
                {
                    MainFile.Logger.Warn($"[热重载] {modId} Initializer 失败，清 Godot 脚本表后重试一次…");
                    GodotScriptRegistrationInterop.PrepareForModReload(modId, assembly);
                    using (GodotScriptRegistrationInterop.BeginHotReloadScope())
                        ok = InvokeInitializerOnce(type);
                }

                allOk &= ok;
            }

            return allOk;
        }

        try
        {
            string harmonyId = (mod.manifest.author ?? "unknown") + "." + modId;
            Log.Info($"No ModInitializerAttribute detected. Calling Harmony.PatchAll for {assembly}");
            new Harmony(harmonyId).PatchAll(assembly);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Exception caught while trying to run PatchAll on assembly {assembly}:\n{ex}");
            return false;
        }
    }

    private static bool InvokeInitializerOnce(Type initializerType) =>
        (bool)(CallModInitializerMethod.Invoke(null, [initializerType]) ?? false);

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            int n = ex.LoaderExceptions?.Length ?? 0;
            MainFile.Logger.Warn($"[热重载] 部分类型加载失败: {n} 个");
            if (ex.LoaderExceptions != null)
            {
                foreach (Exception? le in ex.LoaderExceptions.Take(8))
                {
                    if (le != null)
                        MainFile.Logger.Warn($"[热重载]   {le.GetType().Name}: {le.Message}");
                }
            }

            var loaded = ex.Types.Where(t => t != null).ToList();
            bool hasInitializer = loaded.Any(t => t!.GetCustomAttribute<ModInitializerAttribute>() != null);
            MainFile.Logger.Info(
                $"[热重载] 可加载类型 {loaded.Count}/{ex.Types.Length}，含 ModInitializer={hasInitializer}");
            return loaded!;
        }
    }
}
