// WardensCampaign.cs. Which level is this, and what does the campaign remember?
//
// design/wardens-campaign-maps.md §3 and §4. Timberborn has no campaign, scenario or objective
// system: "level" is our word. A level is a map plus the tasks that run on it, and because
// every map is a new save, everything the campaign remembers across levels has to live outside the
// save. That file is campaign.json, next to settings.json in the mod folder.
//
// Three jobs:
//   1. Which level am I in? MapNameService.Name is the map file's name (verified: it comes from
//      MapFileReference.Name and is persisted with the save), so the level table is keyed by map
//      name. A map that is not in the table means "not a campaign level" and this service goes
//      quiet: a Wardens game on someone else's map is a perfectly good game.
//   2. When is it done? When its tasks are (Levels/<id>.tasks.json, WardensLevelTasks.cs), with the
//      vanilla tutorial on or off. Level 01 also names the tutorial whose completion ends it (the
//      first pod-born beaver, Wardens.MoreBeavers); TutorialService keeps the finished ids and
//      persists them with the save, so this service polls that set twice a second.
//   3. What crosses over? campaign.json: the levels completed, the current level, and the Ledger
//      the Warden appends to (WARDEN.md, "The Ledger"). Goods do not cross over; vanilla starts a
//      new map empty and NewGameMode only covers food and water.
//
// The maps themselves are installed by WardensMapInstaller.cs (MainMenu context). Starting level N+1
// from inside level N is WardensLevelTransition.cs (method C in wardens-campaign-maps.md §2, a
// programmatic new game), reached through the MCP `campaign next` action; the completion toast still
// names the next map, so a player without an agent can pick it in the New Game screen (method A).
//
// The level table is parsed by tools/validate.py, which cross-checks it against the shipped .timber
// files and the tutorial line. Keep the `new WardensLevel(...)` calls on one line each.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Timberborn.GameFactionSystem;
using Timberborn.GameWonderCompletion;
using Timberborn.QuickNotificationSystem;
using Timberborn.SingletonSystem;
using Timberborn.TutorialSettingsSystem;
using Timberborn.TutorialSystem;
using UnityEngine;

namespace Wardens
{
    public sealed class WardensLevel
    {
        public readonly string Id;                  // "01"
        public readonly string MapName;             // the .timber file name, and MapNameService.Name
        public readonly string Title;               // "First Light"
        public readonly string EndsWithTutorial;    // finishing it completes the level
        public readonly string Next;                // the next level's id, or null for the last
        public readonly bool Shipped;               // a map file exists for it today

        public WardensLevel(string id, string mapName, string title, string endsWithTutorial, string next, bool shipped)
        {
            Id = id;
            MapName = mapName;
            Title = title;
            EndsWithTutorial = endsWithTutorial;
            Next = next;
            Shipped = shipped;
        }
    }

    /// One district on a map the player has left, and what it produced beyond its own needs: the
    /// thing a Gate on another map links in (WardensGate.cs). Keyed by settlement and the district
    /// center's entity id, so a later visit to the same save replaces the old reading.
    public sealed class WardensDistrictExport
    {
        public string Id = "";                  // "<settlement>|<district center EntityId>"
        public string Settlement = "";
        public string District = "";            // the district's display name
        public string Map = "";
        public string Level = "";               // the campaign level, or "" for a free map
        public float Day;                       // the in-game day of the reading
        public string SavedUtc = "";
        public readonly Dictionary<string, float> Exports = new Dictionary<string, float>();   // good id -> per day

        public JObject ToJson()
        {
            var exports = new JObject();
            foreach (var pair in Exports) exports[pair.Key] = Math.Round(pair.Value, 2);
            return new JObject
            {
                ["id"] = Id, ["settlement"] = Settlement, ["district"] = District, ["map"] = Map,
                ["level"] = Level, ["day"] = Math.Round(Day, 2), ["savedUtc"] = SavedUtc, ["exports"] = exports,
            };
        }

        public static WardensDistrictExport FromJson(JObject json)
        {
            var d = new WardensDistrictExport
            {
                Id = json.Value<string>("id") ?? "",
                Settlement = json.Value<string>("settlement") ?? "",
                District = json.Value<string>("district") ?? "",
                Map = json.Value<string>("map") ?? "",
                Level = json.Value<string>("level") ?? "",
                Day = json.Value<float?>("day") ?? 0f,
                SavedUtc = json.Value<string>("savedUtc") ?? "",
            };
            if (json["exports"] is JObject exports)
                foreach (var pair in exports)
                    if (pair.Value != null && pair.Value.Type is JTokenType.Float or JTokenType.Integer)
                        d.Exports[pair.Key] = (float)pair.Value;
            return d;
        }
    }

