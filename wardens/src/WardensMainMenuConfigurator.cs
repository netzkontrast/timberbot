// WardensMainMenuConfigurator.cs. Bindito registration for the main menu.
//
// [Context("MainMenu")] runs once when the menu loads, before any game exists. The only thing the
// Wardens need there is the campaign's maps: the game lists custom maps from
// Documents/Timberborn/Maps and nowhere else, so WardensMapInstaller copies them across and
// refreshes the list before the player opens the New Game screen.
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
        }
    }
}
