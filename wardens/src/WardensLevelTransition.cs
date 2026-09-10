// WardensLevelTransition.cs. Loading the next level's map from inside the mod.
//
// design/wardens-campaign-design.md §4.4. Timberborn has no campaign, so "start level 02" is not a
// thing the game offers: a level is a map, and changing level means putting the player on a
// different map. There are three ways to do that and they differ in how much of the game's API has
// actually been verified, so this file implements all three behind one interface and picks the best
// one available at load, loudly.
//
//   HandoffLevelStarter   writes campaign.handoff.json, then asks the main menu to start the level.
//                         Needs nothing unverified in the Game context: worst case the player
//                         returns to the menu themselves and the handoff still fires.
//   NewGameLevelStarter   a real programmatic new game (MapItemProvider -> NewGameConfiguration ->
//                         GameSceneLoader). Every one of those is on the "not verified" list in
//                         wardens-campaign-maps.md §5, so it is reached by reflection: if the shape
//                         is not what we guessed, CanStart says so by name and the chain moves on.
//   SaveLevelStarter      loads a shipped save through ValidatingGameLoader, the path
//                         TimberbotAutoLoad already proves — SaveReference, SettlementReference and
//                         UserDataFolder are used directly, since that shape is verified. Only usable
//                         for levels that ship a save, which none do yet, so it stays inert until one
//                         does and something registers the loader for the Game scene.
//
// The transition is offered, never forced (§4.4): the player may want to finish something first.
// `Start` returns a WardensTransition saying what happened and what the player should see, and the
// caller — the completion card, the MCP `campaign next` action — decides how to say it.
//
// Nothing in this file has been compiled against the game. Treat every log line it emits on the
// first real run as a finding: they are written to name the exact member that was missing.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Timberborn.GameSaveRepositorySystem;
using Timberborn.GameSaveRepositorySystemUI;
using Timberborn.PlatformUtilities;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Wardens
{
    /// What the next level should start with. Empty means "whatever the player picks".
    public sealed class CampaignMode
    {
        public JObject NewGameMode;                 // a captured vanilla NewGameMode, if we have one
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
        public readonly List<string> Missing = new List<string>();   // API members that were absent

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

    // --- the handoff file: the one mechanism that needs nothing unverified ----------------------------

    /// campaign.handoff.json, next to campaign.json. Written in the Game scene, read by the main menu.
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
        public string Name => "handoff";

        public bool CanStart(WardensLevel level, out string why)
        {
            why = null;
            if (level == null) { why = "no level"; return false; }
            // Deliberately unconditional otherwise: writing a file and telling the player what to do
            // works even when every game API we hoped for turns out to be shaped differently.
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
            // Returning to the menu from code is not on the verified list, so it is attempted and
            // never depended on. Either way the handoff is armed and fires at the next main menu.
            var returned = TryReturnToMainMenu(result.Missing);
            result.Started = false;      // the level starts at the menu, not here
            result.Message = returned
                ? "Returning to the main menu to start level " + level.Id + ", " + level.Title + "."
                : "Level " + level.Id + " (" + level.Title + ") is queued. Return to the main menu and it " +
                  "will start; or pick " + level.MapName + " under New Game -> Custom maps.";
            return result;
        }

        /// Best-effort "go back to the menu", by the same call the pause menu uses.
        private static bool TryReturnToMainMenu(List<string> missing)
        {
            var loaderType = WardensReflect.FindType("Timberborn.GameSceneLoading.GameSceneLoader")
                             ?? WardensReflect.FindType("GameSceneLoader");
            if (loaderType == null)
            {
                missing.Add("GameSceneLoader (type not found)");
                return false;
            }
            var loader = WardensServiceLocator.Resolve(loaderType);
            if (loader == null)
            {
                missing.Add("GameSceneLoader (no instance reachable from the Game context)");
                return false;
            }
            foreach (var method in new[] { "StartMainMenu", "LoadMainMenu", "GoToMainMenu" })
            {
                WardensReflect.Call(loader, method, out var why);
                if (why == null) return true;
                missing.Add(why);
            }
            return false;
        }
    }

    // --- the programmatic new game: everything here is unverified -------------------------------------

    public sealed class NewGameLevelStarter : ILevelStarter
    {
        public const string FactionId = WardensStartingPopulation.FactionId;

        public string Name => "new game";

        public bool CanStart(WardensLevel level, out string why)
        {
            why = null;
            if (level == null) { why = "no level"; return false; }
            if (!level.Shipped) { why = "level " + level.Id + " ships no map yet"; return false; }
            if (FindMapReference(level.MapName, out var missing) == null)
            {
                why = "no MapFileReference for '" + level.MapName + "'" +
                      (missing != null ? " (" + missing + ")" : "; is the map installed?");
                return false;
            }
            if (WardensReflect.FindType("Timberborn.GameSceneLoading.GameSceneLoader") == null)
            {
                why = "GameSceneLoader not found in any loaded assembly";
                return false;
            }
            return true;
        }

        public WardensTransition Start(WardensLevel level, CampaignMode mode)
        {
            var result = new WardensTransition { Strategy = Name };

            var mapReference = FindMapReference(level.MapName, out var mapMissing);
            if (mapReference == null)
            {
                if (mapMissing != null) result.Missing.Add(mapMissing);
                result.Message = "Could not find the map '" + level.MapName + "' in the game's map list.";
                return result;
            }

            var configurationType = WardensReflect.FindType("Timberborn.NewGameConfigurationSystem.NewGameConfiguration")
                                    ?? WardensReflect.FindType("NewGameConfiguration");
            if (configurationType == null)
            {
                result.Missing.Add("NewGameConfiguration (type not found)");
                result.Message = "This build of the game does not expose NewGameConfiguration where we expected it.";
                return result;
            }

            // The record's shape is unverified: the game's own log prints FactionId, MapFileReference
            // and NewGameMode, so those three in that order are the first guess, and a two-argument
            // form (faction, map) the second. Whichever is wrong says so by name.
            var newGameMode = ResolveNewGameMode(mode, result.Missing);
            object configuration = null;
            string threeArgs = null, twoArgs = null;
            if (newGameMode != null)
                configuration = WardensReflect.Construct(configurationType, out threeArgs, FactionId, mapReference, newGameMode);
            if (configuration == null)
                configuration = WardensReflect.Construct(configurationType, out twoArgs, FactionId, mapReference);
            if (configuration == null)
            {
                // Both guesses are wrong: say what the type actually wants, which is the finding.
                if (threeArgs != null) result.Missing.Add(threeArgs);
                if (twoArgs != null) result.Missing.Add(twoArgs);
                result.Message = "Could not build a new-game configuration for level " + level.Id + ".";
                return result;
            }

            var loaderType = WardensReflect.FindType("Timberborn.GameSceneLoading.GameSceneLoader");
            var loader = loaderType != null ? WardensServiceLocator.Resolve(loaderType) : null;
            if (loader == null)
            {
                result.Missing.Add("GameSceneLoader (no instance reachable in this scene)");
                result.Message = "The game's scene loader is not reachable from here.";
                return result;
            }

            foreach (var method in new[] { "StartNewGame", "LoadNewGame", "StartGame" })
            {
                WardensReflect.Call(loader, method, out var why, configuration);
                if (why == null)
                {
                    result.Started = true;
                    result.Message = "Starting level " + level.Id + ", " + level.Title + ".";
                    Debug.Log("[Wardens] transition: started level " + level.Id + " through GameSceneLoader." + method);
                    return result;
                }
                result.Missing.Add(why);
            }
            result.Message = "Could not start level " + level.Id + " programmatically.";
            return result;
        }

        /// A MapFileReference for a map name, taken from the game's own list — never built by hand
        /// (it has eight constructor parameters, six of them unknown; wardens-campaign-maps.md §5).
        public static object FindMapReference(string mapName, out string missing)
        {
            missing = null;
            foreach (var sourceName in new[] { "Timberborn.MapItemsUI.MapItemProvider", "MapItemProvider",
                                               "Timberborn.MapRepositorySystem.MapRepository" })
            {
                var type = WardensReflect.FindType(sourceName);
                if (type == null) continue;
                var source = WardensServiceLocator.Resolve(type);
                if (source == null) continue;
                foreach (var method in new[] { "GetCustomMaps", "GetUserMaps", "GetMaps" })
                {
                    var listed = WardensReflect.Call(source, method, out var why);
                    if (why != null) { missing = why; continue; }
                    foreach (var item in WardensReflect.Enumerate(listed))
                    {
                        var reference = item;
                        // MapItem wraps the reference; MapRepository may hand it over directly.
                        var wrapped = WardensReflect.Get(item, "MapFileReference", out _);
                        if (wrapped != null) reference = wrapped;
                        var name = WardensReflect.Get(reference, "Name", out _) as string;
                        if (string.Equals(name, mapName, StringComparison.OrdinalIgnoreCase))
                        {
                            missing = null;
                            return reference;
                        }
                    }
                }
            }
            if (missing == null) missing = "no map source (MapItemProvider / MapRepository) answered with '" + mapName + "'";
            return null;
        }

        /// The player's difficulty settings for the next level. Never invented: either the captured
        /// mode from campaign.json, or the game's own default, or nothing (and the caller falls back).
        private static object ResolveNewGameMode(CampaignMode mode, List<string> missing)
        {
            var type = WardensReflect.FindType("Timberborn.NewGameModeSystem.NewGameMode")
                       ?? WardensReflect.FindType("NewGameMode");
            if (type == null)
            {
                missing.Add("NewGameMode (type not found)");
                return null;
            }
            // A captured mode would be replayed here once WP-decompile confirms the record's fields.
            // Until then, ask the game for its own default rather than guessing twenty parameters.
            foreach (var providerName in new[] { "Timberborn.NewGameModeSystem.NewGameModeProvider", "NewGameModeService" })
            {
                var providerType = WardensReflect.FindType(providerName);
                var provider = providerType != null ? WardensServiceLocator.Resolve(providerType) : null;
                if (provider == null) continue;
                foreach (var method in new[] { "GetDefaultNewGameMode", "GetNewGameMode", "Default" })
                {
                    var value = WardensReflect.Call(provider, method, out var why);
                    if (why == null && value != null) return value;
                }
            }
            missing.Add("NewGameMode default (no provider answered; the level will use whatever the caller supplies)");
            return null;
        }
    }

    // --- a shipped save: proven API, but no level ships one yet ---------------------------------------

    public sealed class SaveLevelStarter : ILevelStarter
    {
        public const string SettlementName = "Wardens Campaign";

        public string Name => "shipped save";

        public bool CanStart(WardensLevel level, out string why)
        {
            why = null;
            if (level == null) { why = "no level"; return false; }
            var path = SavePath(level);
            if (!File.Exists(path)) { why = "no shipped save at " + path; return false; }
            if (WardensServiceLocator.Resolve(typeof(ValidatingGameLoader)) == null)
            {
                why = "ValidatingGameLoader is not reachable in this scene (it is injected in the main "
                      + "menu by TimberbotAutoLoad; a Game-context binding is unconfirmed)";
                return false;
            }
            return true;
        }

        public WardensTransition Start(WardensLevel level, CampaignMode mode)
        {
            var result = new WardensTransition { Strategy = Name };

            // SaveReference, SettlementReference and UserDataFolder are referenced types with known
            // shapes — TimberbotAutoLoad constructs exactly these and that path is proven — so they
            // are used directly rather than guessed at through reflection. The only uncertainty left
            // is getting hold of the loader, which is why that one goes through the locator.
            var loader = WardensServiceLocator.Resolve(typeof(ValidatingGameLoader)) as ValidatingGameLoader;
            if (loader == null)
            {
                result.Missing.Add("ValidatingGameLoader (no instance reachable in this scene)");
                result.Message = "Could not reach the game's save loader.";
                return result;
            }

            try
            {
                var settlement = new SettlementReference(SettlementName, SavesDirectory);
                loader.LoadGame(new SaveReference(level.MapName, settlement));
                result.Started = true;
                result.Message = "Loading level " + level.Id + ", " + level.Title + ".";
                return result;
            }
            catch (Exception ex)
            {
                result.Missing.Add("ValidatingGameLoader.LoadGame threw: " + ex.Message);
                result.Message = "Could not load the shipped save for level " + level.Id + ".";
                return result;
            }
        }

        public static string SavesDirectory => System.IO.Path.Combine(UserDataFolder.Folder, "Saves");

        public static string SavePath(WardensLevel level) =>
            System.IO.Path.Combine(SavesDirectory, SettlementName, level.MapName + ".timber");
    }

    // --- the service that picks one ---------------------------------------------------------------------

    public sealed class WardensLevelTransitionService : ILoadableSingleton
    {
        private readonly List<ILevelStarter> _starters = new List<ILevelStarter>
        {
            // Order matters: the best experience first, the one that always works last.
            new NewGameLevelStarter(),
            new SaveLevelStarter(),
            new HandoffLevelStarter(),
        };

        public void Load()
        {
            // Say at load which strategies are available, so the first run on a real machine reports
            // the answer to wardens-campaign-maps.md §5 instead of leaving it open.
            var next = WardensCampaignService.ById("02") ?? WardensCampaignService.Levels[0];
            foreach (var starter in _starters)
            {
                var can = starter.CanStart(next, out var why);
                Debug.Log($"[Wardens] transition strategy '{starter.Name}': " +
                          (can ? "available" : "unavailable — " + why));
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
                return WardensTransition.Fail("No way to start level " + level.Id + " (" + why + "). " +
                                              "Pick " + level.MapName + " under New Game -> Custom maps.");

            var result = starter.Start(level, mode);
            foreach (var missing in result.Missing)
                Debug.LogWarning("[Wardens] transition (" + result.Strategy + "): " + missing);

            // A strategy that failed is not the end of it: fall through to the next one that can.
            if (!result.Started && starter.Name != "handoff")
            {
                Debug.Log("[Wardens] transition: '" + starter.Name + "' did not start the level; falling back");
                var fallback = new HandoffLevelStarter();
                var second = fallback.Start(level, mode);
                second.Missing.InsertRange(0, result.Missing);
                return second;
            }
            return result;
        }
    }
}
