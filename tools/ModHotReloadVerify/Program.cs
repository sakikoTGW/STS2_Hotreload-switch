using System.Reflection;
using HarmonyLib;
using ModHotReloadVerify;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: ModHotReloadVerify <ModHotReload.dll> [sts2_data_dir]");
    Console.Error.WriteLine("  sts2_data_dir defaults to STS2_DATA_DIR env or Sts2PathDiscovery (build with /p:Sts2Path=...).");
    Environment.Exit(2);
}

string dll = Path.GetFullPath(args[0]);
string sts2Data = args.Length > 1
    ? args[1]
    : Environment.GetEnvironmentVariable("STS2_DATA_DIR") ?? "";

if (string.IsNullOrWhiteSpace(sts2Data))
    sts2Data = Environment.GetEnvironmentVariable("Sts2DataDir") ?? "";

if (string.IsNullOrWhiteSpace(sts2Data) || !Directory.Exists(sts2Data))
{
    Console.Error.WriteLine("FAIL: sts2 data dir not found. Pass as 2nd arg or set STS2_DATA_DIR.");
    Environment.Exit(1);
}

if (!File.Exists(dll))
{
    Console.Error.WriteLine($"FAIL: DLL not found: {dll}");
    Environment.Exit(1);
}

AppDomain.CurrentDomain.AssemblyResolve += (_, resolveArgs) =>
{
    string name = new AssemblyName(resolveArgs.Name).Name ?? "";
    string path = Path.Combine(sts2Data, name + ".dll");
    if (File.Exists(path))
        return Assembly.LoadFrom(path);
    if (name == "0Harmony")
    {
        string alt = Path.Combine(sts2Data, "0Harmony.dll");
        if (File.Exists(alt))
            return Assembly.LoadFrom(alt);
    }
    return null;
};

string modDir = Path.GetDirectoryName(dll)!;
var asm = Assembly.LoadFrom(dll);
string coreDll = Path.Combine(modDir, "ModHotReload.Core.dll");
var coreAsm = File.Exists(coreDll) ? Assembly.LoadFrom(coreDll) : null;
int fails = 0;

void Check(bool ok, string msg)
{
    if (ok) Console.WriteLine($"OK  {msg}");
    else { Console.WriteLine($"FAIL {msg}"); fails++; }
}

var main = asm.GetType("ModHotReload.MainFile");
Check(main != null, "MainFile 类型存在");
var version = main?.GetField("Version", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
Check(version == "1.6.5", $"Version == 1.6.5 (实际: {version})");

string[] requiredTypes =
[
    "ModHotReload.Runtime.ModelDbCleanup",
    "ModHotReload.Runtime.GameSafetyGuard",
    "ModHotReload.Runtime.CombatReloadSettings",
    "ModHotReload.Runtime.CombatReloadInterop",
    "ModHotReload.Runtime.ModStagingStore",
    "ModHotReload.Runtime.HotReloadCoordinator",
    "ModHotReload.Runtime.ModLifecycleCoordinator",
    "ModHotReload.Runtime.RuntimeModModeCoordinator",
    "ModHotReload.Runtime.NativeModUiBridge",
    "ModHotReload.Runtime.ModelIdSerializationCacheInterop",
    "ModHotReload.Runtime.PckVirtualUnmountRegistry",
    "ModHotReload.Patches.NModMenuRowOnTickboxToggledPatch",
    "ModHotReload.Patches.NModdingScreenOnModEnabledOrDisabledPatch",
    "ModHotReload.Patches.NModdingScreenOnNewModDetectedPatch",
    "ModHotReload.Patches.NGameOnNewModDetectedPatch",
    "ModHotReload.Patches.GodotLookupScriptsInAssemblyPatch",
    "ModHotReload.Patches.GodotPathScriptTypeBiMapAddPatch",
    "ModHotReload.Runtime.GodotScriptRegistrationInterop",
    "ModHotReload.Runtime.ModHotReloadEarlyBootstrap",
    "ModHotReload.Runtime.DefaultAlcMigration",
    "ModHotReload.Patches.CombatLifecyclePatch",
    "ModHotReload.Patches.ModManagerTryLoadModPatch",
    "ModHotReload.Patches.ModManagerIsRunningModdedPatch",
    "ModHotReload.Patches.ResourceLoaderLoadVirtualUnmountPatch",
    "ModHotReload.Runtime.IntegrationTestRunner",
    "ModHotReload.Runtime.IntegrationTestMode",
    "ModHotReload.Runtime.HarmonyInstaller",
    "ModHotReload.Runtime.ModSatelliteAssemblyLoader",
    "ModHotReload.Runtime.ModModuleEntry",
    "ModHotReload.Runtime.ModStartupReconciler",
];

foreach (string name in requiredTypes)
    Check(asm.GetType(name) != null, $"类型 {name}");

Check(coreAsm?.GetType("ModHotReload.Core.ModBootstrapEntry") != null, "ModHotReload.Core.ModBootstrapEntry");
Check(coreAsm?.GetType("ModHotReload.Core.ModCollectibleHost") != null, "ModHotReload.Core.ModCollectibleHost");
Check(coreAsm?.GetType("ModHotReload.Core.ModLoadSettingsGate") != null, "ModHotReload.Core.ModLoadSettingsGate");

var godotPrefix = asm.GetType("ModHotReload.Patches.GodotPathScriptTypeBiMapAddPatch")?
    .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
var godotParam = godotPrefix?.GetParameters().FirstOrDefault()?.Name;
Check(godotParam == "scriptPath", $"GodotPathScriptTypeBiMapAddPatch.Prefix 参数 scriptPath (实际: {godotParam})");

try
{
    var harmony = new Harmony("ModHotReload.Verify");
    new PatchClassProcessor(harmony, typeof(VerifyGodotPathBiMapPatch)).Patch();
    Check(true, "Harmony 可成功 patch PathScriptTypeBiMap.Add (Godot 4.5.1)");
    harmony.UnpatchAll("ModHotReload.Verify");
}
catch (Exception ex)
{
    Check(false, $"Harmony patch PathScriptTypeBiMap.Add 失败: {ex.Message}");
}
Check(File.Exists(Path.Combine(Path.GetDirectoryName(dll)!, "ModHotReload.StartupHook.dll")),
    "ModHotReload.StartupHook.dll 已部署");

var combatPatch = asm.GetType("ModHotReload.Patches.CombatLifecyclePatch");
var harmonyAttrs = combatPatch?.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
    .SelectMany(m => m.GetCustomAttributesData())
    .Any(a => a.AttributeType.Name.Contains("HarmonyPatch")) ?? false;
Check(harmonyAttrs, "CombatLifecyclePatch 含 HarmonyPatch");

var modelCleanup = asm.GetType("ModHotReload.Runtime.ModelDbCleanup");
Check(modelCleanup?.GetMethod("InvalidateListCaches", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) != null,
    "ModelDbCleanup.InvalidateListCaches");

var fi = new FileInfo(dll);
Check(fi.Length > 30_000, $"DLL 体积合理 ({fi.Length} bytes)");

HotReloadAuditReport.Print();

Console.WriteLine(fails == 0 ? "\n全部通过。" : $"\n{fails} 项失败。");
Environment.Exit(fails == 0 ? 0 : 1);
