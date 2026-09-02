# Appendix B — Game-internal symbol inventory (drift-detector seed)

> Generated during the Phase 0 audit by a read-only sub-audit over `timberbot/src/*.cs` and `Timberbot.csproj`, spot-checked by the main audit (`WorkingHoursManager._startHours`, `CollectSummary` live reads, `ValidatePlacement`, `PlaceBuilding`, reflection sites). Anchors are `file:line` at commit `d21988a`. Game DLLs are **not** available in the audit environment, so namespaces that the source does not fully qualify are marked `UNVERIFIED`; Phase 1's `scripts/verify-identifiers.*` resolves them by reflection over the referenced assemblies.

**Access-mode legend**

| Mode | Meaning |
|---|---|
| `direct` | compile-time bound to a public game member |
| `pub-int` | compile-time bound to a member reachable only because of `Publicize="true"` (leading-underscore field / `internal` method). Renames break at build time, but a stale DLL against a new game fails at runtime. |
| `refl` | `System.Reflection` at runtime keyed on a **string literal** (named in the row). Fails at runtime, silently or with NRE. |
| `Harmony` | none exists (§3) |

## 1. Inventory

### 1.1 Bindito / DI

| Type (namespace) | Member(s) used | Access | First use | Drift |
|---|---|---|---|---|
| `Configurator` (`Bindito.Core`) | `override void Configure()` | direct | `TimberbotConfigurator.cs:11,20` | L |
| `ContextAttribute` (`Bindito.Core`) | `[Context("Game")]`, `[Context("MainMenu")]` | direct | `TimberbotConfigurator.cs:10`; `TimberbotAutoLoadConfigurator.cs:10` | M — magic context strings |
| binder | `Bind<T>().AsSingleton()` | direct | `TimberbotConfigurator.cs:22-29`; `TimberbotAutoLoadConfigurator.cs:15` | L |
| ctor injection | `TimberbotService` 8 args | direct | `TimberbotService.cs:84-92` | M |
| ctor injection | `TimberbotWrite` 23 args (21 game services) | direct | `TimberbotWrite.cs:98-121` | H — widest injection surface |
| ctor injection | `TimberbotPlacement` 15 args | direct | `TimberbotPlacement.cs:104-119` | M |
| ctor injection | `TimberbotReadV2` 20 args | direct | `TimberbotReadV2.cs:228-248` | M |
| ctor injection | `TimberbotEntityRegistry` 4, `TimberbotEvents` 5, `TimberbotDebug` 1 (`PreviewFactory`, injected but unused), `TimberbotPanel` 3, `TimberbotAutoLoad` 2 | direct | `TimberbotEntityRegistry.cs:42-46`; `TimberbotEvents.cs:37-42`; `TimberbotDebug.cs:53`; `TimberbotPanel.cs:191`; `TimberbotAutoLoad.cs:32-34` | L |

### 1.2 Lifecycle / EventBus — `Timberborn.SingletonSystem`

| Type | Member(s) | Access | First use | Drift |
|---|---|---|---|---|
| `ILoadableSingleton` | `Load()` | direct | `TimberbotService.cs:31,114`; `TimberbotAutoLoad.cs:25,40`; `TimberbotPanel.cs:33,199` | L |
| `IUpdatableSingleton` | `UpdateSingleton()` — **the main-thread pump** | direct | `TimberbotService.cs:31,357`; `TimberbotPanel.cs:33,231` | L |
| `IUnloadableSingleton` | `Unload()` | direct | `TimberbotService.cs:31,284` | L |
| `EventBus` | `Register(object)`, `Unregister(object)` | direct | `TimberbotEntityRegistry.cs:54-55`; `TimberbotService.cs:139,291` | L |
| `OnEventAttribute` | `[OnEvent]` on `void On*(TEvent e)` — 69 sites | direct | `TimberbotEvents.cs:87-194`; `TimberbotEntityRegistry.cs:148,162`; `TimberbotReadV2.cs:2319,2328` | M — 66 event types are the contract |

### 1.3 Entities — `Timberborn.EntitySystem`, `Timberborn.BaseComponentSystem`

| Type | Member(s) | Access | First use | Drift |
|---|---|---|---|---|
| `EntityRegistry` | `Entities`, `GetEntity(Guid)` | direct | `TimberbotEntityRegistry.cs:118,85`; `TimberbotReadV2.cs:448` | L |
| `EntityComponent` | `EntityId` (Guid), `GameObject` (`.name`, `.GetInstanceID()`), `GetComponent<T>()` (~108 call sites, ~50 distinct `T`) | direct | `TimberbotEntityRegistry.cs:109,124,156` | M — keys the whole public-ID scheme |
| `EntityService` | `Delete(EntityComponent)` | direct | `TimberbotPlacement.cs:297` | L |
| `EntityInitializedEvent` / `EntityDeletedEvent` / `EntityCreatedEvent` | `.Entity` | direct | `TimberbotEntityRegistry.cs:149-167`; `TimberbotReadV2.cs:2320-2329`; `TimberbotEvents.cs:164` | L |
| `BaseComponent` | param type in `PostNotification` (dead: no callers) | direct | `TimberbotService.cs:366` | L |

