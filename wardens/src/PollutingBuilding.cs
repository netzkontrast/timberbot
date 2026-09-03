// PollutingBuilding.cs. Spike B: Wardens industry poisons the land it stands on.
//
// STATUS: stub. Attaches, ticks, and logs; does not contaminate anything yet.
//
// Why a stub: vanilla has no building-side contamination source. The soil
// simulator (SoilContaminationSimulator.StartParallelTick) recomputes soil
// contamination from the water columns every tick on a worker thread, so writing
// into its arrays directly gets overwritten. The route that survives is the one
// vanilla badwater uses: a WaterSource entity carrying WaterSourceContamination
// (SetContamination(float) is public). The plan is for this component to own a
// hidden contaminated water source under the building while it is finished and
// running, and to remove it when the building is paused or demolished.
//
// Attach to a blueprint via a modifier, e.g. on the Numbercruncher:
//   { "PollutingBuildingSpec": { "Radius": 2, "Strength": 0.5 } }
// The decorator in WardensConfigurator maps that spec to this component.

using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.BlueprintSystem;
using Timberborn.TickSystem;
using UnityEngine;

namespace Wardens
{
    public record PollutingBuildingSpec : ComponentSpec
    {
        // Tiles around the building footprint that get poisoned.
        [Serialize]
        public int Radius { get; init; }

        // 0..1, passed to WaterSourceContamination.SetContamination once wired.
        [Serialize]
        public float Strength { get; init; }
    }

    public class PollutingBuilding : TickableComponent, IAwakableComponent, IFinishedStateListener
    {
        // Don't spam the log: report once per state change.
        private bool _active;
        private bool _reported;

        private PollutingBuildingSpec _spec;
        private BlockObject _blockObject;

        public void Awake()
        {
            _spec = GetComponent<PollutingBuildingSpec>();
            _blockObject = GetComponent<BlockObject>();
        }

        public void OnEnterFinishedState()
        {
            _active = true;
            _reported = false;
        }

        public void OnExitFinishedState()
        {
            _active = false;
            _reported = false;
            // TODO(spike-b): remove the hidden water source here.
        }

        public override void Tick()
        {
            if (!_active || _reported)
                return;

            var at = _blockObject != null ? _blockObject.Coordinates : Vector3Int.zero;
            Debug.Log($"[Wardens] PollutingBuilding on {base.Name} at {at}: radius={_spec?.Radius ?? 0} strength={_spec?.Strength ?? 0f} (stub, not contaminating yet)");
            _reported = true;
            // TODO(spike-b): spawn/refresh the hidden WaterSource + WaterSourceContamination.
        }
    }
}
