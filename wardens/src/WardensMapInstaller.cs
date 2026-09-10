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

        public WardensMapInstaller(MapRepository mapRepository)
        {
            _mapRepository = mapRepository;
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

            var sourceDirectory = ModMapsDirectory();
            if (!Directory.Exists(sourceDirectory))
            {
                Debug.LogWarning($"[Wardens] no Maps folder in the mod ({sourceDirectory}); " +
                                 "the campaign maps will not appear under Custom maps");
                return;
            }

            var target = MapRepository.UserMapsDirectory;
            Directory.CreateDirectory(target);

            int installed = 0, skipped = 0;
            foreach (var source in Directory.GetFiles(sourceDirectory, "*.timber"))
            {
                var name = Path.GetFileName(source);
                var destination = Path.Combine(target, name);
                try
                {
                    if (UpToDate(source, destination)) { skipped++; continue; }
                    File.Copy(source, destination, overwrite: true);
                    installed++;
                    Debug.Log($"[Wardens] installed map '{Path.GetFileNameWithoutExtension(name)}' -> {destination}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Wardens] cannot install map {name}: {ex.Message}");
                }
            }

            // Retirement is a property of the shipped set, not of whether a copy happened on this
            // launch: after the first launch nothing is installed, and the stale file would survive.
            int removed = RemoveRetired(target);

            // The New Game screen builds its list from MapRepository; without this the maps only
            // show up after a restart.
            if (installed > 0 || removed > 0)
            {
                _mapRepository.NotifyMapRepositoryChanged();
                Debug.Log($"[Wardens] maps: {installed} installed, {skipped} already current, " +
                          $"{removed} retired removed, list refreshed");
            }
            else
            {
                Debug.Log($"[Wardens] maps: {skipped} already current in {target}");
            }

            WarnAboutMissingLevels(target);
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

        private static int RemoveRetired(string target)
        {
            int removed = 0;
            foreach (var retired in RetiredMapNames)
            {
                var path = Path.Combine(target, retired + ".timber");
                if (!File.Exists(path)) continue;
                try
                {
                    File.Delete(path);
                    removed++;
                    Debug.Log($"[Wardens] removed the retired map '{retired}' from {target} " +
                              "(it shipped under that name in an earlier version; saves made on it are unaffected)");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Wardens] cannot remove the retired map {retired}: {ex.Message}");
                }
            }
            return removed;
        }

        // The level table is the campaign; say plainly which of its maps the player will not find.
        private static void WarnAboutMissingLevels(string target)
        {
            foreach (var level in WardensCampaignService.Levels)
            {
                if (!level.Shipped) continue;
                var path = Path.Combine(target, level.MapName + ".timber");
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
            return Path.Combine(WardensSettings.ModDir, "Maps");
        }
    }
}
