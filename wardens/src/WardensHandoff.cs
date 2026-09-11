// WardensHandoff.cs. The main menu's half of a level change, and the way to launch straight into one.
//
// design/wardens-campaign-design.md §4.4. campaign.handoff.json (WardensHandoffFile) names a level;
// when the main menu loads and finds it, the level starts as a new Wardens game on its map, without
// the New Game screen. Two writers:
//
//   - the Game scene, when a level change could not start in place (HandoffLevelStarter: the map was
//     not installed yet); it writes the file and returns to the menu;
//   - a person or a script, before launching the game: {"level": "01"} is the whole file, and the
//     game opens on level 01 instead of the menu.
//
// GameSceneLoader is bound in the MainMenu context (GameSceneLoadingConfigurator, 1.1.2.4 decompile),
// and its load runs in a coroutine that waits for the menu's own load to finish, so starting a game
// from a MainMenu Load() is safe; TimberbotAutoLoad loads a save from the same place.
//
// One-shot: the file is deleted before anything can fail, because a handoff that keeps firing would
// trap the player in a loop they cannot leave, which is worse than one that misses.

using System;
using Timberborn.GameSceneLoading;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensHandoff : ILoadableSingleton
    {
        private readonly WardensMapInstaller _installer;
        private readonly GameSceneLoader _gameSceneLoader;

        public WardensHandoff(WardensMapInstaller installer, GameSceneLoader gameSceneLoader)
        {
            _installer = installer;
            _gameSceneLoader = gameSceneLoader;
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
            WardensHandoffFile.Clear();          // one-shot, before anything can throw; also drops an unreadable file
            if (handoff == null) return;

            var levelId = handoff.Value<string>("level");
            var level = WardensCampaignService.ById(levelId);
            if (level == null)
            {
                Debug.LogWarning($"[Wardens] handoff names level '{levelId}', which is not in the level table");
                return;
            }

            // Load order between two MainMenu singletons is not ours to choose, and the map has to be
            // in the user's Maps folder before FromUserFolder can find it. Idempotent.
            _installer.EnsureInstalled();

            var starter = new NewGameLevelStarter(_gameSceneLoader, autosaver: null);
            if (!starter.CanStart(level, out var why))
            {
                Debug.LogWarning($"[Wardens] handoff cannot start level {level.Id} ({why}). " +
                                 $"Pick it by hand: New Game -> Custom maps -> {level.MapName}.");
                return;
            }

            Debug.Log($"[Wardens] handoff: starting level {level.Id} ({level.MapName}); " +
                      $"maps {_installer.Installed} installed, {_installer.Skipped} already current");
            var result = starter.Start(level, CampaignMode.FromJson(handoff["mode"] as Newtonsoft.Json.Linq.JObject));
            foreach (var missing in result.Missing)
                Debug.LogWarning("[Wardens] handoff: " + missing);
            if (!result.Started) Debug.LogWarning("[Wardens] handoff: did not start level " + level.Id + " — " + result.Message);
        }
    }
}
