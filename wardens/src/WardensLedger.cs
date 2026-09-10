// WardensLedger.cs. The Ledger in one call: poisoned, healed, green, archive, born, bots.
//
// design/wardens-play.md §2 and §6 item 1; WARDEN.md "The Ledger". The agent used to compute the
// daily line from GET /api/tiles through the passthrough (9,216 cells of JSON a day on a 96x96 map)
// and do the arithmetic itself. This counts the same things on the main thread, from the same
// services the tiles route reads (TimberbotReadV2.CollectTiles: ITerrainService.Size, the column
// terrain map's ceilings, ISoilContaminationService.SoilIsContaminated, ISoilMoistureService.SoilIsMoist),
// keeps the previous call's poisoned set so `healed` and `poisoned_new` exist, and formats the line
// WARDEN.md prescribes.
//
// Main thread only: the MCP tool that calls Compute() is queued, never OffThread, because the soil
// services are not on the verified thread-safe list (docs/audit/contradictions.md, C14).
// Session memory only: a reload starts without a previous set, so the first line of a session has no
// deltas. Persisting it is the Archive-in-the-save step (wardens-play.md §6 item 3), not this one.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Timberborn.Bots;
using Timberborn.Characters;
using Timberborn.GameDistricts;
using Timberborn.MapIndexSystem;
using Timberborn.NeedSystem;
using Timberborn.ResourceCountingSystem;
using Timberborn.SoilContaminationSystem;
using Timberborn.SoilMoistureSystem;
using Timberborn.TerrainSystem;
using Timberborn.TimeSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensLedger
    {
        private const string DataCoreId = "DataCore";
        private const float Charged = 0.35f;   // WardensFrames.LowEnergy: under it a Warden is "low"
        private const int NewlyPoisonedLimit = 5;

        private readonly ITerrainService _terrainService;
        private readonly IThreadSafeColumnTerrainMap _terrainMap;
        private readonly MapIndexService _mapIndex;
        private readonly ISoilContaminationService _contamination;
        private readonly ISoilMoistureService _moisture;
        private readonly CharacterPopulation _population;
        private readonly DistrictCenterRegistry _districts;
        private readonly IDayNightCycle _dayNightCycle;

        private HashSet<int> _previousPoisoned;
        private JObject _previous;

        public WardensLedger(ITerrainService terrainService, IThreadSafeColumnTerrainMap terrainMap,
            MapIndexService mapIndex, ISoilContaminationService contamination, ISoilMoistureService moisture,
            CharacterPopulation population, DistrictCenterRegistry districts, IDayNightCycle dayNightCycle)
        {
            _terrainService = terrainService;
            _terrainMap = terrainMap;
            _mapIndex = mapIndex;
            _contamination = contamination;
            _moisture = moisture;
            _population = population;
            _districts = districts;
            _dayNightCycle = dayNightCycle;
        }

        /// The entry: {day, poisoned, healed, green, archive, born, bots, bots_charged, poisoned_new,
        /// delta, line}. Main thread only.
        public JObject Compute()
        {
            var size = _terrainService.Size;
            int stride = _mapIndex.VerticalStride;
            var poisoned = new HashSet<int>();
            var newlyPoisoned = new JArray();
            int green = 0;
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    int index2D = _mapIndex.CellToIndex(new Vector2Int(x, y));
                    int columns = _terrainMap.ColumnCounts[index2D];
                    if (columns == 0) continue;
                    int top = _terrainMap.GetColumnCeiling((columns - 1) * stride + index2D);
                    var cell = new Vector3Int(x, y, top);
                    if (_contamination.SoilIsContaminated(cell))
                    {
                        poisoned.Add(index2D);
                        if (_previousPoisoned != null && !_previousPoisoned.Contains(index2D) && newlyPoisoned.Count < NewlyPoisonedLimit)
                            newlyPoisoned.Add(new JObject { ["x"] = x, ["y"] = y, ["z"] = top });
                    }
                    else if (_moisture.SoilIsMoist(cell)) green++;
                }
            }
            int healed = 0;
            if (_previousPoisoned != null)
                foreach (int i in _previousPoisoned)
                    if (!poisoned.Contains(i)) healed++;

            int bots = 0, charged = 0, beavers = 0;
            foreach (var c in _population.Characters)
            {
                if (c.GetComponent<Bot>() == null) { beavers++; continue; }
                bots++;
                var energy = BotsChargedStep.Energy(c.GetComponent<NeedManager>());
                if (energy != null && energy.Value >= Charged) charged++;
            }
            int archive = 0;
            foreach (var dc in _districts.AllDistrictCenters)
            {
                var counter = dc.GetComponent<DistrictResourceCounter>();
                if (counter != null) archive += counter.GetResourceCount(DataCoreId).AllStock;
            }

            var entry = new JObject
            {
                ["day"] = _dayNightCycle.DayNumber,
                ["poisoned"] = poisoned.Count,
                ["healed"] = healed,
                ["green"] = green,
                ["archive"] = archive,
                ["born"] = beavers,
                ["bots"] = bots,
                ["bots_charged"] = charged,
                ["poisoned_new"] = newlyPoisoned,
            };
            entry["delta"] = Delta(entry, _previous);
            entry["line"] = Line(entry);
            _previousPoisoned = poisoned;
            _previous = entry;
            return entry;
        }

        private static JObject Delta(JObject now, JObject before)
        {
            var d = new JObject();
            foreach (var key in new[] { "poisoned", "green", "archive", "born" })
                d[key] = before == null ? (int?)null : (int)now[key] - (int)before[key];
            return d;
        }

        // D12  poisoned 214 (+8)  healed 0  green 31 (-3)  archive 9 (+3)  born 0  bots 5/5 charged
        private static string Line(JObject e)
        {
            var d = (JObject)e["delta"];
            string With(string key) => d[key] == null || d[key].Type == JTokenType.Null
                ? $"{e[key]}" : $"{e[key]} ({(int)d[key]:+0;-0;0})";
            return $"D{e["day"]}  poisoned {With("poisoned")}  healed {e["healed"]}  green {With("green")}  " +
                   $"archive {With("archive")}  born {With("born")}  bots {e["bots_charged"]}/{e["bots"]} charged";
        }
    }
}
