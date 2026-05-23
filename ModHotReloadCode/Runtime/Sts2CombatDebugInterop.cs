using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace ModHotReload.Runtime;

/// <summary>通过官方 Debug API 启动 Rien 单局并进入弱怪战斗。</summary>
internal static class Sts2CombatDebugInterop
{
    private const string RienCharacterTypeName = "Rien.RienCode.Character.Rien";

    internal static async Task StartRienSingleplayerRunAsync()
    {
        NGame game = NGame.Instance ?? throw new InvalidOperationException("NGame.Instance 为空。");
        if (game.MainMenu == null)
            throw new InvalidOperationException("MainMenu 未就绪，无法开新局。");

        CharacterModel character = CreateRienCharacter();
        IReadOnlyList<ActModel> acts = [ModelDb.Act<Underdocks>()];

        RunState state = await game.StartNewSingleplayerRun(
            character,
            shouldSave: false,
            acts,
            modifiers: [],
            seed: "RIENCOMBATVERIFY",
            gameMode: GameMode.Standard,
            ascensionLevel: 0,
            dailyTime: null);

        _ = state;
    }

    internal static async Task EnterWeakMonsterCombatAsync()
    {
        RunManager run = RunManager.Instance ?? throw new InvalidOperationException("RunManager.Instance 为空。");
        AbstractModel encounter = ResolveEncounter();
        _ = await run.EnterRoomDebug(RoomType.Monster, MapPointType.Monster, encounter, showTransition: false);
        await RienCombatBootstrapInterop.WarmUpAfterCombatEntryAsync();
    }

    internal static CharacterModel CreateRienCharacter()
    {
        foreach (CharacterModel ch in ModelDb.AllCharacters)
        {
            ModelId id = ModelDb.GetId(ch.GetType());
            if (id.ToString().Contains("RIEN", StringComparison.OrdinalIgnoreCase))
                return ch;
        }

        Type? rienType = Type.GetType($"{RienCharacterTypeName}, Rien", throwOnError: false)
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(RienCharacterTypeName, throwOnError: false))
                .FirstOrDefault(t => t != null);

        throw new InvalidOperationException($"未找到 Rien 角色（{RienCharacterTypeName}），ModelDb 中无 CHARACTER.*RIEN*。");
    }

    private static AbstractModel ResolveEncounter()
    {
        EncounterModel canonical = ModelDb.Encounter<ToadpolesWeak>();
        return canonical.ToMutable();
    }
}
