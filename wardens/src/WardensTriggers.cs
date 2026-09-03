// WardensTriggers.cs. Event triggers for the Wardens' optional tutorials.
//
// Vanilla's trigger singletons (Timberborn.TutorialSteps) hardcode Folktails template names:
// PlatformBuiltTrigger only counts Platform.Folktails, MissingDamTrigger only counts Folktails dams,
// UnemployedBeaversTrigger counts beavers. These three do the same jobs with the Wardens' names and
// bots, and finish under their own trigger ids, which the Wardens tutorials list in
// RequiredTutorialIds (tools/gen_tutorial.py). The vanilla triggers still run; nothing of ours
// requires their ids, so they are harmless. (StairsUnlockedTrigger is the exception: it resolves
// Stairs.Folktails at load and would throw, so Stairs.Wardens carries that name as an alias and the
// vanilla trigger fires for our stairs. SurvivedFirstDrought/BadtideTrigger are faction-neutral.)
//
// A trigger only registers while its tutorial is still pending (TriggerPending), exactly like vanilla.

using Timberborn.BlockSystem;
using Timberborn.Buildings;
using Timberborn.GameCycleSystem;
using Timberborn.Population;
using Timberborn.SingletonSystem;
using Timberborn.TemplateSystem;
using Timberborn.TutorialSteps;
using Timberborn.TutorialSystem;

namespace Wardens
{
    /// End of cycle 3 without a single dam: "Wardens.Dams".
    public class WardensMissingDamTrigger : ILoadableSingleton
    {
        public const string TriggerId = "Wardens.MissingDamTrigger";
        private static readonly string[] DamTemplates = { "Dam.Wardens" };
        private readonly EventBus _eventBus;
        private readonly ITutorialTriggers _triggers;
        private readonly BuiltBuildingService _builtBuildings;

        public WardensMissingDamTrigger(EventBus eventBus, ITutorialTriggers triggers, BuiltBuildingService builtBuildings)
        {
            _eventBus = eventBus;
            _triggers = triggers;
            _builtBuildings = builtBuildings;
        }

        public void Load()
        {
            if (_triggers.TriggerPending(TriggerId)) _eventBus.Register(this);
        }

        [OnEvent]
        public void OnCycleEnded(CycleEndedEvent e)
        {
            if (e.Cycle == 3 && _builtBuildings.NumberOfAllBuildings(DamTemplates) == 0)
            {
                _eventBus.Unregister(this);
                _triggers.AddTrigger(TriggerId);
            }
        }
    }

    /// First finished Platform.Wardens: "Wardens.LayerTool".
    public class WardensPlatformBuiltTrigger : ILoadableSingleton
    {
        public const string TriggerId = "Wardens.PlatformBuiltTrigger";
        private const string PlatformTemplate = "Platform.Wardens";
        private readonly EventBus _eventBus;
        private readonly ITutorialTriggers _triggers;

        public WardensPlatformBuiltTrigger(EventBus eventBus, ITutorialTriggers triggers)
        {
            _eventBus = eventBus;
            _triggers = triggers;
        }

        public void Load()
        {
            if (_triggers.TriggerPending(TriggerId)) _eventBus.Register(this);
        }

        [OnEvent]
        public void OnEnteredFinishedState(EnteredFinishedStateEvent e)
        {
            var building = e.BlockObject.GetComponent<Building>();
            if (building == null) return;
            var template = building.GetComponent<TemplateSpec>();
            if (template != null && template.IsNamedExactly(PlatformTemplate))
            {
                _eventBus.Unregister(this);
                _triggers.AddTrigger(TriggerId);
            }
        }
    }

    /// From cycle 3 on, idle Wardens or a large fleet: "Wardens.Haulers".
    public class WardensIdleTrigger : ILoadableSingleton
    {
        public const string TriggerId = "Wardens.IdleWardensTrigger";
        private const int CycleThreshold = 3;
        private const int IdleThreshold = 3;
        private const int FleetThreshold = 20;
        private readonly EventBus _eventBus;
        private readonly ITutorialTriggers _triggers;
        private readonly PopulationService _population;
        private readonly GameCycleService _cycles;

        public WardensIdleTrigger(EventBus eventBus, ITutorialTriggers triggers, PopulationService population, GameCycleService cycles)
        {
            _eventBus = eventBus;
            _triggers = triggers;
            _population = population;
            _cycles = cycles;
        }

        public void Load()
        {
            if (_triggers.TriggerPending(TriggerId)) _eventBus.Register(this);
        }

        [OnEvent]
        public void OnPopulationChanged(PopulationChangedEvent e)
        {
            var data = _population.GlobalPopulationData;
            if (_cycles.Cycle >= CycleThreshold && (data.NumberOfBots >= FleetThreshold || data.BotWorkplaceData.Unemployed >= IdleThreshold))
            {
                _eventBus.Unregister(this);
                _triggers.AddTrigger(TriggerId);
            }
        }
    }
}
