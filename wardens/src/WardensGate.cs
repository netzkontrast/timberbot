// WardensGate.cs. The Gate: a district on another map, linked into this one.
//
// Timberborn runs one map at a time, so a district "living in another map" cannot keep producing in
// the background. What it can do is leave a reading behind: when the player leaves a map, the mod
// records, per district, what it produced beyond its own needs, and a Gate built on a later map
// delivers that surplus every day, as if the old district were still out there working.
//
//   WardensDistrictExports   Game singleton. Samples every finished district's stock four times a
//                            game day and keeps three days of it. A district's surplus of a good is
//                            its stock growth over that window minus what Gates delivered into it,
//                            so a chain of maps never re-exports the same goods. The readings go to
//                            campaign.json (WardensCampaignService.RecordDistricts, its one writer)
//                            once a game day and on every way out: exit to menu
//                            (PreMainMenuStartedEvent), a level transition, quitting, scene unload.
//   WardensMapGate           on MapGate.Wardens. Holds one link (a district id, saved with the
//                            building) and, while finished, gives the linked district's per-day
//                            surplus into its public output inventory; the Hauling Post's haulers
//                            carry it into the district like any flag's output.
//   WardensMapGateFragment   the panel to pick the link (WardensGateFragment.cs).
//
// Game members, all read in the 1.1.2.4 decompile: DistrictCenterRegistry.FinishedDistrictCenters,
// DistrictResourceCounter.GetResourceCount(good).AllStock, IGoodService.Goods,
// IDayNightCycle.PartialDayNumber, SimpleOutputInventory.Inventory, Inventory.Gives /
// UnreservedCapacity / GiveProduced, DistrictBuilding.District, EntityComponent.EntityId,
// SettlementReferenceService.SettlementReference.

