// WardensMapInstaller.cs. Put the campaign's maps where the game can see them.
//
// design/wardens-campaign-maps.md §2 A, and §6 step 1. The game lists custom maps from exactly one
// place, and a mod's own folder is not it. Verified against the 1.1 decompile of
// Timberborn.MapRepositorySystem:
//
//   MapRepository.UserMapsDirectory => Path.Combine(UserDataFolder.Folder, "Maps")
//   MapRepository.GetUserMapNames() enumerates *.timber in that directory, nothing else
//   MapRepository.NotifyMapRepositoryChanged() posts MapRepositoryChangedEvent
//
// So this copies <mod>/Maps/*.timber into the user's Maps folder at the main menu and refreshes the
// list. The dev-machine deploy does the same copy from MSBuild (Wardens.csproj, the Deploy target);
// this is the path for everyone who installs the mod from the Workshop or mod.io.
//
// Runs in [Context("MainMenu")] so it happens once, before any game is loaded and before the New
// Game screen builds its custom-map list.
//
// A cleaner mechanism exists and is not used yet: Timberborn.MapItemsUI exposes
// `ICustomMapItemFactory`, and `MapItemProvider.GetCustomMaps()` concatenates every implementation's
// items with the user's own maps. A MultiBind of that interface returning
// `MapFileReference.FromDisk(<mod>/Maps/x.timber)` would list the mod's maps in place, with no copy
// and nothing left behind on uninstall. It is the better design; it is not shipped here because it
// cannot be verified without loading the game, and the copy path uses only APIs that were read
// straight out of the assembly. See design/wardens-campaign-maps.md §2 A.

using System;
using System.IO;
using System.Reflection;
using Timberborn.MapRepositorySystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensMapInstaller : ILoadableSingleton
    {
        // Names this mod shipped under and no longer does. The stale copy in the player's Maps
        // folder is our own leftover, and leaving it there puts a second, near-identical entry in
        // the campaign's part of the map list. Removed only when the replacement installed cleanly.
        private static readonly string[] RetiredMapNames = { "Wardens Wasteland" };

        private readonly MapRepository _mapRepository;

        public int Installed { get; private set; }
        public int Skipped { get; private set; }
        public string SourceDirectory { get; private set; } = "";

        public WardensMapInstaller(MapRepository mapRepository)
        {
            _mapRepository = mapRepository;
            // The level transition needs a map source and must not add a binding of its own to find
            // one (WardensServiceLocator.cs says why). This is a service we are legitimately handed.
            WardensServiceLocator.Register(mapRepository);
        }

        public void Load()
        {
            try
            {
                Install();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Wardens] map install failed: " + ex);
            }
        }

        private void Install()
        {
            var settings = WardensSettings.Load();
            if (!settings.InstallMaps)
            {
                Debug.Log("[Wardens] map install disabled in settings.json (installMaps=false)");
                return;
            }

            SourceDirectory = ModMapsDirectory();
            if (!Directory.Exists(SourceDirectory))
            {
                Debug.LogWarning($"[Wardens] no Maps folder in the mod ({SourceDirectory}); " +
                                 "the campaign maps will not appear under Custom maps");
                return;
            }

            var target = MapRepository.UserMapsDirectory;
            Directory.CreateDirectory(target);

            foreach (var source in Directory.GetFiles(SourceDirectory, "*.timber"))
            {
                var name = Path.GetFileName(source);
                var destination = Path.Combine(target, name);
                try
                {
                    if (UpToDate(source, destination)) { Skipped++; continue; }
                    File.Copy(source, destination, overwrite: true);
                    Installed++;
                    Debug.Log($"[Wardens] installed map '{Path.GetFileNameWithoutExtension(name)}' -> {destination}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Wardens] cannot install map {name}: {ex.Message}");
                }
            }

            if (Installed > 0) RemoveRetired(target);

            // The New Game screen builds its list from MapRepository; without this the maps only
            // show up after a restart.
            if (Installed > 0)
            {
                _mapRepository.NotifyMapRepositoryChanged();
                Debug.Log($"[Wardens] maps: {Installed} installed, {Skipped} already current, list refreshed");
            }
            else
            {
                Debug.Log($"[Wardens] maps: {Skipped} already current in {target}");
            }

            WarnAboutMissingLevels();
        }

        // Same size and no older than the source: the shipped maps are byte-reproducible
        // (tools/gen_map.py pins the seed and the zip timestamps), so this is enough and it keeps
        // the main menu from rewriting a dozen files on every launch.
        private static bool UpToDate(string source, string destination)
        {
            if (!File.Exists(destination)) return false;
            var a = new FileInfo(source);
            var b = new FileInfo(destination);
            return a.Length == b.Length && b.LastWriteTimeUtc >= a.LastWriteTimeUtc;
        }

        private static void RemoveRetired(string target)
        {
            foreach (var retired in RetiredMapNames)
            {
                var path = Path.Combine(target, retired + ".timber");
                if (!File.Exists(path)) continue;
                try
                {
                    File.Delete(path);
                    Debug.Log($"[Wardens] removed the retired map '{retired}' from {target} " +
                              "(it shipped under that name in an earlier version; saves made on it are unaffected)");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Wardens] cannot remove the retired map {retired}: {ex.Message}");
                }
            }
        }

        // The level table is the campaign; say plainly which of its maps the player will not find.
        private void WarnAboutMissingLevels()
        {
            foreach (var level in WardensCampaignService.Levels)
            {
                if (!level.Shipped) continue;
                var path = Path.Combine(MapRepository.UserMapsDirectory, level.MapName + ".timber");
                if (!File.Exists(path))
                    Debug.LogWarning($"[Wardens] campaign level {level.Id} ({level.Title}) has no map at {path}");
            }
        }

        // The mod's own folder: where this assembly was loaded from. Falls back to the documented
        // location, which is what the dev deploy uses.
        private static string ModMapsDirectory()
        {
            try
            {
                var location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(location))
                {
                    var dir = Path.GetDirectoryName(location);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(Path.Combine(dir, "Maps")))
                        return Path.Combine(dir, "Maps");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] cannot resolve the mod folder from the assembly: " + ex.Message);
            }
            return Path.Combine(Path.GetDirectoryName(WardensSettings.Path) ?? "", "Maps");
        }
    }
}