### 1.4 Blocks / placement / validation — `Timberborn.BlockSystem`, `Timberborn.BlockObjectTools`, `Timberborn.Coordinates`

| Type | Member(s) | Access | First use | Drift |
|---|---|---|---|---|
| `BlockObject` | `IsValid()`, `IsFinished`, `Coordinates`, `CoordinatesAtBaseZ`, `HasEntrance`, `PositionedEntrance.Coordinates` / `.DoorstepCoordinates`, `PositionedBlocks.GetAllBlocks()` | direct | `TimberbotPlacement.cs:1750,1640,1782,1753,1755,1816`; `TimberbotReadV2.cs:1552,1946,2141,2157,2161,2149` | L–M |
| `BlockObject` | `Orientation` cast `(int)` into `OrientNames[]` `{south,west,north,east}` | direct | `TimberbotReadV2.cs:2145`; `TimberbotEntityRegistry.cs:39` | **H — ordinal enum dependency** |
| `BlockObject` | **`_blockObjectValidationService`**, **`_blockValidator`** | **pub-int** | `TimberbotPlacement.cs:2317,2320,2348` | **H** |
| positioned block | `.Coordinates`, `.MatterBelow` (`MatterBelow.GroundOrStackable`, `.Air`) | direct | `TimberbotPlacement.cs:1818-1825` | M |
| `BlockValidator` | `FitsInMap(block,bool)`, `BlockConflictsWithExistingObject`, `BlockConflictsWithTerrain`, `BlockConflictsWithBlockAbove`, `BlockConflictsWithBlocksBelow`, `ConflictsWithUndergroundBlockObject`, `UndergroundBlockIsNotUnderground`, `BlockConflictsWithMatterBelow(block,bool)`, `BlocksValid(PositionedBlocks)` | direct (reached only via `_blockValidator`) | `TimberbotPlacement.cs:2325-2340,2383,2406` | H |
| `BlockObjectValidationService` | `IsValid(BlockObject)`; **`_blockObjectValidators`** array; element `IsValid(BlockObject, out string)` | direct / **pub-int** | `TimberbotPlacement.cs:2318,2371-2375,2415-2419` | **H** |
| `Placement` (struct, ns UNVERIFIED) | `new Placement(Vector3Int, Orientation, FlipMode)`; `FlipMode.Unflipped` | direct | `TimberbotPlacement.cs:1748,2243,2313` | M |
| `Orientation` (`Timberborn.Coordinates`) | `(Orientation)int` ordinal cast 0..3 | direct | `TimberbotPlacement.cs:1748,1779,2047,2080,2242,2313` | **H — ordinal** |
| `BlockObjectSpec` | `Size`, `BaseZ`; `PlaceableBlockObjectSpec` (arg to `PreviewFactory.Create`) | direct | `TimberbotPlacement.cs:250,2237,1712` | L–M |
| `BlockObjectPlacerService` | `GetMatchingPlacer(BlockObjectSpec)` → placer `.Place(BlockObjectSpec, Placement, Action<entity>)` | direct | `TimberbotPlacement.cs:2245-2249` | M |
| `PreviewFactory` | `Create(PlaceableBlockObjectSpec)` → `Preview` (`Reposition(Placement)`, `.BlockObject`, `.GameObject` → `UnityEngine.Object.Destroy`) | direct | `TimberbotPlacement.cs:1716,2014,2314,1749,2438` | M |
| `BlockObjectTool` (`Timberborn.BlockObjectTools`) | `Template.GetSpec<T>()` | direct | `TimberbotWrite.cs:708` | M |
| `BlockObjectSetEvent`, `BlockObjectUnsetEvent`, `EnteredFinishedStateEvent` (`.BlockObject`), `EnteredUnfinishedStateEvent`, `ExitedFinishedStateEvent` | types | direct | `TimberbotEvents.cs:99,158-161` | L–M |

### 1.5 Buildings / templates / science

