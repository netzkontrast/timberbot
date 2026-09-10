// WardensMainMenuConfigurator.cs. Bindito registration for the main menu.
//
// [Context("MainMenu")] runs once when the menu loads, before any game exists. The only thing the
// Wardens need there is the campaign's maps: the game lists custom maps from
// Documents/Timberborn/Maps and nowhere else, so WardensMapInstaller copies them across and
// refreshes the list before the player opens the New Game screen.
//
// It is also where a level change completes: WardensHandoff reads campaign.handoff.json, written by
// the Game scene when the player accepts "continue to the next level"
// (design/wardens-campaign-design.md §4.4, WardensLevelTransition.cs).
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
            // The menu is also where a queued level change lands: the Game scene writes
            // campaign.handoff.json, this reads it. Bound after the installer and depending on it,
            // so the map exists before anything looks for a reference to it.
            Bind<WardensHandoff>().AsSingleton();
        }
    }
}