    /// campaign.json: what survives a map change. Written by the Game context, read by both.
    public sealed class WardensCampaignRecord
    {
        public const int CurrentVersion = 1;
        public const int MaxLedgerEntries = 200;

        public int Version = CurrentVersion;
        public string Level = "";                                   // the level last played
        public string LastMap = "";
        public readonly List<string> Completed = new List<string>();
        public readonly List<JObject> Ledger = new List<JObject>();
        public readonly List<WardensDistrictExport> Districts = new List<WardensDistrictExport>();
        public string UpdatedUtc = "";

        public static string Path => System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(WardensSettings.Path) ?? "", "campaign.json");

        public bool IsCompleted(string levelId) => !string.IsNullOrEmpty(levelId) && Completed.Contains(levelId);

        public static WardensCampaignRecord Load()
        {
            var r = new WardensCampaignRecord();
            try
            {
                if (!File.Exists(Path)) return r;
                // DateParseHandling.None: the timestamps stay the ISO strings they were written as,
                // instead of coming back in the machine's culture format and being written that way.
                JObject json;
                using (var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(File.ReadAllText(Path)))
                       { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
                    json = JObject.Load(reader);
                r.Version = json.Value<int?>("version") ?? CurrentVersion;
                r.Level = json.Value<string>("level") ?? "";
                r.LastMap = json.Value<string>("lastMap") ?? "";
                r.UpdatedUtc = json.Value<string>("updatedUtc") ?? "";
                foreach (var t in json["completed"] as JArray ?? new JArray())
                {
                    var id = (string)t;
                    if (!string.IsNullOrEmpty(id) && !r.Completed.Contains(id)) r.Completed.Add(id);
                }
                foreach (var t in json["ledger"] as JArray ?? new JArray())
                    if (t is JObject entry) r.Ledger.Add(entry);
                foreach (var t in json["districts"] as JArray ?? new JArray())
                    if (t is JObject district) r.Districts.Add(WardensDistrictExport.FromJson(district));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] campaign.json: " + ex.Message + " (starting a fresh record)");
                return new WardensCampaignRecord();
            }
            return r;
        }

        public JObject ToJson() => new JObject
        {
            ["version"] = Version,
            ["level"] = Level,
            ["lastMap"] = LastMap,
            ["completed"] = new JArray(Completed.ToArray()),
            ["ledger"] = new JArray(Ledger.ToArray()),
            ["districts"] = new JArray(Districts.ConvertAll(d => (object)d.ToJson()).ToArray()),
            ["updatedUtc"] = UpdatedUtc,
        };

