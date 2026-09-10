// WardensHandoff.cs. The main menu's half of a level change.
//
// design/wardens-campaign-design.md §4.4, the HandoffLevelStarter row. A running game cannot
// reliably start a new one — every API for that is on the unverified list — but the main menu is
// where a new game starts anyway, and getting there is something the player can always do. So the
// Game scene writes campaign.handoff.json and the menu picks it up here.
//
// Runs in [Context("MainMenu")] beside WardensMapInstaller, and after it: the map has to be
// installed before a map reference for it exists. One-shot — the file is deleted whether or not the
// level started, because a handoff that keeps firing would trap the player in a loop they cannot
// leave, which is worse than one that misses.
//
// Not compiled against the game. If the programmatic start does not work here either, the log says
// which member was missing and the player still has the map in their New Game list, which is the
// floor this whole design was built on (wardens-campaign-maps.md §2 A).

using System;
using Newtonsoft.Json.Linq;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensHandoff : ILoadableSingleton
    {
        private readonly WardensMapInstaller _installer;

        public WardensHandoff(WardensMapInstaller installer)
        {
            // Not read, but it orders the two: the maps must be installed before a map reference for
            // one can exist. Bindito constructs a dependency first, which is exactly the guarantee
            // this needs and the reason it is a constructor parameter rather than a comment.
            _installer = installer;
        }

        public void Load()
        {
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Wardens] handoff failed: " + ex);
                WardensHandoffFile.Clear();
            }
        }

        private void Run()
        {
            var handoff = WardensHandoffFile.Read();
            if (handoff == null) return;

            var levelId = handoff.Value<string>("level");
            var level = WardensCampaignService.ById(levelId);
            WardensHandoffFile.Clear();          // one-shot, before anything can throw

            if (level == null)
            {
                Debug.LogWarning($"[Wardens] handoff names level '{levelId}', which is not in the level table");
                return;
            }

            var mode = CampaignMode.FromJson(handoff["mode"] as JObject);
            Debug.Log($"[Wardens] handoff: starting level {level.Id} ({level.MapName}), " +
                      $"installed maps: {_installer.Installed} new, {_installer.Skipped} current");

            var starter = new NewGameLevelStarter();
            if (!starter.CanStart(level, out var why))
            {
                Debug.LogWarning($"[Wardens] handoff cannot start level {level.Id} programmatically ({why}). " +
                                 $"The map is in the list: New Game -> Custom maps -> {level.MapName}.");
                return;
            }

            var result = starter.Start(level, mode);
            foreach (var missing in result.Missing)
                Debug.LogWarning("[Wardens] handoff: " + missing);
            Debug.Log("[Wardens] handoff: " + (result.Started ? "started" : "did not start") +
                      " level " + level.Id + " — " + result.Message);
        }
    }
}
