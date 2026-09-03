// WardensColdBoot.cs. The start-of-run cutscene.
//
// Fires once per NEW Wardens game (NewGameInitializedEvent, same trigger as the bot swap in
// WardensStartingPopulation; loaded saves never see it). The tutorial system supplies the
// text cards (Tutorials/Stages/Wardens.ColdBoot.*), this class supplies the shot: the game
// is paused and locked, the camera makes one slow, eased orbit around the district center
// ("the Core") and settles back where it started, then the speed lock is released. The game
// stays paused: the Cold Boot "Clock" card asks the player to unpause, which is the first
// thing they learn to do. Skipped entirely when the player disabled the tutorial in the
// new-game panel, so playtest scripts that do not want a 14 s flight can turn it off there.

using System;
using System.Collections.Generic;
using Timberborn.BlockSystem;
using Timberborn.Common;
using Timberborn.Coordinates;
using Timberborn.GameDistricts;
using Timberborn.GameFactionSystem;
using Timberborn.SingletonSystem;
using Timberborn.TimeSystem;
using Timberborn.TutorialSettingsSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensColdBoot : ILoadableSingleton
    {
        public const float OrbitSeconds = 14f;

        private readonly EventBus _eventBus;
        private readonly FactionService _factionService;
        private readonly TutorialSettings _tutorialSettings;
        private readonly SpeedManager _speedManager;
        private readonly WardensCameraDirector _director;
        private readonly DistrictCenterRegistry _districtCenters;
        private readonly WardensChat _chat;

        public bool Played { get; private set; }

        public WardensColdBoot(EventBus eventBus, FactionService factionService,
            TutorialSettings tutorialSettings, SpeedManager speedManager,
            WardensCameraDirector director, DistrictCenterRegistry districtCenters, WardensChat chat)
        {
            _eventBus = eventBus;
            _factionService = factionService;
            _tutorialSettings = tutorialSettings;
            _speedManager = speedManager;
            _director = director;
            _districtCenters = districtCenters;
            _chat = chat;
        }

        public void Load()
        {
            _eventBus.Register(this);
        }

        [OnEvent]
        public void OnNewGameInitialized(NewGameInitializedEvent e)
        {
            if (_factionService.Current?.Id != WardensStartingPopulation.FactionId) return;
            if (_tutorialSettings.DisableTutorial) return;
            Play();
        }

        // Also reachable from the MCP `cutscene` tool so the shot can be tuned without
        // starting a new game every time.
        public void Play(float seconds = OrbitSeconds)
        {
            try
            {
                var frames = BuildOrbit(seconds);
                _speedManager.ChangeAndLockSpeed(0f);
                Played = true;
                _director.Fly(frames, () =>
                {
                    try { _speedManager.UnlockSpeed(); } catch (Exception ex) { Debug.LogWarning("[Wardens] unlock speed: " + ex.Message); }
                });
                _chat.SystemSays("Cold Boot. Orbiting the Core; the game is paused. Read the cards on the right.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] Cold Boot cutscene skipped: " + ex.Message);
                try { _speedManager.UnlockSpeed(); } catch { }
            }
        }

        private List<WardensCameraDirector.Keyframe> BuildOrbit(float seconds)
        {
            var start = _director.Current(0f);
            var target = start.Target;
            foreach (var dc in _districtCenters.AllDistrictCenters)
            {
                var block = dc.GetComponent<BlockObject>();
                if (block != null)
                {
                    target = CoordinateSystem.GridToWorldCentered(block.Coordinates);
                    break;
                }
            }
            var mid = start; mid.Target = target; mid.H = start.H + 180f; mid.Time = seconds * 0.5f;
            var end = start; end.Target = target; end.H = start.H + 360f; end.Time = seconds;
            return new List<WardensCameraDirector.Keyframe> { start, mid, end };
        }
    }
}
