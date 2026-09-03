// WardensPoweredStep.cs. "Power: <building> (n/m)" for any faction.
//
// Vanilla's PowerBuildingsTutorialStep has the right check (a finished building whose mechanical
// graph has at least one generator) but its deserializer highlights the hardcoded Folktails
// shaft buttons and throws when a faction does not have them. This is the same step with the
// shaft templates taken from the blueprint:
//
//   { "PoweredBuildingStepSpec": { "TemplateName": "Cruncher.Wardens", "RequiredAmount": 1,
//                                  "ShaftTemplateNames": ["PowerShaft.Wardens"] } }
//
// Missing tool buttons are skipped instead of crashing the whole tutorial load.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Timberborn.BlockObjectTools;
using Timberborn.BlueprintSystem;
using Timberborn.EntitySystem;
using Timberborn.Buildings;
using Timberborn.Localization;
using Timberborn.MechanicalSystem;
using Timberborn.TemplateSystem;
using Timberborn.ToolButtonSystem;
using Timberborn.TutorialSteps;
using Timberborn.TutorialSystem;
using UnityEngine;

namespace Wardens
{
    public record PoweredBuildingStepSpec : ComponentSpec
    {
        [Serialize] public string TemplateName { get; init; }
        [Serialize] public int RequiredAmount { get; init; }
        [Serialize] public ImmutableArray<string> ShaftTemplateNames { get; init; }
    }

    public class PoweredBuildingStep : ITutorialStep
    {
        private const string LocKey = "Tutorial.PowerBuilding";   // vanilla: "Power: {0} ({1}/{2})"
        private readonly BuiltBuildingService _builtBuildings;
        private readonly ILoc _loc;
        private readonly string _templateName;
        private readonly int _requiredAmount;
        private readonly string _buildingName;

        public PoweredBuildingStep(BuiltBuildingService builtBuildings, ILoc loc, string templateName, int requiredAmount, string buildingName)
        {
            _builtBuildings = builtBuildings;
            _loc = loc;
            _templateName = templateName;
            _requiredAmount = requiredAmount;
            _buildingName = buildingName;
        }

        private int Powered()
        {
            int n = 0;
            foreach (var building in _builtBuildings.GetFinishedBuildings(_templateName))
            {
                var node = building.GetComponent<MechanicalNode>();
                var graph = node != null ? node.Graph : null;
                if (graph != null && graph.NumberOfGenerators > 0) n++;
            }
            return n;
        }

        public string Description() => _loc.T(LocKey, _buildingName, Math.Min(Powered(), _requiredAmount), _requiredAmount);
        public bool Achieved() => Powered() >= _requiredAmount;
    }

    public class PoweredBuildingStepDeserializer : IStepDeserializer
    {
        private readonly BuiltBuildingService _builtBuildings;
        private readonly BuildingService _buildingService;
        private readonly ILoc _loc;
        private readonly ToolButtonService _toolButtons;

        public PoweredBuildingStepDeserializer(BuiltBuildingService builtBuildings, BuildingService buildingService, ILoc loc, ToolButtonService toolButtons)
        {
            _builtBuildings = builtBuildings;
            _buildingService = buildingService;
            _loc = loc;
            _toolButtons = toolButtons;
        }

        public bool TryDeserialize(Blueprint step, out TutorialStep tutorialStep)
        {
            if (step.Specs.Length == 0 || !(step.Specs[0] is PoweredBuildingStepSpec spec))
            {
                tutorialStep = null;
                return false;
            }
            var labeled = _buildingService.GetBuildingTemplate(spec.TemplateName).GetSpec<LabeledEntitySpec>();
            var name = _loc.T(labeled.DisplayNameLocKey);
            var inner = new PoweredBuildingStep(_builtBuildings, _loc, spec.TemplateName, spec.RequiredAmount, name);

            var buttons = new List<ToolButton>();
            if (!spec.ShaftTemplateNames.IsDefaultOrEmpty)
            {
                foreach (var shaft in spec.ShaftTemplateNames)
                {
                    try
                    {
                        buttons.Add(_toolButtons.GetToolButton((BlockObjectTool tool) => tool.Template.GetSpec<TemplateSpec>().IsNamed(shaft)));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Wardens] tutorial: no tool button for {shaft} ({ex.Message})");
                    }
                }
            }
            tutorialStep = buttons.Count > 0
                ? TutorialStep.Create(inner, _toolButtons.GetToolGroupButton(buttons[0]), buttons)
                : TutorialStep.Create(inner);
            return true;
        }
    }
}