        /// Write through a temp file: a half-written campaign.json would lose the whole campaign.
        public bool Save()
        {
            try
            {
                UpdatedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                while (Ledger.Count > MaxLedgerEntries) Ledger.RemoveAt(0);
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var temp = Path + ".tmp";
                File.WriteAllText(temp, ToJson().ToString(Newtonsoft.Json.Formatting.Indented));
                if (File.Exists(Path)) File.Delete(Path);
                File.Move(temp, Path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] campaign.json write failed: " + ex.Message);
                return false;
            }
        }
    }

    public class WardensCampaignService : ILoadableSingleton, IUpdatableSingleton
    {
        private const float PollSeconds = 0.5f;

        // One row per level. Levels 01 and 02 have maps; the rest are the arc
        // (design/wardens-campaign-concept.md §2, design/wardens-campaign-map-set.md §1) and are
        // listed so the completion toast can name what comes next and validate.py can see the gap.
        // A level also ends when every task in Levels/<id>.tasks.json is done (WardensLevelTasks.cs),
        // which works with the vanilla tutorial off; the ending tutorial is the older path.
        public static readonly WardensLevel[] Levels =
        {
            new WardensLevel("01", "Wardens 01 First Light", "First Light", "Wardens.MoreBeavers", "02", true),
            new WardensLevel("02", "Wardens 02 The Sump", "The Sump", "", "03", true),
            new WardensLevel("03", "Wardens 03 The Pods", "The Pods", "", "05", false),
            new WardensLevel("05", "Wardens 05 The Archive", "The Archive", "", "09", false),
            new WardensLevel("09", "Wardens 09 The City", "The Ark", "", "10", false),
            new WardensLevel("10", "Wardens 10 Home", "Home", "", null, false),
        };

        private readonly MapNameService _mapNameService;
        private readonly FactionService _factionService;
        private readonly TutorialService _tutorialService;
        private readonly TutorialSettings _tutorialSettings;
        private readonly QuickNotificationService _quickNotifications;
        private readonly WardensChat _chat;

        private WardensCampaignRecord _record = new WardensCampaignRecord();
        private WardensLevel _level;
        private bool _enabled;          // the Wardens on a campaign map
        private bool _completed;        // this level's ending tutorial is finished
        private bool _announced;        // the completion was announced this session
        private float _nextPoll;

        public WardensCampaignService(MapNameService mapNameService, FactionService factionService,
            TutorialService tutorialService, TutorialSettings tutorialSettings,
            QuickNotificationService quickNotifications, WardensChat chat)
        {
            _mapNameService = mapNameService;
            _factionService = factionService;
            _tutorialService = tutorialService;
            _tutorialSettings = tutorialSettings;
            _quickNotifications = quickNotifications;
            _chat = chat;
        }

        public WardensLevel Level => _level;
        public bool Enabled => _enabled;
        public bool Completed => _completed;
        /// campaign.json already lists the level as complete (an earlier session won it).
        public bool RecordedComplete(string levelId) => _record.IsCompleted(levelId);
        public WardensCampaignRecord Record => _record;

        public static WardensLevel ByMapName(string mapName)
        {
            if (string.IsNullOrEmpty(mapName)) return null;
            foreach (var level in Levels)
                if (string.Equals(level.MapName, mapName, StringComparison.OrdinalIgnoreCase)) return level;
            return null;
        }

        public static WardensLevel ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var level in Levels)
                if (string.Equals(level.Id, id, StringComparison.OrdinalIgnoreCase)) return level;
            return null;
        }

        public void Load()
        {
            _record = WardensCampaignRecord.Load();
            var mapName = _mapNameService.HasMapName ? _mapNameService.Name : null;
            _level = ByMapName(mapName);
            bool wardens = _factionService.Current?.Id == WardensStartingPopulation.FactionId;
            _enabled = wardens && _level != null;

            if (!_enabled)
            {
                Debug.Log($"[Wardens] campaign: inactive (map='{mapName}', faction={_factionService.Current?.Id}); " +
                          "the level table only covers the campaign maps");
                return;
            }

            // Entering a level records it immediately, so a run that is abandoned mid-level still
            // knows where it was.
            if (_record.Level != _level.Id || _record.LastMap != _level.MapName)
            {
                _record.Level = _level.Id;
                _record.LastMap = _level.MapName;
                _record.Save();
            }
            Debug.Log($"[Wardens] campaign: level {_level.Id} ({_level.Title}) on '{_level.MapName}'; " +
                      $"completed=[{string.Join(",", _record.Completed.ToArray())}]; " +
                      $"ends with {(_level.EndsWithTutorial == "" ? "(no tutorial set)" : _level.EndsWithTutorial)}");
        }

        // Completion is announced once per session, and never on the reconcile that happens when a
        // save from after the ending tutorial is reloaded (that is old news).
        public void UpdateSingleton()
        {
            if (!_enabled || _completed) return;
            if (string.IsNullOrEmpty(_level.EndsWithTutorial)) return;
            if (Time.unscaledTime < _nextPoll) return;
            bool firstPoll = _nextPoll == 0f;
            _nextPoll = Time.unscaledTime + PollSeconds;
            try
            {
                if (!Finished(_level.EndsWithTutorial)) return;
                _completed = true;
                bool alreadyKnown = _record.IsCompleted(_level.Id);
                MarkCompleted(_level.Id);
                if (!firstPoll && !alreadyKnown) Announce();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] campaign poll: " + ex.Message);
            }
        }

        /// The Warden's cross-level memory: one Ledger entry, kept in campaign.json.
        public JObject Record_Ledger(JObject entry)
        {
            if (entry == null) throw new ArgumentException("entry required");
            var stored = (JObject)entry.DeepClone();
            stored["level"] = _level?.Id ?? _record.Level;
            stored["utc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            _record.Ledger.Add(stored);
            _record.Save();
            return stored;
        }

        /// The districts of maps the player has played, with their surplus: what a Gate can link.
        public IReadOnlyList<WardensDistrictExport> Districts => _record.Districts;

        public WardensDistrictExport FindDistrict(string id) =>
            string.IsNullOrEmpty(id) ? null : _record.Districts.Find(d => d.Id == id);

        /// This game's readings replace everything recorded under its settlement: the latest visit
        /// wins, and a new game that reuses a settlement name (every start of a level is named after
        /// the level) replaces the old game's districts instead of listing them twice. campaign.json
        /// has one writer, this service, so the sampler hands its readings over.
        public void RecordDistricts(string settlement, List<WardensDistrictExport> readings, bool save)
        {
            var current = new HashSet<string>(readings.ConvertAll(r => r.Id));
            _record.Districts.RemoveAll(d => d.Settlement == settlement && !current.Contains(d.Id));
            foreach (var reading in readings)
            {
                int i = _record.Districts.FindIndex(d => d.Id == reading.Id);
                if (i >= 0) _record.Districts[i] = reading;
                else _record.Districts.Add(reading);
            }
            if (save) _record.Save();
        }

        /// The level's tasks are all done (WardensLevelTasks). `announce` is false when a save from
        /// after the win is loaded again: that is old news.
        public void CompleteByTasks(bool announce)
        {
            if (!_enabled || _completed) return;
            _completed = true;
            bool alreadyKnown = _record.IsCompleted(_level.Id);
            MarkCompleted(_level.Id);
            if (announce && !alreadyKnown) Announce();
        }

        /// Dev/testing and the MCP `campaign` tool: mark a level done without playing it.
        public void MarkCompleted(string levelId)
        {
            if (string.IsNullOrEmpty(levelId)) return;
            if (!_record.Completed.Contains(levelId)) _record.Completed.Add(levelId);
            _record.Level = levelId;
            _record.Save();
        }

        /// Wipe the campaign record. The maps and the saves are untouched.
        public void Reset()
        {
            _record = new WardensCampaignRecord();
            _completed = false;
            _announced = false;
            if (_level != null)
            {
                _record.Level = _level.Id;
                _record.LastMap = _level.MapName;
            }
            _record.Save();
        }

        public JObject State()
        {
            var levels = new JArray();
            foreach (var level in Levels)
                levels.Add(new JObject
                {
                    ["id"] = level.Id,
                    ["title"] = level.Title,
                    ["map"] = level.MapName,
                    ["ends_with"] = level.EndsWithTutorial,
                    ["next"] = level.Next,
                    ["shipped"] = level.Shipped,
                    ["completed"] = _record.IsCompleted(level.Id),
                    ["current"] = _level != null && level.Id == _level.Id,
                });
            var next = _level != null ? ById(_level.Next) : null;
            return new JObject
            {
                ["enabled"] = _enabled,
                ["map"] = _mapNameService.HasMapName ? _mapNameService.Name : null,
                ["level"] = _level?.Id,
                ["title"] = _level?.Title,
                ["ends_with_tutorial"] = _level?.EndsWithTutorial,
                ["level_complete"] = _completed,
                ["tutorial_enabled"] = !_tutorialSettings.DisableTutorial,
                ["next_level"] = next == null ? null : new JObject
                {
                    ["id"] = next.Id, ["title"] = next.Title, ["map"] = next.MapName, ["shipped"] = next.Shipped,
                },
                ["completed"] = new JArray(_record.Completed.ToArray()),
                ["ledger_entries"] = _record.Ledger.Count,
                // What a Gate can link: every district of a map played before, with its surplus per day.
                ["districts"] = new JArray(_record.Districts.ConvertAll(d => (object)d.ToJson()).ToArray()),
                ["record_path"] = WardensCampaignRecord.Path,
                ["levels"] = levels,
            };
        }

        public JArray Ledger(int limit)
        {
            var arr = new JArray();
            int from = Math.Max(0, _record.Ledger.Count - Math.Max(1, limit));
            for (int i = from; i < _record.Ledger.Count; i++) arr.Add(_record.Ledger[i].DeepClone());
            return arr;
        }

        private bool Finished(string tutorialId)
        {
            foreach (string id in _tutorialService._finishedTutorials)
                if (id == tutorialId) return true;
            return false;
        }

        private void Announce()
        {
            if (_announced) return;
            _announced = true;
            var next = ById(_level.Next);
            string line = next == null
                ? $"Level {_level.Id} complete: {_level.Title}. The campaign ends here."
                : next.Shipped
                    ? $"Level {_level.Id} complete: {_level.Title}. Next: level {next.Id}, {next.Title}. Continue in the task panel, or start a new game on the map '{next.MapName}'."
                    : $"Level {_level.Id} complete: {_level.Title}. Level {next.Id} ({next.Title}) is not built yet.";
            Debug.Log("[Wardens] campaign: " + line);
            try { _quickNotifications.SendNotification(line); }
            catch (Exception ex) { Debug.LogWarning("[Wardens] campaign toast: " + ex.Message); }
            try { _chat.SystemSays(line); }
            catch (Exception ex) { Debug.LogWarning("[Wardens] campaign chat: " + ex.Message); }
        }
    }
}