| Type | Member(s) | Access | First use | Drift |
|---|---|---|---|---|
| `BuildingService` (`Timberborn.Buildings`) | `Buildings`, `GetBuildingTemplate(string)` | direct | `TimberbotPlacement.cs:214,243,438,2211`; `TimberbotReadV2.cs:1322` | M |
| template | `GetSpec<T>()` (declaring type UNVERIFIED; note `buildingSpec.GetSpec<BuildingSpec>()` at `TimberbotPlacement.cs:2221`) | direct | `TimberbotPlacement.cs:216` | M |
| `BuildingSpec` | `ScienceCost`, `BuildingCost` | direct | `TimberbotPlacement.cs:256,263` | L–M |
| goods-amount element | props **`"GoodId"`** / fallback **`"Id"`**, **`"Amount"`** | **refl** | `TimberbotPlacement.cs:266-267` | **H** |
| `TemplateSpec` (`Timberborn.TemplateSystem`) | `TemplateName` — source of every prefab name | direct | `TimberbotPlacement.cs:218,709,1327` | M |
| `PausableBuilding` | `Pause()`, `Resume()`, `Paused` | direct | `TimberbotWrite.cs:236-241`; `TimberbotReadV2.cs:1554` | L |
| `ScienceService` (`Timberborn.ScienceSystem`) | `SciencePoints` | direct | `TimberbotPlacement.cs:2223`; `TimberbotWrite.cs:713`; `TimberbotReadV2.cs:771,839` | L |
| `BuildingUnlockingService` (ns UNVERIFIED) | `Unlocked(BuildingSpec)`, `Unlock(BuildingSpec)` | direct | `TimberbotPlacement.cs:257,2223`; `TimberbotWrite.cs:712,717`; `TimberbotReadV2.cs:1332` | M |
| `ToolButtonService` (`Timberborn.ToolButtonSystem`) | `ToolButtons` → `.Tool as BlockObjectTool` | direct | `TimberbotWrite.cs:703-706` | M |
| `ToolUnlockingService` (ns UNVERIFIED) | **`UnlockInternal(BlockObjectTool, Action)`** | **pub-int** | `TimberbotWrite.cs:718` | **H** |

### 1.6 Terrain / water / soil / map index

| Type | Member(s) | Access | First use | Drift |
|---|---|---|---|---|
| `ITerrainService` (`Timberborn.TerrainSystem`) | `Size` | direct | `TimberbotPlacement.cs:167`; `TimberbotReadV2.cs:1107` | L |
| `IThreadSafeColumnTerrainMap` | `ColumnCounts`, `GetColumnCeiling(int)` | direct | `TimberbotPlacement.cs:171,174`; `TimberbotReadV2.cs:1198,1202` | L |
| `IThreadSafeWaterMap` (`Timberborn.WaterSystem`) | `ColumnCount(int)`, `WaterColumns` (`.WaterDepth`, `.Ceiling`), `ColumnContamination(Vector3Int)` | direct | `TimberbotPlacement.cs:194-200`; `TimberbotReadV2.cs:1206-1214` | L |
| `MapIndexService` (`Timberborn.MapIndexSystem`) | `CellToIndex(Vector2Int)`, `VerticalStride` | direct | `TimberbotPlacement.cs:169-170`; `TimberbotReadV2.cs:1108,1197` | M — index arithmetic contract |
| `ISoilContaminationService` | `SoilIsContaminated(Vector3Int)` | direct | `TimberbotReadV2.cs:1221` | L |
| `ISoilMoistureService` | `SoilIsMoist(Vector3Int)` | direct | `TimberbotWrite.cs:634`; `TimberbotReadV2.cs:1222` | L |

### 1.7 Navigation (reflection cluster)

| Type | Member(s) | Access | First use (literal) | Drift |
|---|---|---|---|---|
| `INavMeshService` (`Timberborn.Navigation`) | injected; only `.GetType()` used | refl entry | `TimberbotPlacement.cs:96,113,1654` | H |
| impl of `INavMeshService` | field **`"_nodeIdService"`** → cast `NodeIdService` (`GridToId(Vector3Int)`, `IdToWorld(int)`) | **refl** | `TimberbotPlacement.cs:1654-1663` (dup `1945-1953`) | **H** |
| `BuildingCachingFlowField` (`Timberborn.BuildingsNavigation`) | field **`"_accessCoordinates"`** — `GetField` result **not null-checked** before `GetValue` | **refl** | `TimberbotPlacement.cs:1661` (dup `1952`) | **H — NRE on rename** |
| `DistrictPathNavRangeDrawer` | field **`"_navigationRangeService"`** → method **`"GetRoadNodesInRange"`**(Vector3) → element props **`"Coordinates"`**, **`"Distance"`** | **refl** | `TimberbotPlacement.cs:1667-1678` (dup `1958-1969`) | **H — duplicated verbatim** |
| `BuildingTerrainRange` | `GetRange()` | direct | `TimberbotWrite.cs:787,1026` | L |

### 1.8 Save / settlement (reflection chain + autoload)

