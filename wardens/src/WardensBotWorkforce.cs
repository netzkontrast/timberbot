// WardensBotWorkforce.cs. Bots work everywhere, by default, for free.
//
// The Warden is the best AI its makers ever built and knows everything about bots: no building has to
// be told that bots may work in it, and nothing about bots costs the Wardens science. Two halves:
//
//   Blueprints: every Wardens workplace has DefaultWorkerType "Bot" and no WorkerTypeUnlockCosts, and
//   bot buildings cost no science (wardens/tools/bot_workforce.py, run by the generators and checked
//   by validate.py).
//
//   This file: the district. Read in the 1.1.2.4 decompile (Timberborn.WorkSystem): when a workplace
//   finishes, WorkplaceWorkerType.SetWorkerTypeToDistrict copies its district center's
//   DistrictDefaultWorkerType over the blueprint default, and DistrictDefaultWorkerType starts every
//   district as "Beaver". Without this half the blueprint rule never shows in the game.
//
//     WardensBotWorkforce       on every Wardens district center (WardensBotWorkforceSpec, which the
//                               generator adds): sets the district default to Bot in Awake. Awake runs
//                               at instantiation, before a loaded entity's Load (ComponentCache.
//                               Initialize, then EntitiesLoader.Load), so a new district gets Bot and a
//                               player's saved choice still wins.
//     WardensBotWorkforceMigration  once per save that predates this, in a Wardens game: every district
//                               default and every workplace that allows it is set to Bot. The save
//                               then carries the marker, so later choices by the player stand.

using Timberborn.BaseComponentSystem;
using Timberborn.BlueprintSystem;
using Timberborn.EntitySystem;
using Timberborn.GameFactionSystem;
using Timberborn.Persistence;
using Timberborn.SingletonSystem;
using Timberborn.WorkSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace Wardens
{
    public record WardensBotWorkforceSpec : ComponentSpec
    {
    }

    public class WardensBotWorkforce : BaseComponent, IAwakableComponent
    {
        public const string Bot = "Bot";

        public void Awake()
        {
            GetComponent<DistrictDefaultWorkerType>()?.SetWorkerType(Bot);
        }
    }

    public class WardensBotWorkforceMigration : ILoadableSingleton, IPostLoadableSingleton, ISaveableSingleton
    {
        private static readonly SingletonKey Key = new SingletonKey("WardensBotWorkforce");
        private static readonly PropertyKey<bool> AppliedKey = new PropertyKey<bool>("Applied");

        private readonly ISingletonLoader _singletonLoader;
        private readonly EntityRegistry _entityRegistry;
        private readonly FactionService _factionService;
        private bool _applied;

        public WardensBotWorkforceMigration(ISingletonLoader singletonLoader, EntityRegistry entityRegistry,
            FactionService factionService)
        {
            _singletonLoader = singletonLoader;
            _entityRegistry = entityRegistry;
            _factionService = factionService;
        }

        public void Load()
        {
            _applied = _singletonLoader.TryGetSingleton(Key, out var loader) && loader.Get(AppliedKey);
        }

        // After the entities are loaded, so every district and workplace of the save is there.
        public void PostLoad()
        {
            if (_applied || _factionService.Current?.Id != WardensStartingPopulation.FactionId) return;
            int districts = 0, workplaces = 0;
            foreach (var entity in _entityRegistry.Entities)
            {
                var district = entity.GetComponent<DistrictDefaultWorkerType>();
                if (district != null && district.WorkerType != WardensBotWorkforce.Bot)
                {
                    district.SetWorkerType(WardensBotWorkforce.Bot);
                    districts++;
                }
                // SetWorkerType refuses a type the workplace does not allow (a beaver-only treadmill)
                // or has not unlocked, so this only ever moves what the blueprint rule already frees.
                var workplace = entity.GetComponent<WorkplaceWorkerType>();
                if (workplace != null && workplace.WorkerType != WardensBotWorkforce.Bot)
                {
                    workplace.SetWorkerType(WardensBotWorkforce.Bot);
                    if (workplace.WorkerType == WardensBotWorkforce.Bot) workplaces++;
                }
            }
            _applied = true;
            Debug.Log($"[Wardens] bot workforce: {districts} district default(s) and {workplaces} workplace(s) set to Bot");
        }

        public void Save(ISingletonSaver singletonSaver)
        {
            if (_applied) singletonSaver.GetSingleton(Key).Set(AppliedKey, true);
        }
    }
}
