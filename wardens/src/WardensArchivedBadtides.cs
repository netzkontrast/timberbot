// WardensArchivedBadtides.cs. The badtides the wasteland lived through before day 1.
//
// Design call: the Wardens wake into a poisoned wasteland that has already survived several
// badtides; the player should feel that, not just read it. The Cold Boot cutscene replays three
// archived ones (a `badtide` on the shot, WardensCutsceneScript §3), spaced one per shot.
//
// Vanilla's HazardousWeatherStartedEvent/EndedEvent only drive a notification toast, a sound cue
// and HazardousWeatherHistory's bookkeeping (checked in the decompiled UI/trigger code: no water or
// contamination system reacts to the event, only to WeatherService.IsHazardousWeather over real
// elapsed cycles), so posting them directly replays the real toast + sound without fast-forwarding
// any actual simulation days. HazardousWeatherHistory picks them up the way it would for real ones,
// so the streak/chance math and SurvivedFirstBadtideTrigger (-> the vanilla "Badtides" tutorial)
// are primed exactly as if the player had lived through them.
//
// The cycle index only feeds BadtideWeather's cycle-1..3 handicap ramp (shorter, gentler early
// durations), same as a real first-three-badtides history would use.

using System;
using Timberborn.HazardousWeatherSystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensArchivedBadtides
    {
        public const int Count = 3;

        private readonly EventBus _eventBus;
        private readonly BadtideWeather _badtideWeather;
        private readonly WardensChat _chat;

        public WardensArchivedBadtides(EventBus eventBus, BadtideWeather badtideWeather, WardensChat chat)
        {
            _eventBus = eventBus;
            _badtideWeather = badtideWeather;
            _chat = chat;
        }

        /// Replays archived badtide `index` (1-based). Never throws: a cutscene must play on.
        public void Replay(int index)
        {
            try
            {
                int duration = _badtideWeather.GetDurationAtCycle(index);
                _eventBus.Post(new HazardousWeatherSelectedEvent(_badtideWeather, duration));
                _eventBus.Post(new HazardousWeatherStartedEvent(_badtideWeather));
                _eventBus.Post(new HazardousWeatherEndedEvent(_badtideWeather));
                _chat.SystemSays($"Archive: badtide {index} of {Count}, logged {duration} day{(duration == 1 ? "" : "s")}. "
                                 + "Recorded before this boot; nobody was awake to live through it.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] archived badtide " + index + " skipped: " + ex.Message);
            }
        }
    }
}
