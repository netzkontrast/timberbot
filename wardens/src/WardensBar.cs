// WardensBar.cs. The building bar's safety net: every Wardens building is buildable from the first frame.
//
// Every building the faction ships carries ScienceCost 0 (tools/gen_buildings.py; tools/validate.py
// fails on any other value), so the whole bar is open on every map. This service holds that rule at
// runtime even when the data forgot it: on the first poll that sees the toolbar, a building whose spec
// still carries a science cost (a stale blueprint in a deployed copy, a collection wired in without the
// rule) is unlocked through BuildingUnlockingService.UnlockIgnoringCost and its button refreshed the
// way Timberbot's /api/science/unlock does it (ToolUnlockingService.UnlockInternal), with a warning
// naming the template. `wardens_status` lists them under bar.unlocked_at_load; the list is empty when
// the data is right.
//
// It is what is left of WardensChapters.cs, whose chapter table (story beats on the tutorial line) was
// retired in iteration 05: a level's beats now hang on its tasks (WardensLevelTasks.cs). Iteration 05's
// phases (the Iron Teeth set arriving with the first beaver) grow out of this file.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Timberborn.BlockObjectTools;
using Timberborn.Buildings;
using Timberborn.GameFactionSystem;
using Timberborn.ScienceSystem;
using Timberborn.SingletonSystem;
using Timberborn.TemplateSystem;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensBar : ILoadableSingleton, IUpdatableSingleton
    {
        private const float PollSeconds = 0.5f;
        private const int CheckPolls = 120;   // give the toolbar a minute to appear, then stop looking

        private readonly FactionService _factionService;
        private readonly BuildingUnlockingService _buildingUnlocking;
        private readonly ToolButtonService _toolButtons;
        private readonly ToolUnlockingService _toolUnlocking;

        private readonly List<string> _unlockedAtLoad = new List<string>();
        private bool _enabled;
        private bool _checked;
        private int _polls;
        private int _buttons;
        private float _nextPoll;

        public WardensBar(FactionService factionService, BuildingUnlockingService buildingUnlocking,
            ToolButtonService toolButtons, ToolUnlockingService toolUnlocking)
        {
            _factionService = factionService;
            _buildingUnlocking = buildingUnlocking;
            _toolButtons = toolButtons;
            _toolUnlocking = toolUnlocking;
        }

        public void Load() => _enabled = _factionService.Current?.Id == WardensStartingPopulation.FactionId;

        // Tool buttons are built by UI singletons whose load order we do not control, so the check
        // happens on the first poll that sees them rather than in Load().
        public void UpdateSingleton()
        {
            if (!_enabled || _checked || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollSeconds;
            int buttons = 0;
            try
            {
                foreach (var toolButton in _toolButtons.ToolButtons)
                {
                    buttons++;
                    var blockObjectTool = toolButton.Tool as BlockObjectTool;
                    if (blockObjectTool == null) continue;
                    var buildingSpec = blockObjectTool.Template.GetSpec<BuildingSpec>();
                    if (buildingSpec == null || buildingSpec.ScienceCost <= 0 || _buildingUnlocking.Unlocked(buildingSpec)) continue;
                    var templateSpec = blockObjectTool.Template.GetSpec<TemplateSpec>();
                    string name = templateSpec != null ? templateSpec.TemplateName : "?";
                    _buildingUnlocking.UnlockIgnoringCost(buildingSpec);
                    _toolUnlocking.UnlockInternal(blockObjectTool, () => { });
                    _unlockedAtLoad.Add(name);
                    Debug.LogWarning($"[Wardens] bar: {name} shipped with ScienceCost {buildingSpec.ScienceCost} and was unlocked at load. " +
                                     "Every Wardens building ships with ScienceCost 0 (tools/gen_buildings.py); tools/validate.py names the file.");
                }
            }
            catch (Exception ex)
            {
                _checked = true;
                Debug.LogWarning("[Wardens] bar: check failed, not retried: " + ex.Message);
                return;
            }
            if (buttons == 0)
            {
                if (++_polls < CheckPolls) return;   // the toolbar is not built yet; look again next poll
                Debug.LogWarning("[Wardens] bar: no tool buttons seen; check skipped");
            }
            _checked = true;
            _buttons = buttons;
            Debug.Log($"[Wardens] bar: checked, {buttons} tool buttons, {_unlockedAtLoad.Count} unlocked at load");
        }

        /// For wardens_status: whether the check ran, and what it had to unlock (empty when the data is right).
        public JObject State() => new JObject
        {
            ["checked"] = _checked,
            ["tool_buttons"] = _buttons,
            ["unlocked_at_load"] = new JArray(_unlockedAtLoad.ToArray()),
        };
    }
}
