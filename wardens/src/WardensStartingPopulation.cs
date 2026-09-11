// WardensStartingPopulation.cs. Spike A: the Wardens start as bots.
//
// The game hardcodes the starting population to beavers:
//   GameInitializer.SpawnBeavers() -> StartingBeaversInitializer -> BeaverFactory
// with the counts coming from GameModeSpec (Easy/Normal/Hard), which is shared by
// every faction. There is no faction hook. So we let the game spawn its beavers,
// wait for NewGameInitializedEvent (posted right after), and replace each beaver
// with a bot at the same position. Only fires when the active faction is Wardens.
//
// Removal goes through Character.DestroyCharacter(), never EntityService.Delete()
// directly: CharacterPopulation only drops a character on CharacterKilledEvent, and
// DistrictCitizenAssigner ticks over that list, so a raw delete leaves destroyed
// objects in it and NREs on the next tick (that was the first in-game crash).
//
// The bot template comes from the faction's Characters collection (v0.1 reuses
// Characters.IronTeeth, so bots look like Iron Teeth bots until we ship our own).
//
// Then the opening balance (PLAYTEST.md, level 01 played through the MCP server, 2026-09-11): vanilla
// bots boot at 50% Energy and lose 58% a day, a Charging Post costs 5 scrap, and the first scrap needs
// flags built and ruins stripped first — so every game opened with idle Wardens at 0% by day 2. The
// Wardens boot fully charged (about 1.7 days of charge), and the Core holds StartingScrap, enough for
// the first two Charging Posts before a single ruin is touched.

using System.Collections.Generic;
using Timberborn.Beavers;
using Timberborn.Bots;
using Timberborn.Characters;
using Timberborn.Common;
using Timberborn.Effects;
using Timberborn.GameDistricts;
using Timberborn.GameFactionSystem;
using Timberborn.Goods;
using Timberborn.NeedSystem;
using Timberborn.SimpleOutputBuildings;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensStartingPopulation : ILoadableSingleton
    {
        public const string FactionId = "Wardens";
        private const string EnergyNeed = "Energy";
        private const string Scrap = "ScrapMetal";
        public const int StartingScrap = 10;

        private readonly EventBus _eventBus;
        private readonly FactionService _factionService;
        private readonly CharacterPopulation _characterPopulation;
        private readonly BotFactory _botFactory;
        private readonly DistrictCenterRegistry _districtCenterRegistry;

        public WardensStartingPopulation(
            EventBus eventBus,
            FactionService factionService,
            CharacterPopulation characterPopulation,
            BotFactory botFactory,
            DistrictCenterRegistry districtCenterRegistry)
        {
            _eventBus = eventBus;
            _factionService = factionService;
            _characterPopulation = characterPopulation;
            _botFactory = botFactory;
            _districtCenterRegistry = districtCenterRegistry;
        }

        public void Load()
        {
            _eventBus.Register(this);
        }

        // Posted by GameInitializer.PostSpawnBeavers(), i.e. only for a NEW game.
        // Loaded saves never hit this, so an existing Wardens colony is untouched.
        [OnEvent]
        public void OnNewGameInitialized(NewGameInitializedEvent e)
        {
            if (_factionService.Current?.Id != FactionId)
                return;

            // Snapshot first: DestroyCharacter posts CharacterKilledEvent, which
            // mutates the population list we are reading.
            var beavers = new List<Character>();
            foreach (var character in _characterPopulation.Characters)
            {
                if (character.GetComponent<Beaver>() != null)
                    beavers.Add(character);
            }

            var positions = new List<Vector3>(beavers.Count);
            foreach (var beaver in beavers)
            {
                positions.Add(beaver.Transform.position);
                beaver.DestroyCharacter();   // KillCharacter() + EntityService.Delete()
            }

            foreach (var position in positions)
                _botFactory.Create(position);

            Debug.Log($"[Wardens] new game: replaced {beavers.Count} starting beavers with bots, " +
                      $"{ChargeEveryBot()} charged to full, {GiveStartingScrap()} Scrap Metal in the Core");
        }

        private int ChargeEveryBot()
        {
            int charged = 0;
            foreach (var character in _characterPopulation.Characters)
            {
                if (character.GetComponent<Bot>() == null) continue;
                var needs = character.GetComponent<NeedManager>();
                if (needs == null || !needs.HasNeed(EnergyNeed)) continue;
                float missing = needs.NeedPointsToMax(EnergyNeed);
                if (missing > 0f) needs.ApplyEffect(new InstantEffect(EnergyNeed, missing, 1));
                charged++;
            }
            return charged;
        }

        // Into the Core's public output inventory, the one vanilla puts the starting food in.
        private int GiveStartingScrap()
        {
            foreach (var districtCenter in _districtCenterRegistry.AllDistrictCenters)
            {
                var inventory = districtCenter.GetComponent<SimpleOutputInventory>()?.Inventory;
                if (inventory == null || !inventory.Gives(Scrap)) continue;
                inventory.GiveExistingIgnoringCapacity(new GoodAmount(Scrap, StartingScrap));
                return StartingScrap;
            }
            return 0;
        }
    }
}
