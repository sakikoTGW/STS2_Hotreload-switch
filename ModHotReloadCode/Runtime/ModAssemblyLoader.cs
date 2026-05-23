using System.Reflection;

using System.Runtime.Loader;

using MegaCrit.Sts2.Core.Modding;

using ModHotReload.Core;



namespace ModHotReload.Runtime;



internal static class ModAssemblyLoader

{

    internal static bool IsDefaultAlc(Assembly? assembly) => ModCollectibleHost.IsDefault(assembly);



    internal static bool IsCollectibleAlc(Assembly? assembly) => ModCollectibleHost.IsCollectible(assembly);



    internal static Assembly LoadHotReload(Mod mod, string modId, string shadowDll)

    {

        Assembly? previous = mod.assembly;

        bool migratingFromDefault = IsDefaultAlc(previous);



        string[] probes = [mod.path, ModStagingStore.GetStagingModDir(modId)];

        Assembly loaded = ModCollectibleHost.Reload(modId, shadowDll, probes);



        Version version = loaded.GetName().Version ?? new Version(0, 0);

        string alcNote = migratingFromDefault ? "（Default 僵尸副本已弃用，逻辑走 collectible）" : "";

        MainFile.Logger.Info($"[热重载] {modId} 新程序集 v{version}{alcNote}");



        return loaded;

    }

}


