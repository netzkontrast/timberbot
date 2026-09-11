// WardensLevelTransition.cs. Loading a level's map from inside the mod.
//
// design/wardens-campaign-design.md §4.4, wardens-campaign-maps.md §2 C. Timberborn has no campaign,
// so "start level 02" is not a thing the game offers: a level is a map, and changing level means
// putting the player on a new game on a different map.
//
// Every game member below was read in the 1.1.2.4 decompile (2026-09-11), none is guessed:
//
//   GameSceneLoader.StartNewGameInstantly(string factionId, MapFileReference, string settlementName)
//       builds the NewGameConfiguration itself, with GameModeSpecService.GetDefaultSpec(). Bound
//       AsTransient in MainMenu and Game (GameSceneLoadingConfigurator); ValidatingGameLoader, which
//       the in-game Load dialog uses, takes one in its constructor, so it resolves in the Game scene.
//   MapFileReference.FromUserFolder(name)   -> MapRepository.UserMapsDirectory/<name>.timber
//   Autosaver.CreateExitSave()              the instant save "Exit to main menu" makes (Game only)
//   MainMenuSceneLoader.SaveAndOpenMainMenu() bound in MainMenu, Game and MapEditor
//   ValidatingGameLoader.LoadGame(SaveReference) bound in MainMenu and Game
//
// Three strategies, tried in order:
//
//   NewGameLevelStarter   exit-saves this colony, then starts the level's map as a new Wardens game.
//                         The one that runs whenever the map is installed.
//   SaveLevelStarter      loads a shipped save through ValidatingGameLoader. Only for levels that
//                         ship one, which none do yet.
//   HandoffLevelStarter   writes campaign.handoff.json and returns to the main menu, where
//                         WardensHandoff installs the maps and starts the level. Reached when the map
//                         is not in the player's Maps folder yet (installMaps=false, a deleted file).
//
// The transition is offered, never forced (§4.4): the player may want to finish something first.
// `Start` returns a WardensTransition saying what happened, and the caller (the MCP `campaign next`
// action) decides how to say it.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Timberborn.Autosaving;
using Timberborn.GameSaveRepositorySystem;
using Timberborn.GameSaveRepositorySystemUI;
using Timberborn.GameSceneLoading;
using Timberborn.MainMenuSceneLoading;
using Timberborn.MapRepositorySystem;
using Timberborn.PlatformUtilities;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Wardens
{
    /// What the next level should start with. The game's default mode is what a new game gets today
    /// (StartNewGameInstantly picks it); NewGameMode is kept for a later captured-mode replay.
    public sealed class CampaignMode
    {
        public JObject NewGameMode;
        public bool Tutorial = true;

        public JObject ToJson() => new JObject
        {
            ["tutorial"] = Tutorial,
            ["newGameMode"] = NewGameMode != null ? (JToken)NewGameMode : JValue.CreateNull(),
        };

        public static CampaignMode FromJson(JObject json)
        {
            var mode = new CampaignMode();
            if (json == null) return mode;
            mode.Tutorial = json.Value<bool?>("tutorial") ?? true;
            mode.NewGameMode = json["newGameMode"] as JObject;
            return mode;
        }
    }

    /// The outcome of asking for a level change. `Started` means the game is now loading it.
    public sealed class WardensTransition
    {
        public bool Started;
        public string Strategy = "";
        public string Message = "";                 // what to tell the player, in their terms
        public readonly List<string> Missing = new List<string>();   // what stood in the way

        public static WardensTransition Fail(string message) => new WardensTransition { Message = message };

        public JObject ToJson() => new JObject
        {
            ["started"] = Started,
            ["strategy"] = Strategy,
            ["message"] = Message,
            ["missing"] = new JArray(Missing.ToArray()),
        };
    }

    public interface ILevelStarter
    {
        string Name { get; }
        /// Can this strategy start that level right now? Fills `why` when it cannot.
        bool CanStart(WardensLevel level, out string why);
        WardensTransition Start(WardensLevel level, CampaignMode mode);
    }

    // --- the handoff file: a level change that completes at the main menu -----------------------------

    /// campaign.handoff.json, next to campaign.json. Written in the Game scene, or by hand before a
    /// launch ({"level":"01"} is the whole file), and read once by the main menu (WardensHandoff).
    public static class WardensHandoffFile
    {
        public static string Path => System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(WardensSettings.Path) ?? "", "campaign.handoff.json");

        public static bool Write(WardensLevel level, CampaignMode mode)
        {
            try
            {
                var json = new JObject
                {
                    ["level"] = level.Id,
                    ["map"] = level.MapName,
                    ["mode"] = mode?.ToJson() ?? new CampaignMode().ToJson(),
                    ["writtenUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                };
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var temp = Path + ".tmp";
                File.WriteAllText(temp, json.ToString(Newtonsoft.Json.Formatting.Indented));
                if (File.Exists(Path)) File.Delete(Path);
                File.Move(temp, Path);
                Debug.Log($"[Wardens] handoff written: level {level.Id} ({level.MapName}) -> {Path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] handoff write failed: " + ex.Message);
                return false;
            }
        }

        public static JObject Read()
        {
            try
            {
                return File.Exists(Path) ? JObject.Parse(File.ReadAllText(Path)) : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] handoff read failed: " + ex.Message);
                return null;
            }
        }

        /// One-shot: a handoff that has fired must not fire again on the next menu visit.
        public static void Clear()
        {
            try
            {
                if (File.Exists(Path)) File.Delete(Path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] handoff delete failed: " + ex.Message);
            }
        }
    }

    public sealed class HandoffLevelStarter : ILevelStarter
    {
        private readonly MainMenuSceneLoader _mainMenuSceneLoader;

        public HandoffLevelStarter(MainMenuSceneLoader mainMenuSceneLoader)
        {
            _mainMenuSceneLoader = mainMenuSceneLoader;
        }

        public string Name => "handoff";

        public bool CanStart(WardensLevel level, out string why)
        {
            why = null;
            if (level == null) { why = "no level"; return false; }
            if (!level.Shipped) { why = "level " + level.Id + " ships no map yet"; return false; }
            return true;
        }

        public WardensTransition Start(WardensLevel level, CampaignMode mode)
        {
            var result = new WardensTransition { Strategy = Name };
            if (!WardensHandoffFile.Write(level, mode))
            {
                result.Message = "Could not write the handoff file. Start level " + level.Id +
                                 " from New Game -> Custom maps -> " + level.MapName + ".";
                return result;
            }
            try
            {
                // The pause menu's "Exit to main menu": posts PreMainMenuStartedEvent, which makes
                // the Autosaver write its exit save, then loads the menu, where the handoff fires.
                _mainMenuSceneLoader.SaveAndOpenMainMenu();
                result.Started = true;
                result.Message = "Saving and returning to the main menu to start level " + level.Id + ", " + level.Title + ".";
            }
            catch (Exception ex)
            {
                result.Missing.Add("MainMenuSceneLoader.SaveAndOpenMainMenu threw: " + ex.Message);
                result.Message = "Level " + level.Id + " (" + level.Title + ") is queued. Return to the main menu and it " +
                                 "will start; or pick " + level.MapName + " under New Game -> Custom maps.";
            }
            return result;
        }
    }

    // --- the programmatic new game ---------------------------------------------------------------------

    public sealed class NewGameLevelStarter : ILevelStarter
    {
        public const string FactionId = WardensStartingPopulation.FactionId;

        private readonly GameSceneLoader _gameSceneLoader;
        private readonly Autosaver _autosaver;     // null at the main menu: there is no colony to save

        public NewGameLevelStarter(GameSceneLoader gameSceneLoader, Autosaver autosaver)
        {
            _gameSceneLoader = gameSceneLoader;
            _autosaver = autosaver;
        }

        public string Name => "new game";

        public static string MapPath(WardensLevel level) =>
            System.IO.Path.Combine(MapRepository.UserMapsDirectory, level.MapName + ".timber");

        public bool CanStart(WardensLevel level, out string why)
        {
            why = null;
            if (level == null) { why = "no level"; return false; }
            if (!level.Shipped) { why = "level " + level.Id + " ships no map yet"; return false; }
            // FromUserFolder resolves to exactly this path (MapRepository.CustomMapNameToFileName).
            if (!File.Exists(MapPath(level))) { why = "the map is not installed at " + MapPath(level); return false; }
            return true;
        }

        public WardensTransition Start(WardensLevel level, CampaignMode mode)
        {
            var result = new WardensTransition { Strategy = Name };
            if (_autosaver != null)
            {
                // What "Exit to main menu" does before it leaves: the colony being left stays loadable.
                try { _autosaver.CreateExitSave(); }
                catch (Exception ex) { result.Missing.Add("exit save failed: " + ex.Message); }
            }
            try
            {
                // The settlement is named after the level, so its saves sit together under Load Game.
                _gameSceneLoader.StartNewGameInstantly(FactionId, MapFileReference.FromUserFolder(level.MapName), level.Title);
                result.Started = true;
                result.Message = "Starting level " + level.Id + ", " + level.Title + ".";
                Debug.Log($"[Wardens] transition: starting level {level.Id} ({level.Title}) on '{level.MapName}' as a new {FactionId} game");
            }
            catch (Exception ex)
            {
                result.Missing.Add("GameSceneLoader.StartNewGameInstantly threw: " + ex.Message);
                result.Message = "Could not start level " + level.Id + " programmatically.";
            }
            return result;
        }
    }

    // --- a shipped save: proven API, but no level ships one yet ---------------------------------------

    public sealed class SaveLevelStarter : ILevelStarter
    {
        public const string SettlementName = "Wardens Campaign";

        private readonly ValidatingGameLoader _loader;

        public SaveLevelStarter(ValidatingGameLoader loader)
        {
            _loader = loader;
        }

        public string Name => "shipped save";

        public bool CanStart(WardensLevel level, out string why)
        {
            why = null;
            if (level == null) { why = "no level"; return false; }
            var path = SavePath(level);
            if (!File.Exists(path)) { why = "no shipped save at " + path; return false; }
            return true;
        }

        public WardensTransition Start(WardensLevel level, CampaignMode mode)
        {
            var result = new WardensTransition { Strategy = Name };
            try
            {
                // The same construction TimberbotAutoLoad uses at the main menu.
                var settlement = new SettlementReference(SettlementName, SavesDirectory);
                _loader.LoadGame(new SaveReference(level.MapName, settlement));
                result.Started = true;
                result.Message = "Loading level " + level.Id + ", " + level.Title + ".";
            }
            catch (Exception ex)
            {
                result.Missing.Add("ValidatingGameLoader.LoadGame threw: " + ex.Message);
                result.Message = "Could not load the shipped save for level " + level.Id + ".";
            }
            return result;
        }

        public static string SavesDirectory => System.IO.Path.Combine(UserDataFolder.Folder, "Saves");

        public static string SavePath(WardensLevel level) =>
            System.IO.Path.Combine(SavesDirectory, SettlementName, level.MapName + ".timber");
    }

    // --- the service that picks one ---------------------------------------------------------------------

    public sealed class WardensLevelTransitionService : ILoadableSingleton
    {
        private readonly List<ILevelStarter> _starters;

        public WardensLevelTransitionService(GameSceneLoader gameSceneLoader, Autosaver autosaver,
            ValidatingGameLoader validatingGameLoader, MainMenuSceneLoader mainMenuSceneLoader)
        {
            // Order matters: the best experience first, the one that always works last.
            _starters = new List<ILevelStarter>
            {
                new NewGameLevelStarter(gameSceneLoader, autosaver),
                new SaveLevelStarter(validatingGameLoader),
                new HandoffLevelStarter(mainMenuSceneLoader),
            };
        }

        public void Load()
        {
            // One line per strategy, so a log shows at a glance what `campaign next` would do.
            foreach (var level in WardensCampaignService.Levels)
            {
                if (!level.Shipped) continue;
                var starter = Choose(level, out var why);
                Debug.Log($"[Wardens] transition: level {level.Id} would start by " +
                          (starter != null ? "'" + starter.Name + "'" : "nothing (" + why + ")"));
            }
        }

        /// The first strategy that can start this level, or null.
        public ILevelStarter Choose(WardensLevel level, out string why)
        {
            why = null;
            var reasons = new List<string>();
            foreach (var starter in _starters)
            {
                if (starter.CanStart(level, out var reason)) return starter;
                reasons.Add(starter.Name + ": " + reason);
            }
            why = string.Join("; ", reasons.ToArray());
            return null;
        }

        /// Ask for a level change. Never forced: the caller decides whether the player agreed.
        public WardensTransition Start(WardensLevel level, CampaignMode mode)
        {
            if (level == null) return WardensTransition.Fail("no such level");
            var starter = Choose(level, out var why);
            if (starter == null)
                return WardensTransition.Fail("No way to start level " + level.Id + " (" + why + ").");

            var result = starter.Start(level, mode);
            foreach (var missing in result.Missing)
                Debug.LogWarning("[Wardens] transition (" + result.Strategy + "): " + missing);

            // A strategy that failed is not the end of it: the handoff still reaches the menu.
            if (!result.Started && !(starter is HandoffLevelStarter))
            {
                Debug.Log("[Wardens] transition: '" + starter.Name + "' did not start the level; falling back to the handoff");
                var fallback = _starters[_starters.Count - 1];
                var second = fallback.Start(level, mode);
                second.Missing.InsertRange(0, result.Missing);
                return second;
            }
            return result;
        }
    }
}