| Target | Member | Access | First use | Drift |
|---|---|---|---|---|
| `GameCycleService` as reflection root | private chain **`"_singletonLoader"` → `"_serializedWorldSupplier"` → `"_sceneLoader"` → `"_sceneParameters"`** then props **`"SaveReference"`**, **`"SettlementReference"`**, **`"SettlementName"`** | **refl** | `TimberbotReadV2.cs:1529-1540` (runs on main thread inside `BuildSettlementSnapshot` `:1274`) | **H — 4-deep private walk; any rename → settlement `"unknown"`** |
| `GameSaveRepository` (`Timberborn.GameSaveRepositorySystem`) | `GetSaves(SettlementReference)`, `SaveExists(SaveReference)` | direct | `TimberbotAutoLoad.cs:83,91` | M |
| `SettlementReference(string,string)`, `SaveReference(string, SettlementReference)` | ctors | direct | `TimberbotAutoLoad.cs:74,79` | M |
| `ValidatingGameLoader` (`Timberborn.GameSaveRepositorySystemUI`) | `LoadGame(SaveReference)` | direct | `TimberbotAutoLoad.cs:98` | M |
| `UserDataFolder` (`Timberborn.PlatformUtilities`) | `Folder` | direct | `TimberbotAutoLoad.cs:73` | M |

### 1.9 Components probed via `GetComponent<T>()` (first-use anchors; `W` = `TimberbotWrite.cs`, `RV2` = `TimberbotReadV2.cs`)

| Component (namespace) | Members used | Drift |
|---|---|---|
| `Floodgate` (`Timberborn.WaterBuildings`) | `MaxHeight`, `SetHeightAndSynchronize(float)`, `Height` (`W:267-273`) | M |
| `BuilderPrioritizable` (`Timberborn.BuilderPrioritySystem`) | `SetPriority(Priority)`, `Priority` (`W:295-312`) | L |
| `Workplace` (`Timberborn.WorkSystem`) | `MaxWorkers`, `SetDesiredWorkers(int)`, `DesiredWorkers`, `NumberOfAssignedWorkers` (`W:467-475`) | L |
| `WorkplacePriority` (ns UNVERIFIED) | `SetPriority(Priority)`, `Priority` (`W:294-306`) | L |
| `EntityReachabilityStatus` (`Timberborn.BuildingsReachability`) | `IsAnyUnreachable()` (`RV2:2093,1555`) | M |
| `MechanicalBuilding` / `MechanicalNode` (`Timberborn.MechanicalSystem`) | `ActiveAndPowered`; `IsGenerator`, `IsConsumer`, **`_nominalPowerInput`**, **`_nominalPowerOutput`** (**pub-int**), `Graph` (`.PowerDemand`, `.PowerSupply`) (`RV2:1557,2131-2134,1589-1593`) | **H (two `_nominal*` fields)** |
| `StatusSubject` (`Timberborn.StatusSystem`) | `ActiveStatuses` → `.StatusDescription` (`RV2:1648-1653`) | M |
| `ConstructionSite` (`Timberborn.ConstructionSites`) | `BuildTimeProgress`, `MaterialProgress`, `HasMaterialsToResumeBuilding`; static `ConstructionSiteInventoryInitializer.InventoryComponentName` (`RV2:1577-1579,1626`) | M |
| `Inventories` (`Timberborn.InventorySystem`) | `AllInventories` → `.ComponentName`, `.TotalAmountInStock`, `.Capacity`, `.Stock` (`.GoodId`, `.Amount`) (`RV2:1622-1628,1826-1831`) | M |
| `Wonder` (`Timberborn.Wonders`) | `IsActive` (`RV2:1583`) | L |
| `Dwelling` / `Dweller` (`Timberborn.DwellingSystem`) | `NumberOfDwellers`, `MaxBeavers`; `HasHome` (`RV2:1568-1569,1880`) | L |
| `Clutch` (ns UNVERIFIED) | `SetMode(ClutchMode.Engaged/Disengaged)`, `IsEngaged` (`W:251-257`) | L |
| `Manufactory` (`Timberborn.Workshops`) | `ProductionRecipes`, `SetRecipe(RecipeSpec/null)`, `HasCurrentRecipe`, `CurrentRecipe.Id`, `ProductionProgress`, `IsReadyToProduce`; `RecipeSpecService.GetRecipe(string)` (`W:345-373`; `RV2:1601-1603`) | M |
| `BreedingPod` (`Timberborn.Reproduction`) | `NeedsNutrients`, `Nutrients` (`.Amount`, `.GoodId`) (`RV2:1606-1613,1850`) | M |
| `RangedEffectBuildingSpec` (`Timberborn.RangedEffectSystem`) | `EffectRadius` (`RV2:2135`) | M |
| `DistrictBuilding` (`Timberborn.GameDistricts`) | `District.DistrictName` (`RV2:1558`) | L |
| `StockpilePriority` (`Timberborn.StockpilePrioritySystem`) | `Accept()/Obtain()/Supply()/Empty()`, `IsEmptyActive/IsObtainActive/IsSupplyActive` (`W:523-557`) | M |
| `SingleGoodAllower` (ns UNVERIFIED) | `Disallow()`, `Allow(string)`, `HasAllowedGood`, `AllowedGood` (`W:530-556`) | M |
| `HaulPrioritizable` (`Timberborn.Hauling`) | `Prioritized` get/set (`W:325-331`) | L |
| `FarmHouse` (`Timberborn.Fields`) | `PrioritizePlanting()`, `UnprioritizePlanting()` (`W:396-408`) | L |
| `PlantablePrioritizer`, `PlanterBuilding`, `PlantableSpec`, `InRangePlantingCoordinates` (`Timberborn.Planting`) | `PrioritizePlantable(PlantableSpec/null)`, `AllowedPlantables`, `TemplateName`, `GetCoordinates()` (`W:423-450,627-631`) | M |
| `NamedEntity` (`Timberborn.EntityNaming`) | `SetEntityName(string)`, `EntityName` (`W:1542-1552`) | L |
| `NeedManager` (`Timberborn.NeedSystem`) + `NeedSpec` (`Timberborn.NeedSpecs`) | `GetNeeds()`, `GetNeed(string)`, `GetNeedWellbeing(string)`; need `.IsCritical/.IsFavorable/.IsActive/.Points`; spec `.Id/.NeedGroupId`; `FactionNeedService.GetBeaverNeeds()` (`RV2:1905-1937,478`) | M |
| `WellbeingTracker`, `Worker`, `LifeProgressor`, `GoodCarrier`, `Deteriorable`, `Contaminable`, `Citizen`, `Bot` | `Wellbeing`; `Workplace.GameObject.name`; `LifeProgress`; `LiftingCapacity/IsMovementSlowed/IsCarrying/CarriedGoods`; `DeteriorationProgress`; `IsContaminated`; `AssignedDistrict.DistrictName`; presence (`RV2:1860-1891,2188-2201`) | L–M |
| `LivingNaturalResource`, `Cuttable`, `Gatherable`, `Growable` | `IsDead`; presence; presence; `IsGrown`, `GrowthProgress` (`RV2:1959-1961,2210-2230`) | L |
| `WaterInputSpec` (`Timberborn.WaterBuildings`) | `WaterInputCoordinates` (`TimberbotPlacement.cs:1626-1627`) | M |
| `DistrictResourceCounter` (`Timberborn.ResourceCountingSystem`) | `GetResourceCount(string)` → `.AllStock`, `.AvailableStock` (`RV2:1977-1987`) | M |
| `DistrictDistributionSetting` (`Timberborn.DistributionSystem`) | `GetGoodDistributionSetting(string)`, `GoodDistributionSettings`; setting `.SetImportOption(ImportOption)`, `.SetExportThreshold(int)`, `.GoodId/.ImportOption/.ExportThreshold`; `Enum.TryParse<ImportOption>` from HTTP body (`W:745-758`; `RV2:1364-1374`) | M — enum names are an HTTP contract |

