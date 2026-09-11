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
//     WardensBotWorkforceMigration  on every Wardens game's load, unlocks bots for free in every
//                               workplace template that prices them (WorkplaceUnlockingService.
//                               UnlockIgnoringCost). The blueprint rule only reaches our own
//                               blueprints; the Wardens bar also carries vanilla ones, and the
//                               vanilla Scavenger Flag wants 500 Science before a bot may work it, so
//                               without this the first building of level 01 never gets a worker (seen
//                               in the game, 0.4.5). Then, once per save that predates this pass, every
//                               district default and every workplace is set to Bot; the save carries
//                               the marker, so later choices by the player stand.

using System.Linq;
using Timberborn.BaseComponentSystem;
using Timberborn.BlueprintSystem;
using Timberborn.EntitySystem;
using Timberborn.GameFactionSystem;
using Timberborn.Persistence;
using Timberborn.SingletonSystem;
using Timberborn.TemplateSystem;
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
        // V2: the pass that also runs after the free unlock. Saves marked by the first pass (0.4.2 to
        // 0.4.5) get it once more, because their flags could not take bots then.
        private static readonly PropertyKey<bool> AppliedKey = new PropertyKey<bool>("AppliedV2");

        private readonly ISingletonLoader _singletonLoader;
        private readonly EntityRegistry _entityRegistry;
        private readonly FactionService _factionService;
        private readonly TemplateService _templateService;
        private readonly WorkplaceUnlockingService _workplaceUnlockingService;
        private bool _applied;
        private bool _unlocked;

        public WardensBotWorkforceMigration(ISingletonLoader singletonLoader, EntityRegistry entityRegistry,
            FactionService factionService, TemplateService templateService,
            WorkplaceUnlockingService workplaceUnlockingService)
        {
            _singletonLoader = singletonLoader;
            _entityRegistry = entityRegistry;
            _factionService = factionService;
            _templateService = templateService;
            _workplaceUnlockingService = workplaceUnlockingService;
        }

        private bool IsWardens => _factionService.Current?.Id == WardensStartingPopulation.FactionId;

        public void Load()
        {
            _applied = _singletonLoader.TryGetSingleton(Key, out var loader) && loader.Has(AppliedKey) && loader.Get(AppliedKey);
            // Before the entities load, so a workplace saved with bots keeps them.
            UnlockBotsEverywhere();
        }

        // Every workplace template that prices bots, unlocked without spending anything. The set is
        // saved by WorkplaceUnlockingService itself; doing it again on every load is harmless and
        // covers templates a mod update added since.
        private void UnlockBotsEverywhere()
        {
            if (_unlocked || !IsWardens) return;
            _unlocked = true;
            int count = 0;
            foreach (var workplace in _templateService.GetAll<WorkplaceSpec>())
            {
                if (workplace.WorkerTypeUnlockCosts.IsDefaultOrEmpty) continue;
                var template = workplace.GetSpec<TemplateSpec>().TemplateName;
                foreach (var cost in workplace.WorkerTypeUnlockCosts.Where(c => c.WorkerType == WardensBotWorkforce.Bot && c.ScienceCost > 0))
                {
                    _workplaceUnlockingService.UnlockIgnoringCost(new UnlockableWorkerType(template, cost.WorkerType));
                    count++;
                }
            }
            Debug.Log($"[Wardens] bot workforce: bots unlocked for free in {count} workplace template(s)");
        }

        // After the entities are loaded, so every district and workplace of the save is there.
        public void PostLoad()
        {
            UnlockBotsEverywhere();     // in case the faction was not known yet at Load
            if (_applied || !IsWardens) return;
            int districts = 0, workplaces = 0;
            foreach (var entity in _entityRegistry.Entities)
            {
                var district = entity.GetComponent<DistrictDefaultWorkerType>();
                if (district != null && district.WorkerType != WardensBotWorkforce.Bot)
                {
                    district.SetWorkerType(WardensBotWorkforce.Bot);
                    districts++;
                }
                // SetWorkerType refuses a type the workplace does not allow (a beaver-only treadmill),
                // so this moves exactly the workplaces bots may work.
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
