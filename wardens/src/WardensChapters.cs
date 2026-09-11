// WardensChapters.cs. Story chapters: the beats of the tutorial line, announced as it advances.
//
// design/wardens-chapter-1-plan.md §4 and its status note of 2026-09-11. The chapters used to gate
// the building bar: nine buildings shipped with a padlock cost (ScienceCost 999999) and this
// service unlocked them when their chapter's tutorial finished. That gate is gone. Every building
// the faction ships carries ScienceCost 0 (tools/gen_buildings.py; tools/validate.py fails on any
// other value), so the whole bar is open from the first frame of every level, on every map. What a
// chapter still is: the buildings it is about (documentation, and the `chapter` tool's listing),
// the tutorial whose completion opens it, and the announcement when it does: a toast, a line in
// the WARDENS UPLINK panel, and ChapterOpened for the cutscene runner (WardensCutscenes.cs plays
// the chapter:<Id> scenes from it).
//
// TutorialService keeps the finished tutorial ids (persisted with the save); this service polls
// that set twice a second, the way WardensCampaignService does. No save state of its own: a loaded
// game reconciles itself on the first poll, silently (no toasts for old news). With the tutorial
// switched off in the new-game panel the story cannot advance, so every chapter counts as opened
// from the start, silently. Other factions are never touched.
//
// One safety net stays. On the first poll that sees the toolbar, a building whose spec still
// carries a science cost (a stale blueprint in a deployed copy, a collection wired in without the
// rule) is unlocked through BuildingUnlockingService.UnlockIgnoringCost and its button refreshed
// the way Timberbot's /api/science/unlock does it (ToolUnlockingService.UnlockInternal), with a
// warning naming the template: the rule "every building from the start" holds at runtime even when
// the data forgot it, and the log says which file to fix. `chapter status` lists them under
// unlocked_at_load; the list is empty when the data is right.
//
// Chapter 1 ("First Light") is where a new game starts and needs no entry here.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Timberborn.BlockObjectTools;
using Timberborn.Buildings;
using Timberborn.GameFactionSystem;
using Timberborn.Localization;
using Timberborn.QuickNotificationSystem;
using Timberborn.ScienceSystem;
using Timberborn.SingletonSystem;
using Timberborn.TemplateSystem;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;
using Timberborn.TutorialSettingsSystem;
using Timberborn.TutorialSystem;
using UnityEngine;

namespace Wardens
{
    public sealed class WardensChapter
    {
        public readonly string Id;
        public readonly string OpenedByTutorial;
        public readonly string[] Templates;   // the buildings the chapter is about; none of them locked

        public WardensChapter(string id, string openedByTutorial, string[] templates)
        {
            Id = id;
            OpenedByTutorial = openedByTutorial;
            Templates = templates;
        }

        public string TitleLocKey => $"Wardens.Chapter.{Id}.Title";
        /// The chapter's opening line. The row keeps the name it had when the chapters were a gate.
        public string OpenedLocKey => $"Wardens.Chapter.{Id}.Unlocked";
    }

    public class WardensChapterService : ILoadableSingleton, IUpdatableSingleton
    {
        private const float PollSeconds = 0.5f;
        private const int BarCheckPolls = 120;   // give the toolbar a minute to appear, then stop looking

        // Order matters only for display. Each chapter opens when its tutorial is finished, which
        // by construction of the tutorial line (tools/gen_tutorial.py) happens right before the
        // tutorial that asks for the chapter's buildings starts. tools/check_cutscenes.py parses
        // these entries: keep the `new WardensChapter("Id", "Tutorial", new[] { ... })` shape.
        public static readonly WardensChapter[] Chapters =
        {
            new WardensChapter("Badwater", "Wardens.Scrap",
                new[] { "SludgePump.Wardens", "ReedBed.Wardens", "SludgeTank.Wardens", "Rack.Wardens" }),
            new WardensChapter("Signal", "Wardens.WorkingHours",
                new[] { "Cruncher.Wardens" }),
            new WardensChapter("Pods", "Wardens.Storage",
                new[] { "BreedingPod.Wardens" }),
            new WardensChapter("Power", "Wardens.Housing",
                new[] { "BadwaterCell.Wardens", "SludgeBurner.Wardens" }),
            new WardensChapter("Green", "Wardens.MoreBeavers",
                new[] { "AdvancedBreedingPod.Wardens" }),
        };