### 1.10 Automation — `Timberborn.Automation` / `Timberborn.AutomationBuildings`

| Type | Member(s) | Access | First use | Drift |
|---|---|---|---|---|
| `Automator` | `IsTransmitter`, `State`, `AutomatorName`, `OutputConnections` | direct | `TimberbotWrite.cs:1049`; `TimberbotReadV2.cs:1666-1732` | L–M |
| `AutomatorConnection` | `Receiver`, `Disconnect()` | direct | `TimberbotReadV2.cs:1736`; `TimberbotWrite.cs:1142` | M |
| `Automatable` | `SetInput(Automator)`, `Input`, `IsAutomated`, **`_inputConnection`** | direct / **pub-int** | `TimberbotWrite.cs:1056,1140-1142` | **H (field)** |
| `Relay`, `Memory` | `UsesInputB`, `Mode`, `SetInputA/B(/ResetInput)`, `SetMode(RelayMode/MemoryMode)`, `InputA/B(/ResetInput)` | direct | `TimberbotWrite.cs:1073-1102,1458-1471,1162-1186` | M |
| `DepthSensor`, `ContaminationSensor`, `FlowSensor` | `SetThreshold(float)`, `SetMode(NumericComparisonMode)`, `Threshold`, `Mode` | direct | `TimberbotWrite.cs:1221-1273`; `TimberbotReadV2.cs:1756-1767` | M |
| `ResourceCounter` | `SetGoodId`, `SetThreshold(int)`, `SetFillRateThreshold(float)`, `SetMode`, `SetComparisonMode`, `SetIncludeInputs(bool)` + getters | direct | `TimberbotWrite.cs:1315-1355`; `TimberbotReadV2.cs:1771-1775` | M |
| `PopulationCounter`, `PowerMeter`, `Chronometer`, `Lever` | setters/getters per `TimberbotWrite.cs:1368-1529`, `TimberbotReadV2.cs:1779-1807` | direct | as cited | M |
| Enums `NumericComparisonMode`, `ResourceCounterMode`, `PopulationCounterMode`, `PowerMeterMode`, `RelayMode`, `MemoryMode`, `ChronometerMode` | `Enum.TryParse<T>(string, true, out T)` from HTTP body | direct | `TimberbotWrite.cs:1227,1337,1374,1419,1456,1469,1498` | M — enum member names are a public HTTP contract |

