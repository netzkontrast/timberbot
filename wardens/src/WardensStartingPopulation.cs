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

using System.Collections.Generic;
using Timberborn.Beavers;
using Timberborn.Bots;
using Timberborn.Characters;
using Timberborn.Common;
using Timberborn.GameFactionSystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensStartingPopulation : ILoadableSingleton
    {
        public const string FactionId = "Wardens";

        private readonly EventBus _eventBus;
        private readonly FactionService _factionService;
        private readonly CharacterPopulation _characterPopulation;
        private readonly BotFactory _botFactory;

        public WardensStartingPopulation(
            EventBus eventBus,
            FactionService factionService,
            CharacterPopulation characterPopulation,
            BotFactory botFactory)
        {
            _eventBus = eventBus;
            _factionService = factionService;
            _characterPopulation = characterPopulation;
            _botFactory = botFactory;
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

            Debug.Log($"[Wardens] new game: replaced {beavers.Count} starting beavers with bots");
        }
    }
}
