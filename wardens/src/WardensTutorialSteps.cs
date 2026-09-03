// WardensTutorialSteps.cs. Two goal-type tutorial steps vanilla does not have.
//
// The vanilla tutorial engine (Timberborn.TutorialSystem) turns each child blueprint of a
// TutorialStageSpec into an ITutorialStep through a multibound IStepDeserializer. Vanilla
// ships "build N of X", "select X", camera and speed steps, but no "have N of a good in
// stock" and nothing about bots. Chapter goals need both, so they live here:
//
//   GoodStockStepSpec   { "GoodId": "ScrapMetal", "Amount": 10 }   stock across all districts
//   BotsChargedStepSpec { "MinEnergy": 0.5 }                        every bot's Energy need >= value
//
// Both are plain JSON in Tutorials/Stages/*.blueprint.json, exactly like the vanilla steps,
// and show up in the vanilla tutorial panel with a localized "x/y" description.

using System;
using System.Collections.Generic;
using Timberborn.BlueprintSystem;
using Timberborn.Bots;
using Timberborn.Characters;
using Timberborn.GameDistricts;
using Timberborn.Localization;
using Timberborn.NeedSystem;
using Timberborn.ResourceCountingSystem;
using Timberborn.TutorialSystem;
using UnityEngine;

namespace Wardens
{
    public record GoodStockStepSpec : ComponentSpec
    {
        [Serialize] public string GoodId { get; init; }
        [Serialize] public int Amount { get; init; }
    }

    public record BotsChargedStepSpec : ComponentSpec
    {
        [Serialize] public float MinEnergy { get; init; }
    }

    public class GoodStockStep : ITutorialStep
    {
        private readonly DistrictCenterRegistry _districts;
        private readonly ILoc _loc;
        private readonly string _goodId;
        private readonly int _amount;
        private readonly string _goodName;

        public GoodStockStep(DistrictCenterRegistry districts, ILoc loc, string goodId, int amount)
        {
            _districts = districts;
            _loc = loc;
            _goodId = goodId;
            _amount = amount;
            // Vanilla convention: Good.<Id>.PluralDisplayName (see Goods/*.blueprint.json).
            _goodName = _loc.T($"Good.{goodId}.PluralDisplayName");
        }

        public string Description() =>
            _loc.T("Tutorial.Wardens.GoodStock", _goodName, Math.Min(Stock(), _amount), _amount);

        public bool Achieved() => Stock() >= _amount;

        private int Stock()
        {
            int total = 0;
            foreach (var dc in _districts.AllDistrictCenters)
            {
                var counter = dc.GetComponent<DistrictResourceCounter>();
                if (counter != null)
                    total += counter.GetResourceCount(_goodId).AllStock;
            }
            return total;
        }
    }

    public class BotsChargedStep : ITutorialStep
    {
        private const string EnergyNeedId = "Energy";
        private readonly CharacterPopulation _population;
        private readonly ILoc _loc;
        private readonly float _minEnergy;

        public BotsChargedStep(CharacterPopulation population, ILoc loc, float minEnergy)
        {
            _population = population;
            _loc = loc;
            _minEnergy = minEnergy;
        }

        public string Description()
        {
            Count(out int charged, out int total);
            return _loc.T("Tutorial.Wardens.BotsCharged", Mathf.RoundToInt(_minEnergy * 100f), charged, total);
        }

        public bool Achieved()
        {
            Count(out int charged, out int total);
            return total > 0 && charged == total;
        }

        public static float? Energy(NeedManager needs)
        {
            if (needs == null) return null;
            foreach (var spec in needs.GetNeeds())
                if (spec.Id == EnergyNeedId)
                    return needs.GetNeed(spec.Id).Points;
            return null;
        }

        private void Count(out int charged, out int total)
        {
            charged = 0;
            total = 0;
            foreach (var character in _population.Characters)
            {
                if (character.GetComponent<Bot>() == null) continue;
                var energy = Energy(character.GetComponent<NeedManager>());
                if (energy == null) continue;
                total++;
                if (energy.Value >= _minEnergy) charged++;
            }
        }
    }

    public class GoodStockStepDeserializer : IStepDeserializer
    {
        private readonly DistrictCenterRegistry _districts;
        private readonly ILoc _loc;

        public GoodStockStepDeserializer(DistrictCenterRegistry districts, ILoc loc)
        {
            _districts = districts;
            _loc = loc;
        }

        public bool TryDeserialize(Blueprint step, out TutorialStep tutorialStep)
        {
            if (step.Specs.Length > 0 && step.Specs[0] is GoodStockStepSpec spec)
            {
                tutorialStep = TutorialStep.Create(new GoodStockStep(_districts, _loc, spec.GoodId, spec.Amount));
                return true;
            }
            tutorialStep = null;
            return false;
        }
    }

    public class BotsChargedStepDeserializer : IStepDeserializer
    {
        private readonly CharacterPopulation _population;
        private readonly ILoc _loc;

        public BotsChargedStepDeserializer(CharacterPopulation population, ILoc loc)
        {
            _population = population;
            _loc = loc;
        }

        public bool TryDeserialize(Blueprint step, out TutorialStep tutorialStep)
        {
            if (step.Specs.Length > 0 && step.Specs[0] is BotsChargedStepSpec spec)
            {
                tutorialStep = TutorialStep.Create(new BotsChargedStep(_population, _loc, spec.MinEnergy));
                return true;
            }
            tutorialStep = null;
            return false;
        }
    }
}