### 1.11 Time / weather / cycle / speed / work hours / districts / notifications / goods / forestry / planting

| Type | Member(s) | Access | First use | Drift |
|---|---|---|---|---|
| `IDayNightCycle` (`Timberborn.TimeSystem`) | `DayNumber`, `DayProgress`, `PartialDayNumber` | direct | `TimberbotEvents.cs:59`; `TimberbotReadV2.cs:725,786` | L |
| `SpeedManager` (`Timberborn.TimeSystem`) | `ChangeSpeed(int)`, `CurrentSpeed` (cast `(int)`, mapped through hardcoded `SpeedScale = {0,1,3,7}`) | direct | `TimberbotWrite.cs:178`; `TimberbotReadV2.cs:212,717`; `TimberbotEvents.cs:141` | M |
| `WeatherService` (`Timberborn.WeatherSystem`) | `HazardousWeatherDuration`, `IsHazardousWeather`, `TemperateWeatherDuration`, `CycleLengthInDays` | direct | `TimberbotEvents.cs:87`; `TimberbotReadV2.cs:726,791-793` | L |
| `GameCycleService` (`Timberborn.GameCycleSystem`) | `Cycle`, `CycleDay` | direct | `TimberbotEvents.cs:90-92`; `TimberbotReadV2.cs:726,789` | L |
| `WorkingHoursManager` (`Timberborn.WorkSystem`) | `EndHours`, `AreWorkingHours`, `WorkedPartOfDay` (set), **`_startHours`** | direct / **pub-int** | `TimberbotWrite.cs:188-190`; `TimberbotReadV2.cs:1314-1315` | **H — `_startHours` in arithmetic for every work-hours write** |
| `DistrictCenterRegistry` / `DistrictCenter` (`Timberborn.GameDistricts`) | `FinishedDistrictCenters`, `AllDistrictCenters`; `DistrictName`, `DistrictPopulation.NumberOfAdults/Children/Bots` | direct | `TimberbotWrite.cs:163-164`; `TimberbotReadV2.cs:1968-1975` | L–M |
| `PopulationDistributorRetriever` (`Timberborn.GameDistrictsMigration`, ns UNVERIFIED) | `GetPopulationDistributor<AdultsDistributorTemplate>(DistrictCenter)` → `.Current`, `.MigrateTo(DistrictCenter,int)` | direct | `TimberbotWrite.cs:206-213` | M |
| `NotificationSaver` / `NotificationBus` (`Timberborn.NotificationSystem`) | `Notifications` (`.Subject/.Description/.Cycle/.CycleDay`); `Post(string, BaseComponent)` (dead) | direct | `TimberbotReadV2.cs:1412-1419`; `TimberbotService.cs:369` | L–M |
| `IGoodService` (`Timberborn.Goods`) | `Goods` | direct | `TimberbotEntityRegistry.cs:67` | L |
| `Priority` (`Timberborn.PrioritySystem`) | `Enum.TryParse<Priority>`, `(int)p` into `PriorityNames[]` | direct | `TimberbotWrite.cs:289`; `TimberbotEntityRegistry.cs:40,59-60` | **H — ordinal** |
| `TreeCuttingArea` (`Timberborn.Forestry`) | `IsInCuttingArea(Vector3Int)`, `AddCoordinates(List<Vector3Int>)`, `RemoveCoordinates(List<Vector3Int>)` | direct | `TimberbotEntityRegistry.cs:69`; `TimberbotWrite.cs:498-500` | M |
| `PlantingService` (`Timberborn.Planting`) | `SetPlantingCoordinates(Vector3Int,string)`, `UnsetPlantingCoordinates(Vector3Int)`, `IsResourceAt(Vector3Int)` | direct | `TimberbotWrite.cs:589,675,634` | M |
| `PlantingAreaValidator` (ns UNVERIFIED) | `CanPlant(Vector3Int,string)` | direct | `TimberbotWrite.cs:584,633,646,865` | M |
| `FactionService` (`Timberborn.GameFactionSystem`) | `Current.Id` → faction suffix for every prefab name | direct | `TimberbotPlacement.cs:152` | M |

