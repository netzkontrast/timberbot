// WardensFrames.cs. The Warden's heartbeat: one sensor frame per N game ticks, or sooner on an event.
//
// design/wardens-play.md §6. The agent plays through the MCP `frame` tool: it long-polls here, and
// every `every_ticks` game ticks (ITickableSingleton.Tick, so a paused game produces no frames on its
// own) or whenever something happens (a day starts, a building finishes, a chapter opens, a beaver is
// born, a cutscene starts or ends, the human types) the main thread assembles a compact frame: time,
// population and charge, the archive (Data Cores) and science, chapter, cutscene and open tutorial
// steps, what the human has selected,
// the camera pose and how long the human has left it alone, the events since the last frame, and an
// `attention` list: where to look first, in priority order, with world positions the `camera` and
// `point` tools accept. The agent never polls the read API to find out whether anything changed.
//
// Threading follows WardensChat: the main thread publishes under a lock and pulses; the listener
// thread waits on the lock in short slices so a chat message (kept in WardensChat's own store) can
// wake it too. Events are collected on the main thread ([OnEvent] handlers run there).

using System;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using Timberborn.Beavers;
using Timberborn.BlockSystem;
using Timberborn.Bots;
using Timberborn.Characters;
using Timberborn.EntitySystem;
using Timberborn.GameCycleSystem;
using Timberborn.GameDistricts;
using Timberborn.HazardousWeatherSystem;
using Timberborn.NeedSystem;
using Timberborn.Population;
using Timberborn.ScienceSystem;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;
using Timberborn.StatusSystem;
using Timberborn.TemplateSystem;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using Timberborn.TutorialSystem;
using Timberborn.WeatherSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensFrames : ILoadableSingleton, ITickableSingleton, IUpdatableSingleton
    {
        public const int DefaultEveryTicks = 60;
        public const int MinEveryTicks = 5;
        public const int MaxEveryTicks = 2000;
        private const float LowEnergy = 0.35f;
        private const string DataCoreId = "DataCore";

        private readonly EventBus _eventBus;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly GameCycleService _cycles;
        private readonly WeatherService _weather;
        private readonly SpeedManager _speedManager;
        private readonly PopulationService _populationService;
        private readonly CharacterPopulation _population;
        private readonly DistrictCenterRegistry _districts;
        private readonly ScienceService _science;
        private readonly TutorialService _tutorialService;
        private readonly EntitySelectionService _selection;
        private readonly WardensCameraDirector _director;
        private readonly WardensChat _chat;
        private readonly WardensChapterService _chapters;
        private readonly WardensCutscenes _cutscenes;

        private readonly object _lock = new object();
        private JObject _latest;
        private long _seq;

        // main thread only
        private readonly List<string> _events = new List<string>();
        private readonly List<JObject> _spots = new List<JObject>();   // positions attached to events
        private long _tick;
        private long _lastFrameTick;
        private volatile int _everyTicks = DefaultEveryTicks;
        private bool _pending;
        private int _lastDay = -1;
        private int _lastBots = -1;
        private int _lastBeavers = -1;
        private int _lastChaptersComplete = -1;
        private WardensCameraDirector.Keyframe _lastPose;
        private float _poseChangedAt;
        private string _lastSelection;
        private string _lastCutscene;

        public WardensFrames(EventBus eventBus, IDayNightCycle dayNightCycle, GameCycleService cycles,
            WeatherService weather, SpeedManager speedManager, PopulationService populationService,
            CharacterPopulation population, DistrictCenterRegistry districts, ScienceService science,
            TutorialService tutorialService, EntitySelectionService selection, WardensCameraDirector director,
            WardensChat chat, WardensChapterService chapters, WardensCutscenes cutscenes)
        {
            _eventBus = eventBus;
            _dayNightCycle = dayNightCycle;
            _cycles = cycles;
            _weather = weather;
            _speedManager = speedManager;
            _populationService = populationService;
            _population = population;
            _districts = districts;
            _science = science;
            _tutorialService = tutorialService;
            _selection = selection;
            _director = director;
            _chat = chat;
            _chapters = chapters;
            _cutscenes = cutscenes;
        }

        public long Seq { get { lock (_lock) return _seq; } }
        public int EveryTicks => _everyTicks;

        public void Load()
        {
            _eventBus.Register(this);
            _lastPose = _director.Current();
            _poseChangedAt = Time.unscaledTime;
        }

        public void SetEveryTicks(int ticks) => _everyTicks = Mathf.Clamp(ticks, MinEveryTicks, MaxEveryTicks);

        // ---- the clock (main thread, game ticks: stops while paused) ----------------------------

        public void Tick()
        {
            _tick++;
            if (_tick - _lastFrameTick >= _everyTicks) _pending = true;
        }

        // ---- events (main thread) -------------------------------------------------------------

        private void Note(string what, Vector3? at = null)
        {
            _events.Add(what);
            if (at != null) _spots.Add(new JObject { ["what"] = what, ["at"] = WardensCameraDirector.Vec(at.Value) });
            _pending = true;
        }

        [OnEvent] public void OnDayStart(DaytimeStartEvent e) => Note("day.start");
        [OnEvent] public void OnNightStart(NighttimeStartEvent e) => Note("night.start");
        [OnEvent] public void OnCycleDay(CycleDayStartedEvent e) => Note($"cycle.day:{_cycles.Cycle}/{_cycles.CycleDay}");
        [OnEvent] public void OnCycleEnd(CycleEndedEvent e) => Note($"cycle.end:{_cycles.Cycle}");
        [OnEvent] public void OnHazardStart(HazardousWeatherStartedEvent e) => Note("hazard.start");
        [OnEvent] public void OnHazardEnd(HazardousWeatherEndedEvent e) => Note("hazard.end");
        [OnEvent] public void OnPopulationChanged(PopulationChangedEvent e) => Note("population.changed");
        [OnEvent] public void OnBeaverBorn(BeaverBornEvent e) => Note("beaver.born");
        [OnEvent] public void OnCharacterKilled(CharacterKilledEvent e) => Note("character.killed");
        [OnEvent] public void OnBuildingUnlocked(BuildingUnlockedEvent e) => Note("building.unlocked");
        [OnEvent] public void OnStatusAlert(StatusAlertAddedEvent e) => Note("status.alert");
        [OnEvent] public void OnSpeedChanged(CurrentSpeedChangedEvent e) => Note($"speed:{_speedManager.CurrentSpeed}");

        [OnEvent]
        public void OnBuildingFinished(EnteredFinishedStateEvent e)
        {
            try
            {
                var block = e.BlockObject;
                var template = block != null ? block.GetComponent<TemplateSpec>() : null;
                var name = template != null ? template.TemplateName : "?";
                Vector3? at = block != null ? (Vector3?)block.Transform.position : null;
                Note($"building.finished:{name}@{(block != null ? Coords(block.Coordinates) : "?")}", at);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] frames: building.finished: " + ex.Message);
            }
        }

        private static string Coords(Vector3Int c) => $"{c.x},{c.y},{c.z}";

        // ---- publishing (main thread, every frame) -----------------------------------------------

        public void UpdateSingleton()
        {
            try
            {
                WatchHuman();
                var complete = ChaptersComplete();
                if (_lastChaptersComplete >= 0 && complete > _lastChaptersComplete) Note("chapter.open");
                _lastChaptersComplete = complete;
                var cutscene = _cutscenes.Playing ? _cutscenes.CurrentId : null;
                if (cutscene != _lastCutscene)
                {
                    if (_lastCutscene != null) Note("cutscene.end:" + _lastCutscene);
                    if (cutscene != null) Note("cutscene.start:" + cutscene);
                    _lastCutscene = cutscene;
                }
                if (!_pending) return;
                var frame = Build();
                lock (_lock)
                {
                    _seq++;
                    frame["seq"] = _seq;
                    _latest = frame;
                    Monitor.PulseAll(_lock);
                }
                _pending = false;
                _lastFrameTick = _tick;
                _events.Clear();
                _spots.Clear();
            }
            catch (Exception ex)
            {
                _pending = false;
                _events.Clear();
                _spots.Clear();
                Debug.LogWarning("[Wardens] frames: " + ex.Message);
            }
        }

        private void WatchHuman()
        {
            var pose = _director.Current();
            if (!_director.IsFlying && (pose.Target != _lastPose.Target || pose.H != _lastPose.H || pose.V != _lastPose.V || pose.Zoom != _lastPose.Zoom))
            {
                _lastPose = pose;
                _poseChangedAt = Time.unscaledTime;
            }
            var sel = _selection.IsAnythingSelected ? _selection.SelectedObject.GetComponent<TemplateSpec>()?.TemplateName ?? "?" : null;
            if (sel != _lastSelection)
            {
                _lastSelection = sel;
                _poseChangedAt = Time.unscaledTime;
                if (sel != null) Note("selection:" + sel);
            }
        }

        private int ChaptersComplete()
        {
            int n = 0;
            foreach (var chapter in WardensChapterService.Chapters)
                if (_chapters.IsComplete(chapter)) n++;
            return n;
        }

        private JObject Build()
        {
            var frame = new JObject
            {
                ["tick"] = _tick,
                ["ticks_since_last"] = _tick - _lastFrameTick,
                ["every_ticks"] = _everyTicks,
                ["speed"] = _speedManager.CurrentSpeed,
                ["paused"] = _speedManager.CurrentSpeed <= 0f,
                ["day"] = _dayNightCycle.DayNumber,
                ["day_progress"] = (float)_dayNightCycle.DayProgress,
                ["cycle"] = (int)_cycles.Cycle,
                ["cycle_day"] = _cycles.CycleDay,
                ["hazardous"] = _weather.IsHazardousWeather,
                ["events"] = new JArray(_events),
            };

            // population and charge
            int bots = 0, beavers = 0, withEnergy = 0;
            float sum = 0f, min = 1f;
            var low = new JArray();
            foreach (var c in _population.Characters)
            {
                if (c.GetComponent<Bot>() == null) { beavers++; continue; }
                bots++;
                var energy = BotsChargedStep.Energy(c.GetComponent<NeedManager>());
                if (energy == null) continue;
                withEnergy++;
                sum += energy.Value;
                if (energy.Value < min) min = energy.Value;
                if (energy.Value < LowEnergy && low.Count < 5)
                {
                    var entity = c.GetComponent<EntityComponent>();
                    low.Add(new JObject
                    {
                        ["entityId"] = entity != null ? entity.EntityId.ToString() : null,
                        ["energy"] = energy.Value,
                        ["at"] = WardensCameraDirector.Vec(c.Transform.position),
                    });
                }
            }
            var data = _populationService.GlobalPopulationData;
            frame["bots"] = new JObject
            {
                ["count"] = bots,
                ["energy_min"] = withEnergy > 0 ? (float?)min : null,
                ["energy_avg"] = withEnergy > 0 ? (float?)(sum / withEnergy) : null,
                ["unemployed"] = data.BotWorkplaceData.Unemployed,
                ["low"] = low,
            };
            frame["beavers"] = beavers;

            // the archive
            int archive = 0;
            foreach (var dc in _districts.AllDistrictCenters)
            {
                var counter = dc.GetComponent<DistrictResourceCounter>();
                if (counter != null) archive += counter.GetResourceCount(DataCoreId).AllStock;
            }
            frame["archive"] = archive;
            frame["science"] = _science.SciencePoints;

            // story
            frame["chapter"] = _chapters.Summary();
            frame["cutscene"] = _cutscenes.Summary();
            var open = new JArray();
            foreach (var kv in _tutorialService._activeTutorialStages)
            {
                foreach (var step in kv.Value.TutorialSteps)
                {
                    bool achieved;
                    string text;
                    try { achieved = step.Step.Achieved(); } catch { achieved = false; }
                    if (achieved) continue;
                    try { text = step.Step.Description(); } catch (Exception ex) { text = "? " + ex.Message; }
                    open.Add(new JObject { ["tutorial"] = kv.Key, ["stage"] = kv.Value.Id, ["step"] = text });
                }
            }
            frame["open_steps"] = open;

            // the human
            JObject selection = null;
            if (_selection.IsAnythingSelected)
            {
                var so = _selection.SelectedObject;
                selection = new JObject { ["template"] = so.GetComponent<TemplateSpec>()?.TemplateName };
                var block = so.GetComponent<BlockObject>();
                if (block != null) selection["coords"] = new JObject { ["x"] = block.Coordinates.x, ["y"] = block.Coordinates.y, ["z"] = block.Coordinates.z };
                selection["at"] = WardensCameraDirector.Vec(so.Transform.position);
            }
            frame["selection"] = selection;
            frame["camera"] = _director.State();
            frame["human"] = new JObject
            {
                ["idle_seconds"] = Time.unscaledTime - _poseChangedAt,
                ["unread"] = _chat.UndeliveredCount(),
            };

            // deltas since the previous frame
            var since = new JObject();
            if (_lastDay >= 0 && _dayNightCycle.DayNumber != _lastDay) since["day_changed"] = true;
            if (_lastBots >= 0 && bots < _lastBots) since["bots_lost"] = _lastBots - bots;
            if (_lastBeavers >= 0 && beavers > _lastBeavers) since["beavers_born"] = beavers - _lastBeavers;
            _lastDay = _dayNightCycle.DayNumber;
            _lastBots = bots;
            _lastBeavers = beavers;
            frame["since"] = since;

            // where to look, in order
            var attention = new JArray();
            if (_cutscenes.Playing)
            {
                bool choice = (string)frame["cutscene"]["waiting"] == WardensCutsceneScript.WaitChoice;
                attention.Add(new JObject
                {
                    ["what"] = "cutscene",
                    ["why"] = choice ? "a choice card is open: the human answers it, not you" : "a scene is playing: say nothing, leave the camera",
                });
            }
            if (_chat.UndeliveredCount() > 0)
                attention.Add(new JObject { ["what"] = "chat", ["why"] = "the human spoke; answer first" });
            foreach (var b in low)
                attention.Add(new JObject { ["what"] = "bot.low_energy", ["why"] = $"energy {(float)b["energy"]:0.00}; a stopped Warden does not get up", ["at"] = b["at"] });
            foreach (var spot in _spots)
                attention.Add(new JObject { ["what"] = spot["what"], ["why"] = "just happened", ["at"] = spot["at"] });
            if (selection != null)
                attention.Add(new JObject { ["what"] = "selection", ["why"] = "the human is pointing at this", ["at"] = selection["at"] });
            if (open.Count > 0)
                attention.Add(new JObject { ["what"] = "tutorial.step", ["why"] = (string)open[0]["step"] });
            var next = (string)frame["chapter"]["next"];
            if (next != null)
                attention.Add(new JObject { ["what"] = "chapter.next", ["why"] = $"{next} waits for {(string)frame["chapter"]["next_waits_for"]}" });
            frame["attention"] = attention;
            return frame;
        }

        // ---- the tool (listener thread) --------------------------------------------------------------

        /// Returns the first frame with seq > after, waiting up to timeoutMs. Wakes early when the
        /// human types (the frame then carries `stale: true` if no new frame was published).
        public JObject Wait(long after, int timeoutMs)
        {
            var deadline = Environment.TickCount + Math.Max(0, timeoutMs);
            lock (_lock)
            {
                while (true)
                {
                    if (_latest != null && _seq > after) return (JObject)_latest.DeepClone();
                    int remaining = deadline - Environment.TickCount;
                    if (remaining <= 0 || _chat.UndeliveredCount() > 0)
                    {
                        var stale = _latest != null ? (JObject)_latest.DeepClone() : new JObject { ["seq"] = _seq };
                        stale["stale"] = true;
                        stale["timed_out"] = remaining <= 0;
                        return stale;
                    }
                    Monitor.Wait(_lock, Math.Min(remaining, 250));
                }
            }
        }
    }
}
