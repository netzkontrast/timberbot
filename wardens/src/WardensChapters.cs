// WardensChapters.cs. Story chapters gate the building bar.
//
// design/wardens-chapter-1-plan.md §4. Vanilla's FactionGoalsUnlocker is a singleton that checks a
// condition and calls an unlocking service; this is the same shape, driven by the tutorial line.
// A chapter is a list of building templates that ship with the padlock cost
// (BuildingSpec.ScienceCost == LockedCost, set by tools/gen_buildings.py) plus the tutorial whose
// completion opens it. TutorialService keeps the finished tutorial ids (persisted with the save);
// this service polls that set twice a second, and when a chapter's tutorial is in it the chapter's
// buildings are unlocked through BuildingUnlockingService.UnlockIgnoringCost, the toolbar button is
// refreshed the way Timberbot's /api/science/unlock does it (ToolUnlockingService.UnlockInternal),
// and the player gets a toast plus a line in the WARDENS UPLINK panel.
//
// No save state of its own: finished tutorials and unlocked buildings are both persisted by vanilla,
// so a loaded game reconciles itself on the first poll, silently (no toasts for old news). With the
// tutorial switched off in the new-game panel, or chapterGating=false in settings.json, every
// chapter opens at once. Other factions are never touched.
//
// Chapter 1 ("First Light") is the starting bar and needs no entry here: Path, Scavenger Flag,
// Power Shaft, Charging Post and Scrap Pile keep ScienceCost 0. Power is life, so the Charging Post
// is never behind a gate.

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
        public readonly string UnlockedByTutorial;
        public readonly string[] Templates;

        public WardensChapter(string id, string unlockedByTutorial, string[] templates)
        {
            Id = id;
            UnlockedByTutorial = unlockedByTutorial;
            Templates = templates;
        }

        public string TitleLocKey => $"Wardens.Chapter.{Id}.Title";
        public string UnlockedLocKey => $"Wardens.Chapter.{Id}.Unlocked";
    }

    public class WardensChapterService : ILoadableSingleton, IUpdatableSingleton
    {
        // The padlock: vanilla shows a building with ScienceCost > 0 as locked until it is unlocked.
        public const int LockedCost = 999999;
        private const float PollSeconds = 0.5f;

        // Order matters only for display. Each chapter opens when its tutorial is finished, which
        // by construction of the tutorial line (tools/gen_tutorial.py) happens right before the
        // tutorial that asks for the chapter's buildings starts.
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
        private bool _enabled;      // active faction is the Wardens
        private bool _gating;       // chapterGating in settings.json
        private bool _unlockAll;    // tutorial disabled or gating off: open everything
        private bool _reconciled;   // first poll after load done (that one is silent)
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
        public bool Gating => _gating;

        public void Load()
        {
            _enabled = _factionService.Current?.Id == WardensStartingPopulation.FactionId;
            _gating = WardensSettings.Load().ChapterGating;
            _unlockAll = _enabled && (!_gating || _tutorialSettings.DisableTutorial);
            if (_enabled)
                Debug.Log($"[Wardens] chapters: {Chapters.Length} gates, gating={_gating}, tutorial={!_tutorialSettings.DisableTutorial}");
        }

        // Tool buttons are built by UI singletons whose load order we do not control, so the first
        // reconcile happens here rather than in Load().
        public void UpdateSingleton()
        {
            if (!_enabled || _done.Count == Chapters.Length) return;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollSeconds;
            bool announce = _reconciled;
            _reconciled = true;
            try
            {
                var finished = FinishedTutorials();
                foreach (var chapter in Chapters)
                {
                    if (_done.Contains(chapter.Id)) continue;
                    if (_unlockAll || finished.Contains(chapter.UnlockedByTutorial))
                        Open(chapter, announce);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] chapters: " + ex.Message);
            }
        }

        /// Dev/testing entry point (MCP `chapter` tool): open a chapter regardless of the tutorial.
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

        public bool IsComplete(WardensChapter chapter)
        {
            if (_done.Contains(chapter.Id)) return true;
            foreach (var name in chapter.Templates)
                if (!IsUnlocked(name)) return false;
            return true;
        }

        private HashSet<string> FinishedTutorials()
        {
            var set = new HashSet<string>();
            foreach (string id in _tutorialService._finishedTutorials)
                set.Add(id);
            return set;
        }

        private void Open(WardensChapter chapter, bool announce)
        {
            int opened = 0;
            foreach (var name in chapter.Templates)
            {
                try
                {
                    if (UnlockTemplate(name)) opened++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Wardens] chapter {chapter.Id}: cannot unlock {name}: {ex.Message}");
                }
            }
            _done.Add(chapter.Id);
            Debug.Log($"[Wardens] chapter {chapter.Id} open ({opened} of {chapter.Templates.Length} newly unlocked, announce={announce})");
            if (!announce || opened == 0) return;

            string title = _loc.T(chapter.TitleLocKey);
            string text = _loc.T(chapter.UnlockedLocKey);
            try { _quickNotifications.SendNotification(title + " " + text); }
            catch (Exception ex) { Debug.LogWarning("[Wardens] chapter toast: " + ex.Message); }
            try { _chat.SystemSays(title + " " + text); }
            catch (Exception ex) { Debug.LogWarning("[Wardens] chapter chat: " + ex.Message); }
        }

        // Returns true when the template was locked and is now unlocked. The toolbar route is the
        // one Timberbot uses (button state refreshes); the template route covers a building that has
        // no button (never the case for the chapter tables, but it keeps the service from stalling).
        private bool UnlockTemplate(string name)
        {
            foreach (var toolButton in _toolButtons.ToolButtons)
            {
                var blockObjectTool = toolButton.Tool as BlockObjectTool;
                if (blockObjectTool == null) continue;
                var templateSpec = blockObjectTool.Template.GetSpec<TemplateSpec>();
                if (templateSpec == null || templateSpec.TemplateName != name) continue;
                var buildingSpec = blockObjectTool.Template.GetSpec<BuildingSpec>();
                if (buildingSpec == null || _buildingUnlocking.Unlocked(buildingSpec)) return false;
                _buildingUnlocking.UnlockIgnoringCost(buildingSpec);
                _toolUnlocking.UnlockInternal(blockObjectTool, () => { });
                return true;
            }

            var template = _buildingService.GetBuildingTemplate(name);
            var spec = template.GetSpec<BuildingSpec>();
            if (spec == null || _buildingUnlocking.Unlocked(spec)) return false;
            _buildingUnlocking.UnlockIgnoringCost(spec);
            Debug.LogWarning($"[Wardens] chapters: {name} has no tool button; unlocked without a toolbar refresh");
            return true;
        }

        private bool IsUnlocked(string name)
        {
            try
            {
                var spec = _buildingService.GetBuildingTemplate(name).GetSpec<BuildingSpec>();
                return spec == null || _buildingUnlocking.Unlocked(spec);
            }
            catch
            {
                return false;
            }
        }

        // ---- MCP ------------------------------------------------------------------------------

        /// One line for wardens_status: which chapter the story is on.
        public JObject Summary()
        {
            var complete = new JArray();
            string next = null, waitingFor = null;
            foreach (var chapter in Chapters)
            {
                if (IsComplete(chapter)) { complete.Add(chapter.Id); continue; }
                if (next == null) { next = chapter.Id; waitingFor = chapter.UnlockedByTutorial; }
            }
            return new JObject
            {
                ["enabled"] = _enabled,
                ["gating"] = _gating && !_unlockAll,
                ["complete"] = complete,
                ["next"] = next,
                ["next_waits_for"] = waitingFor,
            };
        }

        /// The whole table with per-building lock state, for the `chapter` tool.
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
                    ["unlocked_by_tutorial"] = chapter.UnlockedByTutorial,
                    ["complete"] = IsComplete(chapter),
                    ["buildings"] = buildings,
                });
            }
            var state = Summary();
            state["locked_cost"] = LockedCost;
            state["chapters"] = chapters;
            return state;
        }

        private string SafeLoc(string key)
        {
            try { return _loc.T(key); } catch { return key; }
        }
    }
}