using System;
using System.Collections.Generic;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.BlueprintSystem;
using Timberborn.EntitySystem;
using Timberborn.GameDistricts;
using Timberborn.GameFactionSystem;
using Timberborn.GameWonderCompletion;
using Timberborn.Goods;
using Timberborn.MainMenuSceneLoading;
using Timberborn.Persistence;
using Timberborn.ResourceCountingSystem;
using Timberborn.SettlementNameSystem;
using Timberborn.SimpleOutputBuildings;
using Timberborn.SingletonSystem;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace Wardens
{
    public record WardensMapGateSpec : ComponentSpec
    {
    }

    public class WardensDistrictExports : ILoadableSingleton, IUpdatableSingleton, IUnloadableSingleton
    {
        private const float SampleEveryDays = 0.25f;
        private const float WindowDays = 3f;
        private const float MinSpanDays = 0.5f;      // less than half a day says nothing about a rate
        private const float MinRatePerDay = 0.05f;
        private const float PollSeconds = 0.5f;

        private sealed class Sample
        {
            public float Day;
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
            public readonly Dictionary<string, float> Imported = new Dictionary<string, float>();
        }

        private readonly DistrictCenterRegistry _districtCenterRegistry;
        private readonly IGoodService _goodService;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly SettlementReferenceService _settlementReferenceService;
        private readonly MapNameService _mapNameService;
        private readonly FactionService _factionService;
        private readonly EventBus _eventBus;
        private readonly WardensCampaignService _campaign;

        private readonly Dictionary<string, List<Sample>> _samples = new Dictionary<string, List<Sample>>();
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        private readonly Dictionary<string, Dictionary<string, float>> _imported = new Dictionary<string, Dictionary<string, float>>();
        private bool _enabled;
        private float _nextSampleDay = -1f;
        private int _lastPublishedDay = -1;
        private float _nextPoll;

        public WardensDistrictExports(DistrictCenterRegistry districtCenterRegistry, IGoodService goodService,
            IDayNightCycle dayNightCycle, SettlementReferenceService settlementReferenceService,
            MapNameService mapNameService, FactionService factionService, EventBus eventBus,
            WardensCampaignService campaign)
        {
            _districtCenterRegistry = districtCenterRegistry;
            _goodService = goodService;
            _dayNightCycle = dayNightCycle;
            _settlementReferenceService = settlementReferenceService;
            _mapNameService = mapNameService;
            _factionService = factionService;
            _eventBus = eventBus;
            _campaign = campaign;
        }

        // Read when used, not at Load: the order two singletons load in is not ours to choose, and a
        // reading keyed by an empty settlement would be linkable from its own map.
        public string Settlement => _settlementReferenceService.SettlementReference?.SettlementName ?? "";
        private string Map => _mapNameService.HasMapName ? _mapNameService.Name : "";

        /// Districts a Gate here may link: every recorded one that is not on this map's settlement.
        public List<WardensDistrictExport> Linkable()
        {
            var here = Settlement;
            var list = new List<WardensDistrictExport>();
            foreach (var d in _campaign.Districts)
                if (d.Settlement != here) list.Add(d);
            return list;
        }

        public void Load()
        {
            _enabled = _factionService.Current?.Id == WardensStartingPopulation.FactionId;
            if (!_enabled) return;
            _eventBus.Register(this);
            Application.quitting += Flush;
        }

        public void UpdateSingleton()
        {
            if (!_enabled || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollSeconds;
            try
            {
                float now = _dayNightCycle.PartialDayNumber;
                if (now < _nextSampleDay) return;
                TakeSample(now);
                _nextSampleDay = now + SampleEveryDays;
                if ((int)now > _lastPublishedDay)
                {
                    _lastPublishedDay = (int)now;
                    Publish(save: true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] district exports: " + ex.Message);
            }
        }

        /// A way out of this map: record the latest reading now. Idempotent; safe while entities live.
        public void Flush()
        {
            if (!_enabled) return;
            try
            {
                TakeSample(_dayNightCycle.PartialDayNumber);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] district exports: final sample failed (" + ex.Message + "); keeping the last one");
            }
            Publish(save: true);
        }

        [OnEvent]
        public void OnPreMainMenuStarted(PreMainMenuStartedEvent preMainMenuStartedEvent) => Flush();

        public void Unload()
        {
            Application.quitting -= Flush;
            // Loading another save posts no menu event; the entities may already be going, so no new
            // sample here, only what the last one measured.
            if (_enabled) Publish(save: true);
        }

        /// A Gate delivered goods into this district: not this district's own production.
        public void NoteImported(DistrictCenter district, string goodId, int amount)
        {
            var id = district != null ? Key(district) : null;
            if (id == null)
                foreach (var dc in _districtCenterRegistry.FinishedDistrictCenters) { id = Key(dc); break; }
            if (id == null) return;
            if (!_imported.TryGetValue(id, out var goods)) _imported[id] = goods = new Dictionary<string, float>();
            goods.TryGetValue(goodId, out var had);
            goods[goodId] = had + amount;
        }

        private string Key(DistrictCenter district) =>
            Settlement + "|" + district.GetComponent<EntityComponent>().EntityId.ToString("N");

        private void TakeSample(float now)
        {
            if (string.IsNullOrEmpty(Settlement)) return;      // not named yet: nothing to key a reading by
            foreach (var dc in _districtCenterRegistry.FinishedDistrictCenters)
            {
                var counter = dc.GetComponent<DistrictResourceCounter>();
                if (counter == null) continue;
                var id = Key(dc);
                _names[id] = dc.DistrictName;
                var sample = new Sample { Day = now };
                foreach (var good in _goodService.Goods)
                    sample.Stock[good] = counter.GetResourceCount(good).AllStock;
                if (_imported.TryGetValue(id, out var imported))
                    foreach (var pair in imported) sample.Imported[pair.Key] = pair.Value;
                if (!_samples.TryGetValue(id, out var list)) _samples[id] = list = new List<Sample>();
                list.Add(sample);
                // Keep the newest sample at or before the window's start, so the span is the window.
                while (list.Count > 2 && list[1].Day <= now - WindowDays) list.RemoveAt(0);
            }
        }

        private void Publish(bool save)
        {
            var settlement = Settlement;
            var map = Map;
            var readings = new List<WardensDistrictExport>();
            foreach (var pair in _samples)
            {
                var list = pair.Value;
                if (list.Count < 2) continue;
                var first = list[0];
                var last = list[list.Count - 1];
                float span = last.Day - first.Day;
                if (span < MinSpanDays) continue;
                var reading = new WardensDistrictExport
                {
                    Id = pair.Key,
                    Settlement = settlement,
                    District = _names.TryGetValue(pair.Key, out var name) ? name : "",
                    Map = map,
                    Level = WardensCampaignService.ByMapName(map)?.Id ?? "",
                    Day = last.Day,
                    SavedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                };
                foreach (var stock in last.Stock)
                {
                    first.Stock.TryGetValue(stock.Key, out var before);
                    last.Imported.TryGetValue(stock.Key, out var importedNow);
                    first.Imported.TryGetValue(stock.Key, out var importedBefore);
                    float rate = ((stock.Value - before) - (importedNow - importedBefore)) / span;
                    if (rate >= MinRatePerDay) reading.Exports[stock.Key] = rate;
                }
                readings.Add(reading);
            }
            if (readings.Count == 0) return;
            _campaign.RecordDistricts(readings, save);
        }
    }

    public class WardensMapGate : TickableComponent, IAwakableComponent, IFinishedStateListener, IPersistentEntity
    {
        private static readonly ComponentKey GateKey = new ComponentKey("WardensMapGate");
        private static readonly PropertyKey<string> LinkKey = new PropertyKey<string>("Link");

        private readonly WardensCampaignService _campaign;
        private readonly WardensDistrictExports _exports;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly Dictionary<string, float> _carry = new Dictionary<string, float>();
        private SimpleOutputInventory _output;
        private DistrictBuilding _districtBuilding;
        private float _lastDay = -1f;

        public WardensMapGate(WardensCampaignService campaign, WardensDistrictExports exports, IDayNightCycle dayNightCycle)
        {
            _campaign = campaign;
            _exports = exports;
            _dayNightCycle = dayNightCycle;
        }

        public string LinkId { get; private set; } = "";
        public WardensDistrictExport Link => _campaign.FindDistrict(LinkId);
        public List<WardensDistrictExport> Linkable() => _exports.Linkable();

        public void Awake()
        {
            _output = GetComponent<SimpleOutputInventory>();
            _districtBuilding = GetComponent<DistrictBuilding>();
            DisableComponent();
        }

        public void OnEnterFinishedState()
        {
            _lastDay = -1f;
            EnableComponent();
        }

        public void OnExitFinishedState() => DisableComponent();

        /// The next linkable district after the current one, round the list; nothing to link, no change.
        public void LinkNext()
        {
            var list = Linkable();
            if (list.Count == 0) return;
            int at = list.FindIndex(d => d.Id == LinkId);
            SetLink(list[(at + 1) % list.Count].Id);
        }

        public void Unlink() => SetLink("");

        private void SetLink(string id)
        {
            if (id == LinkId) return;
            LinkId = id ?? "";
            _carry.Clear();
            var link = Link;
            Debug.Log(link == null
                ? "[Wardens] gate: unlinked"
                : $"[Wardens] gate: linked to {link.District} of {link.Settlement} ({link.Exports.Count} goods)");
        }

        public override void Tick()
        {
            float now = _dayNightCycle.PartialDayNumber;
            var link = Link;
            var inventory = _output != null ? _output.Inventory : null;
            if (_lastDay < 0f || link == null || inventory == null || !inventory.Enabled)
            {
                _lastDay = now;
                return;
            }
            // Clamped: a long pause or a load never pays out a lump.
            float elapsed = Mathf.Clamp(now - _lastDay, 0f, 1f);
            _lastDay = now;
            foreach (var export in link.Exports)
            {
                if (!inventory.Gives(export.Key)) continue;      // a good this faction does not have
                _carry.TryGetValue(export.Key, out var carry);
                carry += export.Value * elapsed;
                int whole = (int)carry;
                if (whole > 0)
                {
                    int given = Math.Min(whole, inventory.UnreservedCapacity(export.Key));
                    if (given > 0)
                    {
                        inventory.GiveProduced(new GoodAmount(export.Key, given));
                        _exports.NoteImported(_districtBuilding != null ? _districtBuilding.District : null, export.Key, given);
                    }
                    carry -= whole;     // a full Gate wastes the surplus rather than owing it
                }
                _carry[export.Key] = carry;
            }
        }

        public void Save(IEntitySaver entitySaver)
        {
            if (!string.IsNullOrEmpty(LinkId)) entitySaver.GetComponent(GateKey).Set(LinkKey, LinkId);
        }

        public void Load(IEntityLoader entityLoader)
        {
            if (entityLoader.TryGetComponent(GateKey, out var loader) && loader.Has(LinkKey))
                LinkId = loader.Get(LinkKey) ?? "";
        }
    }
}