        private readonly FactionService _factionService;
        private readonly TutorialService _tutorialService;
        private readonly TutorialSettings _tutorialSettings;
        private readonly BuildingService _buildingService;
        private readonly BuildingUnlockingService _buildingUnlocking;
        private readonly ToolButtonService _toolButtons;
        private readonly ToolUnlockingService _toolUnlocking;
        private readonly QuickNotificationService _quickNotifications;
        private readonly ILoc _loc;
        private readonly WardensChat _chat;

        private readonly HashSet<string> _done = new HashSet<string>();
        private readonly List<string> _unlockedAtLoad = new List<string>();
        private bool _enabled;      // active faction is the Wardens
        private bool _openAll;      // tutorial disabled: the story cannot advance, every chapter counts as opened
        private bool _reconciled;   // first poll after load done (that one is silent)
        private bool _barChecked;   // the safety net ran (once, when the toolbar exists)
        private int _barPolls;
        private float _nextPoll;

        public WardensChapterService(FactionService factionService, TutorialService tutorialService,
            TutorialSettings tutorialSettings, BuildingService buildingService,
            BuildingUnlockingService buildingUnlocking, ToolButtonService toolButtons,
            ToolUnlockingService toolUnlocking, QuickNotificationService quickNotifications,
            ILoc loc, WardensChat chat)
        {
            _factionService = factionService;
            _tutorialService = tutorialService;
            _tutorialSettings = tutorialSettings;
            _buildingService = buildingService;
            _buildingUnlocking = buildingUnlocking;
            _toolButtons = toolButtons;
            _toolUnlocking = toolUnlocking;
            _quickNotifications = quickNotifications;
            _loc = loc;
            _chat = chat;
        }

        public bool Enabled => _enabled;

        /// Raised when a chapter is announced (a real opening, or a forced one through `chapter unlock`),
        /// never on the silent reconcile after a load.
        public event Action<WardensChapter> ChapterOpened;

        public void Load()
        {
            _enabled = _factionService.Current?.Id == WardensStartingPopulation.FactionId;
            _openAll = _enabled && _tutorialSettings.DisableTutorial;
            if (_enabled)
                Debug.Log($"[Wardens] chapters: {Chapters.Length} story beats, nothing locked, tutorial={!_tutorialSettings.DisableTutorial}");
        }

        // Tool buttons are built by UI singletons whose load order we do not control, so the bar
        // check and the first reconcile happen here rather than in Load().
        public void UpdateSingleton()
        {
            if (!_enabled || (_barChecked && _done.Count == Chapters.Length)) return;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollSeconds;
            bool announce = _reconciled;
            _reconciled = true;
            if (!_barChecked) CheckTheBar();
            if (_done.Count == Chapters.Length) return;
            try
            {
                var finished = FinishedTutorials();
                foreach (var chapter in Chapters)
                {
                    if (_done.Contains(chapter.Id)) continue;
                    if (_openAll || finished.Contains(chapter.OpenedByTutorial))
                        Open(chapter, announce);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] chapters: " + ex.Message);
            }
        }

        /// Dev/testing entry point (MCP `chapter action=unlock`): announce a chapter now, whatever the
        /// tutorial says. On a chapter that has already opened this replays its toast, Uplink line and scene.
        public bool Force(string chapterId)
        {
            foreach (var chapter in Chapters)
            {
                if (!string.Equals(chapter.Id, chapterId, StringComparison.OrdinalIgnoreCase)) continue;
                Open(chapter, announce: true);
                return true;
            }
            return false;
        }

        /// The story has reached this chapter: its tutorial is finished, or it was announced.
        public bool Opened(WardensChapter chapter)
        {
            if (_done.Contains(chapter.Id)) return true;
            if (_openAll) return true;
            return Finished(chapter.OpenedByTutorial);
        }

        private bool Finished(string tutorialId)
        {
            foreach (string id in _tutorialService._finishedTutorials)
                if (id == tutorialId) return true;
            return false;
        }

