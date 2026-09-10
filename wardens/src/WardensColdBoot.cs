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
//
// The orbit also plays back 3 archived badtides (design call: the wasteland already lived
// through them before day 1; the player should feel that, not just read it). Vanilla's
// HazardousWeatherStartedEvent/EndedEvent only drive a notification toast, a sound cue and
// HazardousWeatherHistory's bookkeeping (checked in the decompiled UI/trigger code: no water or
// contamination system reacts to the event, only to WeatherService.IsHazardousWeather over real
// elapsed cycles) so posting them directly, spaced out over the orbit, replays the real toast +
// sound 3 times without needing to fast-forward any actual simulation days. HazardousWeatherHistory
// picks them up the same way it would for real ones, so the streak/chance math and
// SurvivedFirstBadtideTrigger (-> the vanilla "Badtides" tutorial) are primed exactly as if the
// player had lived through 3 real ones.

using System;
using System.Collections.Generic;
using Timberborn.BlockSystem;
using Timberborn.Common;
using Timberborn.Coordinates;
using Timberborn.GameDistricts;
using Timberborn.GameFactionSystem;
using Timberborn.HazardousWeatherSystem;
using Timberborn.SingletonSystem;
using Timberborn.TimeSystem;
using Timberborn.TutorialSettingsSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensColdBoot : ILoadableSingleton, IUpdatableSingleton
    {
        public const float OrbitSeconds = 14f;
        private static readonly float[] BadtideDelays = { 1.5f, 5.5f, 9.5f };   // seconds into the orbit

        private readonly EventBus _eventBus;
        private readonly FactionService _factionService;
        private readonly TutorialSettings _tutorialSettings;
        private readonly SpeedManager _speedManager;
        private readonly WardensCameraDirector _director;
        private readonly DistrictCenterRegistry _districtCenters;
        private readonly WardensChat _chat;
        private readonly BadtideWeather _badtideWeather;

        private bool _badtideSequenceActive;
        private float _badtideSequenceElapsed;
        private int _badtidesFired;

        public bool Played { get; private set; }

        public WardensColdBoot(EventBus eventBus, FactionService factionService,
            TutorialSettings tutorialSettings, SpeedManager speedManager,
            WardensCameraDirector director, DistrictCenterRegistry districtCenters, WardensChat chat,
            BadtideWeather badtideWeather)
        {
            _eventBus = eventBus;
            _factionService = factionService;
            _tutorialSettings = tutorialSettings;
            _speedManager = speedManager;
            _director = director;
            _districtCenters = districtCenters;
            _chat = chat;
            _badtideWeather = badtideWeather;
        }

        public void Load()
        {
            _eventBus.Register(this);
        }

        public void UpdateSingleton()
        {
            if (!_badtideSequenceActive) return;
            _badtideSequenceElapsed += Time.unscaledDeltaTime;
            while (_badtidesFired < BadtideDelays.Length && _badtideSequenceElapsed >= BadtideDelays[_badtidesFired])
            {
                FireArchivedBadtide(++_badtidesFired);
            }
            if (_badtidesFired >= BadtideDelays.Length) _badtideSequenceActive = false;
        }

        // Cycle index only feeds BadtideWeather's cycle-1..3 handicap ramp (shorter/gentler early
        // durations), same as a real first-three-badtides history would use.
        private void FireArchivedBadtide(int index)
        {
            try
            {
                int duration = _badtideWeather.GetDurationAtCycle(index);
                _eventBus.Post(new HazardousWeatherSelectedEvent(_badtideWeather, duration));
                _eventBus.Post(new HazardousWeatherStartedEvent(_badtideWeather));
                _eventBus.Post(new HazardousWeatherEndedEvent(_badtideWeather));
                _chat.SystemSays($"Archive: badtide {index} of 3, logged {duration} day{(duration == 1 ? "" : "s")}. Recorded before this boot; nobody was awake to live through it.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] archived badtide " + index + " skipped: " + ex.Message);
            }
        }

        [OnEvent]
        public void OnNewGameInitialized(NewGameInitializedEvent e)
        {
            if (_factionService.Current?.Id != WardensStartingPopulation.FactionId) return;
            if (_tutorialSettings.DisableTutorial) return;
            Play(OrbitSeconds, seedBadtideHistory: true);
        }

        // Also reachable from the MCP `cutscene` tool so the shot can be tuned without starting a
        // new game every time; seedBadtideHistory defaults off there so replaying the shot for
        // tuning does not keep inflating the save's real badtide count.
        public void Play(float seconds = OrbitSeconds, bool seedBadtideHistory = false)
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
                if (seedBadtideHistory)
                {
                    _badtideSequenceActive = true;
                    _badtideSequenceElapsed = 0f;
                    _badtidesFired = 0;
                }
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
