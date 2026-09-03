// WardensPopulationStep.cs. "Beavers: (n/m)" for the First Beaver tutorial.
//
// Vanilla's BeaverBirthStepSpec listens for BeaverBornEvent through FirstbornService. The Wardens
// start with no beavers at all and grow them in pods, so the plain population count is the honest
// check: the step is done once the settlement holds RequiredAmount beavers (adults + children).
//
//   { "BeaversStepSpec": { "RequiredAmount": 1 } }

using System;
using Timberborn.BlueprintSystem;
using Timberborn.Localization;
using Timberborn.Population;
using Timberborn.TutorialSystem;

namespace Wardens
{
    public record BeaversStepSpec : ComponentSpec
    {
        [Serialize] public int RequiredAmount { get; init; }
    }

    public class BeaversStep : ITutorialStep
    {
        private const string LocKey = "Tutorial.Wardens.Beavers";
        private readonly PopulationService _population;
        private readonly ILoc _loc;
        private readonly int _required;

        public BeaversStep(PopulationService population, ILoc loc, int required)
        {
            _population = population;
            _loc = loc;
            _required = required;
        }

        private int Count() => _population.GlobalPopulationData.NumberOfBeavers;
        public string Description() => _loc.T(LocKey, Math.Min(Count(), _required), _required);
        public bool Achieved() => Count() >= _required;
    }

    public class BeaversStepDeserializer : IStepDeserializer
    {
        private readonly PopulationService _population;
        private readonly ILoc _loc;

        public BeaversStepDeserializer(PopulationService population, ILoc loc)
        {
            _population = population;
            _loc = loc;
        }

        public bool TryDeserialize(Blueprint step, out TutorialStep tutorialStep)
        {
            if (step.Specs.Length == 0 || !(step.Specs[0] is BeaversStepSpec spec))
            {
                tutorialStep = null;
                return false;
            }
            tutorialStep = TutorialStep.Create(new BeaversStep(_population, _loc, Math.Max(1, spec.RequiredAmount)));
            return true;
        }
    }
}
