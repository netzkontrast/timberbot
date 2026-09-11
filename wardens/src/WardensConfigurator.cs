// WardensConfigurator.cs. Bindito DI registration for the Wardens faction mod.
//
// [Context("Game")]: runs when a game (new or loaded) is set up, not on the main menu.
// The Timberbot API that is compiled into this mod registers itself through its own
// TimberbotConfigurator / TimberbotAutoLoadConfigurator (src/Timberbot/), so nothing of it
// is bound here. Faction-specific behaviour (bot start, cutscene triggers) checks
// FactionService.Current.Id == "Wardens" at runtime; the MCP server, chat panel, pointer,
// camera director and cutscene runner are faction-agnostic and load in every game.

using Bindito.Core;
using Timberborn.Emptying;
using Timberborn.EntityPanelSystem;
using Timberborn.Hauling;
using Timberborn.SimpleOutputBuildings;
using Timberborn.SimpleOutputBuildingsUI;
using Timberborn.TemplateInstantiation;
using Timberborn.TutorialSystem;

namespace Wardens
{
    [Context("Game")]
    public class WardensConfigurator : Configurator
    {
        public override void Configure()
        {
            // Spike A: swap the beavers the game spawns at new-game for bots.
            Bind<WardensStartingPopulation>().AsSingleton();

            // Agent-facing runtime: in-game MCP server, chat, pointer, camera.
            Bind<WardensCameraDirector>().AsSingleton();
            Bind<WardensPointer>().AsSingleton();
            Bind<WardensChat>().AsSingleton();
            // The bar's safety net: anything still science-priced is unlocked at load, and named.
            Bind<WardensBar>().AsSingleton();
            // The campaign: which level this map is, what the run has completed, and the Ledger
            // that crosses maps (campaign.json). The maps themselves are installed at the main
            // menu by WardensMapInstaller (WardensMainMenuConfigurator.cs).
            Bind<WardensCampaignService>().AsSingleton();
            // Loading the next level's map from inside a running game. It injects GameSceneLoader,
            // Autosaver, ValidatingGameLoader and MainMenuSceneLoader, each read in the 1.1.2.4
            // decompile as bound in the Game context (WardensLevelTransition.cs lists where).
            Bind<WardensLevelTransitionService>().AsSingleton();
            // Cutscenes: Cutscenes/*.json played by one runner (the Cold Boot is the first scene);
            // the overlay draws the letterbox, the captions and the choice cards; the story record
            // (story.json) keeps the choices and marks. design/wardens-cutscenes.md.
            Bind<WardensStoryState>().AsSingleton();
            // The three badtides the wasteland logged before day 1, replayed by a shot's `badtide`.
            Bind<WardensArchivedBadtides>().AsSingleton();
            Bind<WardensCutsceneOverlay>().AsSingleton();
            Bind<WardensCutscenes>().AsSingleton();
            // The level's tasks (Levels/<id>.tasks.json), a graph checked against the game whatever the
            // tutorial setting; each task's scene plays when it goes live (task:<level>.<Id>). Their panel
            // becomes the level-end card that starts the next level.
            Bind<WardensLevelTasks>().AsSingleton();
            Bind<WardensTaskPanel>().AsSingleton();
            // The Warden's heartbeat: sensor frames per N game ticks for the MCP `frame` tool.
            Bind<WardensFrames>().AsSingleton();
            Bind<WardensAssetDump>().AsSingleton();
            Bind<WardensMcpTools>().AsSingleton();
            Bind<WardensMcpServer>().AsSingleton();

            // Faction art: swaps the bot / carrying / zipline textures for the Wardens' recolors.
            Bind<WardensMaterialPatcher>().AsSingleton();

            // Story/tutorial goal steps, picked up by the vanilla TutorialStageService.
            MultiBind<IStepDeserializer>().To<GoodStockStepDeserializer>().AsSingleton();
            MultiBind<IStepDeserializer>().To<BotsChargedStepDeserializer>().AsSingleton();
            MultiBind<IStepDeserializer>().To<PoweredBuildingStepDeserializer>().AsSingleton();
            MultiBind<IStepDeserializer>().To<BeaversStepDeserializer>().AsSingleton();

            // Triggers for the optional tutorials (Dams, Layer tool, Haulers); see WardensTriggers.cs.
            Bind<WardensMissingDamTrigger>().AsSingleton();
            Bind<WardensPlatformBuiltTrigger>().AsSingleton();
            Bind<WardensIdleTrigger>().AsSingleton();

            // Spike B: any blueprint that carries a "PollutingBuildingSpec" gets a
            // PollutingBuilding component. Same decorator mechanism vanilla uses.
            // Decorated components are resolved through the container (BaseInstantiator ->
            // Container.GetInstance), so every component type needs a transient binding.
            Bind<PollutingBuilding>().AsTransient();
            // Bots work everywhere by default: a Wardens district center's default worker type is
            // Bot, and older saves are moved over once (WardensBotWorkforce.cs).
            Bind<WardensBotWorkforce>().AsTransient();
            Bind<WardensBotWorkforceMigration>().AsSingleton();
            // The Gate: every map records what its districts produce beyond their needs, and a Gate
            // on a later map delivers a linked district's surplus (WardensGate.cs).
            Bind<WardensDistrictExports>().AsSingleton();
            Bind<WardensMapGate>().AsTransient();
            Bind<WardensMapGateFragment>().AsSingleton();
            MultiBind<EntityPanelModule>().ToProvider<WardensGatePanelModuleProvider>().AsSingleton();
            MultiBind<TemplateModule>().ToProvider(ProvideTemplateModule).AsSingleton();
        }

        private static TemplateModule ProvideTemplateModule()
        {
            var builder = new TemplateModule.Builder();
            builder.AddDecorator<PollutingBuildingSpec, PollutingBuilding>();
            builder.AddDecorator<WardensBotWorkforceSpec, WardensBotWorkforce>();
            // The Gate's output is a vanilla SimpleOutputInventory (its spec is in the blueprint);
            // hauling it away and showing it in the panel are wired per building type in vanilla
            // (RuinsConfigurator, GatheringConfigurator), so the Gate wires the same four itself.
            builder.AddDecorator<WardensMapGateSpec, WardensMapGate>();
            builder.AddDecorator<WardensMapGateSpec, HaulCandidate>();
            builder.AddDecorator<WardensMapGateSpec, SimpleOutputInventoryHaulBehaviorProvider>();
            builder.AddDecorator<WardensMapGateSpec, EmptyOutputWorkplaceBehavior>();
            builder.AddDecorator<WardensMapGateSpec, SimpleOutputInventoryFragmentEnabler>();
            return builder.Build();
        }
    }
}
