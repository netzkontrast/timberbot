// WardensMainMenuConfigurator.cs. Bindito registration for the main menu.
//
// [Context("MainMenu")] runs once when the menu loads, before any game exists. The only thing the
// Wardens need there is the campaign's maps: the game lists custom maps from
// Documents/Timberborn/Maps and nowhere else, so WardensMapInstaller copies them across and
// refreshes the list before the player opens the New Game screen.
//
// It is also where a queued level starts: WardensHandoff reads campaign.handoff.json, written by the
// Game scene when a level change has to go through the menu, or by hand before a launch, and starts
// that level as a new game (design/wardens-campaign-design.md §4.4, WardensLevelTransition.cs).
//
// Everything else (the MCP server, chat, chapters, the campaign record) is Game context and lives
// in WardensConfigurator. The Timberbot API compiled into this mod registers its own main-menu
// singleton through Timberbot/TimberbotAutoLoadConfigurator.cs.

using Bindito.Core;

namespace Wardens
{
    [Context("MainMenu")]
    public class WardensMainMenuConfigurator : Configurator
    {
        public override void Configure()
        {
            Bind<WardensMapInstaller>().AsSingleton();
            // The menu is also where a queued level starts: campaign.handoff.json names it, this
            // starts it. It calls the installer's EnsureInstalled() first, so the map is in place
            // whichever of the two loads first.
            Bind<WardensHandoff>().AsSingleton();
        }
    }
}