### 1.12 UI and Unity

| Type | Member(s) | Access | First use | Drift |
|---|---|---|---|---|
| `UILayout` (`Timberborn.UILayoutSystem`) | `AddAbsoluteItem(VisualElement)` | direct | `TimberbotPanel.cs:214` | M |
| `VisualElementInitializer`, `NineSliceVisualElement`, `NineSliceButton`, `ToggleDisplayStyle(bool)` ext. (`Timberborn.CoreUI`) | ctors / `InitializeVisualElement` | direct | `TimberbotPanel.cs:210,218,378,511` | M |
| `UnityEngine.UIElements` | `VisualElement`, `Label`, `TextField`, `ScrollView` … | direct | `TimberbotPanel.cs:399-800` | L |
| `UnityEngine.Time` | `realtimeSinceStartup`, `frameCount` | direct | `TimberbotService.cs:260`; `TimberbotHttpServer.cs:156` | L |
| `UnityEngine.Debug` | `Log/LogWarning/LogError` — called from background threads | direct | `TimberbotLog.cs:30,36` | L |
| `UnityEngine.Object` | `Destroy(GameObject)` | direct | `TimberbotPlacement.cs:1909,2438` | M |

### 1.13 Generic reflection inspector (`/api/debug`, gated by `debugEndpointEnabled`)

`Type.GetField/GetProperty/GetMethod` on caller-supplied names, `MethodInfo.Invoke`, `BindingFlags.NonPublic` (`TimberbotDebug.cs:48,512,751,802-831`); literal `"AllComponents"` (`:788`). Drift H by design; not part of the production contract.

## 2. README-credited symbols — actual shape in code

| README credit (`README.md:147-151`) | Actual | Verdict |
|---|---|---|
| `BlockObjectPlacerService.Place()` | `GetMatchingPlacer(spec)` then `placer.Place(spec, placement, callback)` — arity 3, on the placer (`TimberbotPlacement.cs:2245-2249`) | present, signature differs |
| `TemplateInstantiator` | zero hits in `src/`; assembly referenced but unused (`Timberbot.csproj`, `Timberborn.TemplateInstantiation`) | **NOT FOUND IN CODE** |
| `MarkAsPreviewAndInitialize` | zero hits; `PreviewFactory.Create` used instead (`TimberbotPlacement.cs:1716`) | **NOT FOUND IN CODE** |
| `IsValid()` | three overloads: `BlockObject.IsValid()` (`:1750`), `BlockObjectValidationService.IsValid(BlockObject)` (`:2318`), validator `IsValid(BlockObject, out string)` (`:2375`) | present |
| `BuildingUnlockingService.Unlock()` / `.Unlocked()` | arity 1 (`TimberbotWrite.cs:717`; `TimberbotPlacement.cs:257`) | present |
| `WorkingHoursManager` | `WorkedPartOfDay = (endHours - _startHours) / 24f` (`TimberbotWrite.cs:188`) | present, via pub-int field |
| `TreeCuttingArea.AddCoordinates()` | `AddCoordinates(List<Vector3Int>)` (`TimberbotWrite.cs:498`) | present |
| `PlantingService.SetPlantingCoordinates()` | `(Vector3Int, string)` (`TimberbotWrite.cs:589`) | present |
| `PlantingAreaValidator.CanPlant()` | `(Vector3Int, string)` (`TimberbotWrite.cs:584`) | present |
| `PreviewFactory` | injected twice; unused in `TimberbotDebug` (`TimberbotDebug.cs:46,53`) | present |
| `BlockValidator` | reached only through `_blockValidator` (`TimberbotPlacement.cs:2320`) | present via pub-int |

## 3. Counts

| Metric | Count | Anchor |
|---|---|---|
| Distinct `Timberborn.*` namespaces touched | 90 (64 via `using`, 26 fully-qualified only) | grep over `src/*.cs` |
| Distinct Timberborn types | ≈168 (66 event types + ≈102 services/components/specs/enums) | §1 |
| Distinct members | ≈330 | §1 |
| Reflection string literals | 17 across 24 call sites | `TimberbotPlacement.cs:266-267,1654-1678,1945-1969`; `TimberbotReadV2.cs:1529-1540`; `TimberbotDebug.cs:788` |
| Publicized-internal members | 8 | `BlockObject._blockObjectValidationService`, `._blockValidator`; `BlockObjectValidationService._blockObjectValidators`; `MechanicalNode._nominalPowerInput/_nominalPowerOutput`; `WorkingHoursManager._startHours`; `Automatable._inputConnection`; `ToolUnlockingService.UnlockInternal` |
| Harmony / HarmonyLib | **none** | grep 0 hits; `manifest.json` has no dependencies |
| BepInEx | build-time publicizer only (`BepInEx.AssemblyPublicizer.MSBuild` 0.4.2, `PrivateAssets="all"`, `Timberbot.csproj:28`) | not a runtime dependency |
| Game DLL references | 114 `<Reference>` entries, 79 with `Publicize="true"` | `Timberbot.csproj` |
| Referenced but unused assemblies | ≈22 (`Timberborn.TemplateInstantiation`, `.BlueprintSystem`, `.Terraforming`, `.TickSystem`, …) | `Timberbot.csproj` vs `using` set; note assembly `Timberborn.BotsUpkeep` ↔ namespace `Timberborn.BotUpkeep` (`TimberbotEvents.cs:107`) |