        private HashSet<string> FinishedTutorials()
        {
            var set = new HashSet<string>();
            foreach (string id in _tutorialService._finishedTutorials)
                set.Add(id);
            return set;
        }

        // The safety net: every building on the bar is buildable from the first frame. Waits for the
        // first poll that sees tool buttons, then runs once per game; a failure gives up with a warning
        // rather than retrying every poll, so the chapters keep working without it.
        private void CheckTheBar()
        {
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
                    Debug.LogWarning($"[Wardens] chapters: {name} shipped with ScienceCost {buildingSpec.ScienceCost} and was unlocked at load. " +
                                     "Every Wardens building ships with ScienceCost 0 (tools/gen_buildings.py); tools/validate.py names the file.");
                }
            }
            catch (Exception ex)
            {
                _barChecked = true;
                Debug.LogWarning("[Wardens] chapters: bar check failed, not retried: " + ex.Message);
                return;
            }
            if (buttons == 0)
            {
                if (++_barPolls < BarCheckPolls) return;   // the toolbar is not built yet; look again next poll
                Debug.LogWarning("[Wardens] chapters: no tool buttons seen; bar check skipped");
            }
            _barChecked = true;
            Debug.Log($"[Wardens] chapters: bar checked, {buttons} tool buttons, {_unlockedAtLoad.Count} unlocked at load");
        }

        private void Open(WardensChapter chapter, bool announce)
        {
            bool first = _done.Add(chapter.Id);
            Debug.Log($"[Wardens] chapter {chapter.Id} open (first={first}, announce={announce})");
            if (!announce) return;

            string title = SafeLoc(chapter.TitleLocKey);
            string text = SafeLoc(chapter.OpenedLocKey);
            try { _quickNotifications.SendNotification(title + " " + text); }
            catch (Exception ex) { Debug.LogWarning("[Wardens] chapter toast: " + ex.Message); }
            try { _chat.SystemSays(title + " " + text); }
            catch (Exception ex) { Debug.LogWarning("[Wardens] chapter chat: " + ex.Message); }
            try { ChapterOpened?.Invoke(chapter); }
            catch (Exception ex) { Debug.LogWarning("[Wardens] chapter opened handlers: " + ex.Message); }
        }

        private bool IsUnlocked(string name)
        {
            try
            {
                var spec = _buildingService.GetBuildingTemplate(name).GetSpec<BuildingSpec>();
                return spec == null || spec.ScienceCost <= 0 || _buildingUnlocking.Unlocked(spec);
            }
            catch
            {
                return false;
            }
        }

        // ---- MCP ------------------------------------------------------------------------------

        /// One line for wardens_status and the frames: which chapter the story is on.
        public JObject Summary()
        {
            var complete = new JArray();
            string next = null, waitingFor = null;
            foreach (var chapter in Chapters)
            {
                if (Opened(chapter)) { complete.Add(chapter.Id); continue; }
                if (next == null) { next = chapter.Id; waitingFor = chapter.OpenedByTutorial; }
            }
            return new JObject
            {
                ["enabled"] = _enabled,
                ["complete"] = complete,
                ["next"] = next,
                ["next_waits_for"] = waitingFor,
            };
        }

        /// The whole table, for the `chapter` tool: per chapter its tutorial, whether the story has
        /// reached it, and the buildings it is about (all buildable from the start; `unlocked` says so).
        public JObject State()
        {
            var chapters = new JArray();
            foreach (var chapter in Chapters)
            {
                var buildings = new JArray();
                foreach (var name in chapter.Templates)
                    buildings.Add(new JObject { ["template"] = name, ["unlocked"] = IsUnlocked(name) });
                chapters.Add(new JObject
                {
                    ["id"] = chapter.Id,
                    ["title"] = SafeLoc(chapter.TitleLocKey),
                    ["opened_by_tutorial"] = chapter.OpenedByTutorial,
                    ["opened"] = Opened(chapter),
                    ["buildings"] = buildings,
                });
            }
            var state = Summary();
            state["unlocked_at_load"] = new JArray(_unlockedAtLoad.ToArray());
            state["chapters"] = chapters;
            return state;
        }

        private string SafeLoc(string key)
        {
            try { return _loc.T(key); } catch { return key; }
        }
    }
}