## 4. Main-thread marshalling and where it leaks

Hook: `Timberborn.SingletonSystem.IUpdatableSingleton.UpdateSingleton()` at `TimberbotService.cs:357-365` → `DrainRequests()` (`TimberbotHttpServer.cs:140`), `ReadV2.ProcessPendingRefresh(now)` (`TimberbotReadV2.cs:491`, 1.0 ms `CaptureBudgetMs` `:209`), `ProcessWriteJobs(now, writeBudgetMs)` (`TimberbotHttpServer.cs:172`). No `ITickableSingleton`, no `MonoBehaviour.Update`, no coroutine. Every POST route is `Queued = true` (`TimberbotHttpServer.cs:506-576`).

Game services called from the **listener thread** (violating `TimberbotReadV2.cs:50-56` and `design/thread-safe-surfaces.md:110-127`):

| # | Call | Anchor | Endpoint |
|---|---|---|---|
| 1-4 | `_buildingService.Buildings`, `GetSpec<…>()`, `_buildingUnlockingService.Unlocked`, reflection over `BuildingCost` | `TimberbotPlacement.cs:240-270` | `GET /api/prefabs` |
| 5 | `_buildingService.Buildings` (error-hint path) | `TimberbotPlacement.cs:214` | `GET /api/prefabs` |
| 6-10 | `_speedManager.CurrentSpeed`, `_dayNightCycle.*`, `_gameCycleService.*`, `_weatherService.*`, `_scienceService.SciencePoints` | `TimberbotReadV2.cs:717-793` | `GET /api/summary` |
| 11-12 | `_terrainService.Size`, `_mapIndexService.CellToIndex/VerticalStride` | `TimberbotReadV2.cs:1107-1108,1197-1209` | `GET /api/tiles` |
| 13-14 | `_soilContaminationService.SoilIsContaminated`, `_soilMoistureService.SoilIsMoist` ("verify first" bucket) | `TimberbotReadV2.cs:1221-1222` | `GET /api/tiles` |

Sanctioned off-thread: `IThreadSafeColumnTerrainMap`, `IThreadSafeWaterMap` (`TimberbotReadV2.cs:1198-1214`). Lower-risk: `UnityEngine.Debug.Log*` from background threads (`TimberbotLog.cs:30,36`).

## 5. Ranked drift-detector seeds

1. `WorkingHoursManager._startHours` — `TimberbotWrite.cs:188`
2. `BlockObject._blockValidator`, `._blockObjectValidationService`, `BlockObjectValidationService._blockObjectValidators` — `TimberbotPlacement.cs:2317,2320,2371`
3. `"_singletonLoader" → "_serializedWorldSupplier" → "_sceneLoader" → "_sceneParameters"` — `TimberbotReadV2.cs:1529`
4. `"_accessCoordinates"` (unchecked `GetField`) — `TimberbotPlacement.cs:1661,1952`
5. `ToolUnlockingService.UnlockInternal` — `TimberbotWrite.cs:718`
6. `MechanicalNode._nominalPowerInput/_nominalPowerOutput` — `TimberbotReadV2.cs:2133-2134`
7. `Automatable._inputConnection` — `TimberbotWrite.cs:1142`
8. Ordinal enum casts: `Orientation`, `Priority`, `SpeedManager.CurrentSpeed` — `TimberbotPlacement.cs:1748`; `TimberbotReadV2.cs:2145,717`; `TimberbotEntityRegistry.cs:39-40,59`
9. `"GetRoadNodesInRange"`, `"Coordinates"`, `"Distance"` — `TimberbotPlacement.cs:1670-1678` (dup `1961-1969`)
10. Hardcoded content lists `TreeSpecies`/`CropSpecies` (`TimberbotEntityRegistry.cs:34-37`), `_cropNames` (`TimberbotReadV2.cs:210-211`), `_roleMap` (`TimberbotReadV2.cs:215-226`)
