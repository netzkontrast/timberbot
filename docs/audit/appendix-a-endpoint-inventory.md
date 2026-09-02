# Appendix A — Endpoint inventory (from code)

> Generated during the Phase 0 audit by a read-only sub-audit over `timberbot/src/*.cs` and `openapi.yaml`, then spot-checked against `TimberbotHttpServer.cs` by the main audit. Anchors are `file:line` at commit `d21988a`. Where the sub-audit and the main audit disagree, `docs/audit/00-repo-audit.md` wins.

**Totals found in code:** 26 GET routes + 35 POST routes = 61 HTTP method·path rows (3 paths — `/api/speed`, `/api/workhours`, `/api/distribution` — carry both GET and POST, so 58 distinct paths), plus 1 WS upgrade path (`/api/ws`) with 2 inbound command types and 4 outbound frame types.

## Legend for the shared columns

| Tag | Meaning (with anchor) |
|---|---|
| **[G]** | **GET, listener thread.** Served inline on the `Timberbot-HTTP` background thread inside `ListenLoop` at `TimberbotHttpServer.cs:326-341`; dispatched by `RouteReadRequest` at `TimberbotHttpServer.cs:422-473`. Reads a **published snapshot**: the route calls `RequestFresh(…, 2000)` which blocks the listener thread until the Unity main thread captures (`TimberbotReadV2.cs:2928`, `:3379`, `:3410`). Never touches live entity graphs — stated rule at `TimberbotReadV2.cs:50-56`. |
| **[W]** | **POST, main thread via write-job queue.** Body parsed on the listener thread (`TimberbotHttpServer.cs:347-383`), request enqueued to `ConcurrentQueue _pending` at `TimberbotHttpServer.cs:417`; `DrainRequests` (main thread, ≤10/frame) moves it to `_writeQueue` at `:157`; `ProcessWriteJobs` builds the job at `:181` and steps it under a 1 ms budget at `:207`; response sent on a ThreadPool thread at `:211`→`:669`. Game services are only touched inside `Step()` on the main thread. |
| **[E1]** | Missing/`null` body field silently coerces to a default via `req.Body?.Value<T>("k") ?? default` at the registration line — e.g. absent `id` becomes `0`, absent bool becomes the literal default. **No 400 is produced for a missing field.** |
| **[E2]** | Domain validation failure returns `{"error":"<code>: <why>. run: <cli hint>", <context keys>}` built by `TimberbotJw.Error` (`TimberbotJw.cs:149-156`) — **HTTP status stays 200**, because `LambdaWriteJob._statusCode` is initialised to `200` (`ITimberbotWriteJob.cs:39`) and no handler ever changes it. There is **no separate `"hint"` field**; the why + remediation are concatenated into the single `error` string. |
| **[E3]** | Type-mismatched JSON (e.g. `{"speed":"fast"}`) throws inside `Value<int>()` during job construction → caught at `TimberbotHttpServer.cs:194-200` → **500** `{"error":"internal_error: …"}`. |
| **[E4]** | Unknown path → `UnknownEndpoint()` (`TimberbotHttpServer.cs:481-502`) returned with **HTTP 200** (GET: `:333`; POST: `:152`). Body is `{"error":"unknown_endpoint: check spelling and use the correct HTTP method","get_endpoints":[…],"post_endpoints":[…]}`. |
| **[E5]** | Snapshot starvation → `{"error":"refresh_timeout: server is building a fresh snapshot, retry in 1s"}`, HTTP **200** (`TimberbotReadV2.cs:2929`, `:3380`, `:3411`, `:1134`). |
| **[E6]** | Global pre-route middleware, in order: **OPTIONS**→204 (`:247-253`); **401** + `WWW-Authenticate` (`:259-272`); **409** `{"error":"game_not_ready","hint":"player must press Launch in the Timberbot widget"}` (`:279-285`, body at `TimberbotAgentState.cs:369-370` — the **only** error in the codebase carrying a `hint` key); **413** `body_too_large` (`:363-367`); **400** `invalid_body` (`:378-382`). |

## Table 1 — GET routes (26)

| Method · Path | Handler (registration → body) | Params read | Response shape | R/W | Thread model | Game service(s) | Errors |
|---|---|---|---|---|---|---|---|
| `GET/ANY /api/ping` | `TimberbotHttpServer.ListenLoop` `:287` → inline literal `:289` | none | `{status,ready,openapiVersion}` — hardcoded string, **not** an `ok` envelope. `openapiVersion` from `TimberbotPure.cs:18` (`"1.0.0"`) | R | Listener thread, **no snapshot, no game service** `:289` | none | Auth-exempt `:259`; gate-exempt `TimberbotAgentState.cs:360`. **Method-agnostic** — a POST also returns this |
| `GET/ANY /api/settlement` | `:292` → `TimberbotReadV2.GetSettlementName` `TimberbotReadV2.cs:1262` | none | `{name}` (hand-built string, `:294`) | R | Listener thread; blocks on `_settlementStore.RequestFresh(2000)` `TimberbotReadV2.cs:1266` | settlement snapshot store | Timeout → `"unknown"` (`:1268-1271`), **not** an error. Method-agnostic |
| `GET /api/agent/state` | `:299` → `TimberbotAgentState.ToStateResponseJson` `TimberbotAgentState.cs:320-347` | none | `{mode,goal,ready,agentStatus,lastError,pendingRequest}` | R | Listener thread; **lock-guarded container only**, no game service `TimberbotAgentState.cs:323` | none | Gate-exempt `TimberbotAgentState.cs:362`. GET-only; POST → [E4] |
| `GET /api/summary` | `:427` → `CollectSummary` `TimberbotReadV2.cs:557` | `format` | flat: `settlement,faction,day,dayProgress,speed,cycle,cycleDay,isHazardous,tempDays,hazardDays,markedGrown,markedSeedling,unmarkedGrown,cropReady,cropGrowing,adults,children,bots,foodDays,waterDays,logDays,plankDays,gearDays,beds,homeless,workers,unemployed,wellbeing,miserable,critical,science,alerts,buildings`; `format=json` nests `housing/employment/wellbeing/dc/trees/crops/alerts/buildings` | R | [G] — 4 concurrent `RequestFresh` `:563-566` | `ScienceService`, `WeatherService`, `IDayNightCycle`, `GameCycleService`, `DistrictCenterRegistry` | [E5] |
| `GET /api/alerts` | `:429` → `CollectAlerts` `TimberbotReadV2.cs:856` | `format,limit,offset` | JSON array (FlatArrayRoute `:3406`) | R | [G] | building snapshot | [E5]; `not_ready` `:3412` |
| `GET /api/tree_clusters` | `:431` → `CollectTreeClusters` `TimberbotReadV2.cs:860` | `format` (`cellSize=10`,`top=5` **hardcoded**, not wired to query) | array of cells `{x,y,z,count,…,species[]}` | R | [G] | natural-resource snapshot | [E5] `:864` |
| `GET /api/food_clusters` | `:433` → `CollectFoodClusters` `TimberbotReadV2.cs:899` | `format` (`cellSize`,`top` hardcoded) | array of cells | R | [G] | natural-resource snapshot | [E5] |
| `GET /api/resources` | `:435` → `CollectResources` `TimberbotReadV2.cs:938` | `format` | toon: array `{district,good,available,all}`; json: object keyed by district | R | [G] `:941` | `DistrictCenterRegistry` (via snapshot) | [E5] |
| `GET /api/population` | `:437` → `CollectPopulation` `TimberbotReadV2.cs:968` | none | array `{district,adults,children,bots}` | R | [G] `:971` | district snapshot | [E5] |
| `GET /api/time` | `:439` → `CollectTime` `TimberbotReadV2.cs:985` (`ValueRoute.Collect` `:3376`) | none | `{day,dayProgress,partialDay}` (`:3429`) | R | [G] | `IDayNightCycle` (captured main-thread `:1279`) | [E5]; `not_ready` `:3381` |
| `GET /api/weather` | `:441` → `CollectWeather` `TimberbotReadV2.cs:986` | none | `{cycle,cycleDay,isHazardous,tempDays,hazardDays,cycleLength}` (`:3439`) | R | [G] | `WeatherService`, `GameCycleService` (`:1289-1300`) | [E5] |
| `GET /api/districts` | `:443` → `CollectDistricts` `TimberbotReadV2.cs:987` | `format` | array `{name,…}`; json nests `population`,`resources` | R | [G] `:990` | district snapshot | [E5] |
| `GET /api/buildings` | `:445` → `CollectBuildings` `TimberbotReadV2.cs:549` → `CollectionRoute.Collect` `:2923` | `format,detail,id,limit,offset,name,x,y,radius` | paginated `{total,offset,limit,items[]}`; each item `{id,name,x,y,z,orientation,finished,paused,priority,workers,alerts}` (`:2975`) | R | [G] `:2928` | building `ProjectionSnapshot` | [E5] `:2929` |
| `GET /api/trees` | `:447` → `CollectTrees` `TimberbotReadV2.cs:1014` | `format,limit,offset,name,x,y,radius` | paginated `{total,offset,limit,items[]}` (`:3130`) | R | [G] | natural-resource snapshot | [E5] |
| `GET /api/crops` | `:449` → `CollectCrops` `TimberbotReadV2.cs:1016` | same as trees | paginated (`:3141`) | R | [G] | natural-resource snapshot | [E5] |
| `GET /api/gatherables` | `:451` → `CollectGatherables` `TimberbotReadV2.cs:1018` | same as trees | paginated (`:3153`) | R | [G] | natural-resource snapshot | [E5] |
| `GET /api/beavers` | `:453` → `CollectBeavers` `TimberbotReadV2.cs:1020` | `format,detail,id,limit,offset,name,x,y,radius` | paginated; `detail=full` adds inventory/needs (`:3074`) | R | [G]; `detail=full` forces a full-detail capture `:2928` | beaver `ProjectionSnapshot` | [E5] |
| `GET /api/distribution` | `:455` → `CollectDistribution` `TimberbotReadV2.cs:1022` | `format` | ValueRoute object | R | [G] | district/distribution snapshot | [E5] |
| `GET /api/science` | `:457` → `CollectScience` `TimberbotReadV2.cs:1023` | `format` | ValueRoute object | R | [G] | `ScienceService`, `BuildingUnlockingService` | [E5] |
| `GET /api/wellbeing` | `:459` → `CollectWellbeing` `TimberbotReadV2.cs:1027` | `format` | aggregated need categories | R | [G] | `FactionNeedService` | [E5] |
| `GET /api/notifications` | `:461` → `CollectNotifications` `TimberbotReadV2.cs:1094` | `format,limit,offset` | array (FlatArrayRoute) | R | [G] | `NotificationSaver` | [E5]; `not_ready` |
| `GET /api/workhours` | `:463` → `CollectWorkHours` `TimberbotReadV2.cs:1096` | none | `{…}` (`:3458`) | R | [G] | `WorkingHoursManager` | [E5] |
| `GET /api/power` | `:465` → `CollectPowerNetworks` `TimberbotReadV2.cs:1097` | `format` | ValueRoute, raw pre-built JSON (`:3464`) | R | [G] | power-network snapshot | [E5] |
| `GET /api/speed` | `:467` → `CollectSpeed` `TimberbotReadV2.cs:1098` | none | `{speed,…}` (`:3452`) | R | [G] | `SpeedManager` (captured `:1302`) | [E5] |
| `GET /api/prefabs` | `:469` → `TimberbotPlacement.CollectPrefabs` `TimberbotPlacement.cs:240` | none | array `{name,sizeX,sizeY,sizeZ,scienceCost,unlocked,cost[{good,amount}]}` | R | ⚠️ **Listener thread, but iterates the LIVE game service** `_buildingService.Buildings` at `TimberbotPlacement.cs:242` and calls `GetSpec<>()`/reflection at `:243-244,:254,:266`. **No snapshot, no main-thread hop.** Violates the rule at `TimberbotReadV2.cs:50-56`. Additionally it writes into `TimberbotPlacement.Jw` (`TimberbotPlacement.cs:84`), the *same shared* `TimberbotJw` used by main-thread write jobs (`:1854`, and `TimberbotHttpServer.cs:561`) → concurrent `Reset()` on one `StringBuilder` | `BuildingService`, `BuildingUnlockingService`, `TemplateSpec`, `BlockObjectSpec`, `BuildingSpec` | Reflection failure swallowed by `try` at `:260-…`; no error contract |
| `GET /api/tiles` | **Special-cased, not in the switch** `TimberbotHttpServer.cs:330-331` → `CollectTiles` `TimberbotReadV2.cs:1103` | `format,x1,y1,x2,y2` (query only — `:320-323`) | `{mapSize{x,y,z},region{x1,y1,x2,y2},tiles[]}` | R | ⚠️ **Listener thread with mixed safety.** Snapshots are fine (`:1129-1130`), but it calls `_terrainService.Size` `:1107`, `_mapIndexService.VerticalStride` `:1108`, `_mapIndexService.CellToIndex` `:1197,:1205`, `_soilContaminationService.SoilIsContaminated` `:1221`, `_soilMoistureService.SoilIsMoist` `:1222` — **none of these are `IThreadSafe*` types** (`:131,:133,:135,:136`). Only `_waterMap`/`_terrainMap` are (`:132,:134`) | `ITerrainService`, `MapIndexService`, `IThreadSafeWaterMap`, `IThreadSafeColumnTerrainMap`, `ISoilContaminationService`, `ISoilMoistureService` | [E5] `:1134`. Out-of-range coords are **clamped, not rejected** `:1113-1116`. All-zero bbox returns only `mapSize` `:1110-1111` |

## Table 2 — POST routes (35)

All rows are **[W]** (main-thread write job) unless noted. All use **[E1]/[E2]/[E3]**. Registration lines are in `TimberbotHttpServer.BuildPostRoutes`.

| Method · Path | Registration → handler body | Body fields read | Response shape | R/W | Notes / errors |
|---|---|---|---|---|---|
| `POST /api/speed` | `:511` → `TimberbotWrite.SetSpeed` `TimberbotWrite.cs:173` | `speed:int` | `{speed}` | W | `SpeedManager.ChangeSpeed` `:177`. Range 0-3 → `invalid_param` + `("got",speed)` `:175` |
| `POST /api/workhours` | `:527` → `SetWorkHours` `TimberbotWrite.cs:184` | `endHours:int` (default 18) | `{endHours}` | W | `WorkingHoursManager` `:188`. Range 1-24 `:186` |
| `POST /api/district/migrate` | `:528` → `MigratePopulation` `TimberbotWrite.cs:194` | `from,to:string`, `count:int` | `{…}` | W | `DistrictCenterRegistry.FinishedDistrictCenters` `:197` |
| `POST /api/building/pause` | `:512` → `PauseBuilding` `TimberbotWrite.cs:225` | `id:int`, `paused:bool` | `{id,name,paused}` `:240` | W | `PausableBuilding` `:231`. `not_found` `:228`, `invalid_type` `:233` |
| `POST /api/building/clutch` | `:513` → `SetClutch` `TimberbotWrite.cs:245` | `id`, `engaged:bool` (default `true`) | `{id,name,engaged}` `:259` | W | `Clutch.SetMode` `:255`. `not_found`/`invalid_type` `:248,:252` |
| `POST /api/building/floodgate` | `:514` → `SetFloodgateHeight` `TimberbotWrite.cs:261` | `id`, `height:float` | `{id,name,height}` | W | `not_found` `:265` |
| `POST /api/building/priority` | `:515` → `SetBuildingPriority` `TimberbotWrite.cs:283` | `id`, `priority:string` (def `"Normal"`), `type:string` | `{…}` | W | `BuilderPrioritySystem` |
| `POST /api/building/hauling` | `:516` → `SetHaulPriority` `TimberbotWrite.cs:319` | `id`, `prioritized:bool` (def `true`) | `{…}` | W | |
| `POST /api/building/recipe` | `:517` → `SetRecipe` `TimberbotWrite.cs:339` | `id`, `recipe:string` | `{…}` | W | `RecipeSpecService` `TimberbotWrite.cs:79` |
| `POST /api/building/farmhouse` | `:518` → `SetFarmhouseAction` `TimberbotWrite.cs:390` | `id`, `action:string` | `{…}` | W | `accept` branch `:544` |
| `POST /api/building/plantable` | `:519` → `SetPlantablePriority` `TimberbotWrite.cs:417` | `id`, `plantable:string` | `{…}` | W | |
| `POST /api/building/workers` | `:520` → `SetWorkers` `TimberbotWrite.cs:461` | `id`, `count:int` | `{…}` | W | |
| `POST /api/building/storage` | `:526` → `SetStorage` `TimberbotWrite.cs:517` | `id`, `good`, `mode` | `{…}` | W | |
| `POST /api/building/range` | `:523` → `CreateCollectBuildingRangeJob` `TimberbotWrite.cs:809` → `CollectBuildingRange` `:777` | `id` | `{…}` | **R via write queue** | Read-only data, but routed through the main-thread job queue |
| `POST /api/building/demolish` | `:531` → `TimberbotPlacement.DemolishBuilding` `TimberbotPlacement.cs:306` | `id` | `{id,name,demolished}` `:302` | W | `EntityService` |
| `POST /api/crop/demolish` | `:532` → `DemolishCrop` `TimberbotPlacement.cs:309` | `id` | `{id,name,demolished}` | W | `invalid_type: not a natural resource` `:313`; `not a crop` `:315` |
| `POST /api/building/place` | `:561` → `PlaceBuilding` `TimberbotPlacement.cs:2204`, serialised via `.ToJson(_service.Placement.Jw)` `:52` | `prefab,x,y,z,orientation` (def `"south"`) | `PlaceBuildingResult.ToJson` | W | `BlockObjectPlacerService` `TimberbotPlacement.cs:94`. Failure path `PlaceBuildingResult.Fail` `:49` / `WriteErrorJson` `:72` |
| `POST /api/placement/find` | `:548-560` → `CreateFindPlacementJob` `TimberbotPlacement.cs:401` → `FindPlacement` `:1921` | `prefab,x1,y1,x2,y2` **or** `x,y,radius` (radius defaults to `TimberbotService.DefaultSearchRadius`=30, `TimberbotService.cs:66`); also `format` | toon: flat array of ≤10 `{x,y,z,orientation,entranceX,entranceY,pathAccess,reachable,distance,nearPower,flooded,waterDepth}` `:1856-1873`; json: nested with `prefab` metadata `:1877` | R (multi-frame) | Bbox→centre+radius rewrite at `:555-558` only when all of x1/y1/x2/y2 are 0 |
| `POST /api/path/place` | `:547` → `CreateRoutePathJob` `TimberbotPlacement.cs:398` | `x1,y1,x2,y2,style`(def `"direct"`)`,sections,timings` | route result + optional `SnapshotMs/GraphMs/AstarMs` `:298-305` | W (multi-frame A*) | `EnsureBuildingsFreshNow` on main thread `:430`; 10-tile padding `:406` |
| `POST /api/planting/mark` | `:521` → `MarkPlanting` `TimberbotWrite.cs:564` | `x1,y1,x2,y2,z,crop` | `{…}` | W | `PlantingService` `TimberbotWrite.cs:81` |
| `POST /api/planting/find` | `:522` → `CreateFindPlantingSpotsJob` `TimberbotWrite.cs:654` → `FindPlantingSpots` `:621` | `crop`, `id` **or** `building_id`, `x1,y1,x2,y2,z` | `{…}` | R via write queue | Dual key `id`/`building_id` at `:522` |
| `POST /api/planting/clear` | `:524` → `UnmarkPlanting` `TimberbotWrite.cs:657` | `x1,y1,x2,y2,z` | `{…}` | W | |
| `POST /api/cutting/area` | `:525` → `MarkCuttingArea` `TimberbotWrite.cs:481` | `x1,y1,x2,y2,z,marked:bool` (def `true`) | `{…}` | W | |
| `POST /api/science/unlock` | `:529` → `UnlockBuilding` `TimberbotWrite.cs:699` | `building:string` | `{…}` | W | `ScienceService`, `BuildingUnlockingService`, `ToolUnlockingService` |
| `POST /api/distribution` | `:530` → `SetDistribution` `TimberbotWrite.cs:739` | `district,good,import`, `exportThreshold:int` (def **-1**) | `{…}` | W | |
| `POST /api/automation/link` | `:562` → `LinkAutomation` `TimberbotWrite.cs:1038` | `sourceId,targetId`, `input` (def `"a"`) | `{…}` | W | |
| `POST /api/automation/unlink` | `:563` → `UnlinkAutomation` `TimberbotWrite.cs:1118` | `id`, `input` (def `"a"`) | `{…}` | W | |
| `POST /api/automation/configure` | `:564` → `ConfigureAutomation` `TimberbotWrite.cs:1208` | `id,property,value` | `{…}` | W | |
| `POST /api/automation/rename` | `:565` → `RenameEntity` `TimberbotWrite.cs:1536` | `id,name` | `{…}` | W | ⚠️ Undocumented in `docs/api-reference.md` |
| `POST /api/agent/config` | `:573` → `TimberbotHttpServer.HandleAgentConfig` `:591` | `mode,goal` (presence-checked, not `??`-defaulted `:594-597`) | `ToStateResponseJson()` `:608` | W (state file) | `invalid_mode: must be 'autonomous' or 'request'` `:600`. Marks dirty `:606`. **No game service.** Runs on main thread only because it is registered as a write job |
| `POST /api/agent/request` | `:574` → `HandleAgentRequest` `:611` | `prompt:string` | `{pendingRequest:{id,prompt}}` `:623-625` | W | `invalid_prompt: prompt is required` `:615`. Fires `AgentState.Changed` → WS broadcast `TimberbotAgentState.cs:158` |
| `POST /api/agent/message` | `:575` → `HandleAgentMessage` `:628` | `message:string` | `{"ok":true}` `:634` — **the only `ok` envelope in the codebase** | W | `invalid_message: 'message' is required` `:632`. ⚠️ Undocumented in `docs/api-reference.md` |
| `POST /api/ready` | `:576` → `HandleReady` `:637` | `ready:bool` (presence-checked `:639`) | `{ready}` `:644` | W | `invalid_ready: 'ready' boolean is required` `:641`. Gate-exempt `TimberbotAgentState.cs:361` |
| `POST /api/debug` | `:533-541` → `TimberbotDebug.DebugInspect` `TimberbotDebug.cs:448` | `target` (def `"help"`) + **every** body property flattened to `Dictionary<string,string>` `:537-539`; sub-params `path,filter,depth,sample,method,arg0..,left,right,value,op,id` | varies per target; anonymous objects → Newtonsoft | R/W (reflection **can invoke arbitrary methods**, `target:call` `:512`) | Gated by `debugEndpointEnabled`; else `{"error":"disabled: debug endpoint"}` `:535`. `settleFrames=0` `:541`. Unknown target → `invalid_param: unknown target …` listing all 10 targets `:535`. Sub-router targets: `help,roots,get,fields,describe,call,compare,assert,validate,validate_all` (`:458-534`) |
| `POST /api/benchmark` | `:542-546` → `CreateBenchmarkJob` `TimberbotDebug.cs:55` → `RunBenchmark` `:58` | `iterations:int` (def 100) | `{test,iterations,totalMs,perCallMs,gc0,items,pass}` `:444` | R | `{"error":"disabled: benchmark endpoint"}` when debug off `:544` |

## Table 3 — WebSocket surface (`TimberbotWebSocketServer.cs`, port 8086)

| Direction · name | Registration → handler | Params / payload fields | Shape | R/W | Thread model | Errors |
|---|---|---|---|---|---|---|
| `GET /api/ws` (upgrade) | path check `:206` | header `Authorization: Bearer` `:227`, query `?token=` fallback `:230`, `Sec-WebSocket-{Key,Version}`, `Connection`, `Upgrade` `:213-216` | `101 Switching Protocols` `:242-246` | — | Accept thread `:150-162`, then per-connection `Task` `:160` | `400 malformed_request` `:187`; `405 method_not_allowed` `:195`; `404 not_found` `:208`; `426 upgrade_required` `:219`; `401 unauthorized` + `WWW-Authenticate` `:233` |
| **C→S** `heartbeat` | `ReceiveLoop` switch `:365` → `TimberbotAgentState.Heartbeat` `TimberbotAgentState.cs:204` | `payload.agent_status`, `payload.acked_request_id`, `payload.version` (parsed `TimberbotPure.cs:348-353`) | no direct reply; may trigger a `state` broadcast `TimberbotAgentState.cs:231` | W (container only) | Connection's receive `Task`; **lock-guarded container, never touches game state** — stated `TimberbotWebSocketServer.cs:23-25` | — |
| **C→S** `ping` | `:371` | none | replies `pong` `:372` | R | receive Task | — |
| **C→S** unknown type | `:374` | — | `{"type":"error","payload":{"error":"unknown_type: <t>"}}` `:375` | — | receive Task | — |
| **S→C** `state` | `OnAgentStateChanged` `:130` → `TimberbotPure.BuildStateMessage` `TimberbotPure.cs:264` | — | `{type:"state",payload:{mode,goal,ready,agentStatus,lastError,pendingRequest}}` | R | Raised on whichever thread mutated (main thread for write jobs); fan-out via bounded per-conn queue `:136-148` | Slow consumer → connection dropped `:141-146` |
| **S→C** `event` | `PushEvent` `:123` → `TimberbotPure.BuildEventMessage` `TimberbotPure.cs:277` | — | `{type:"event",payload:{event,day,timestamp,data}}` | R | Called from `[OnEvent]` handlers on the main thread (`TimberbotEvents.cs:55-68`) | Unparseable `dataJson` degrades to a JSON string `TimberbotPure.cs:284` |
| **S→C** `error` | `TimberbotPure.BuildErrorMessage` `:300` | — | `{type:"error",payload:{error}}` | — | — | Also emitted for `message_too_large: cap 65536 bytes` `:352-353` and `invalid_message` `:360` |
| **S→C** `pong` | `TimberbotPure.BuildPongMessage` `:310` | — | `{type:"pong",payload:{}}` | — | — | — |

## Routing mechanism

- **GET** — hand-written `switch` on the exact path in `RouteReadRequest` (`TimberbotHttpServer.cs:424-470`); `/api/tiles` special-cased outside the switch (`:330-331`). Fall-through → `UnknownEndpoint()` `:472`.
- **POST** — `Dictionary<string, PostRouteDescriptor>` built once (`:89`, `:504-583`, `StringComparer.Ordinal` `:579`), each holding a `Func<PendingRequest, ITimberbotWriteJob>` factory (`:58`).
- **No prefix/parameterised matching.** Path normalised at `:242` (`TrimEnd('/').ToLowerInvariant()`); ids travel in query/body.
- **Method handling is inconsistent**: `/api/ping` `:287` and `/api/settlement` `:292` match before any method check.
- **Auth** `:259-273` (constant-time compare via `TimberbotPure.BearerTokenMatches`), WS auth separately at `TimberbotWebSocketServer.cs:225-238`. Startup guard `TimberbotService.cs:126-133`.
- **Ready gate** `:279-285`, whitelist `TimberbotAgentState.cs:357-365`.
- **Body-size limit** `:356-367`, enforced before `JObject.Parse`; `maxBodyBytes <= 0` disables the cap (`:370-373`). WS caps: 8 KB handshake (`TimberbotWebSocketServer.cs:68`), 64 KB frames (`:307`).
- **Serialisation**: pre-serialised strings via `TimberbotJw` (`TimberbotJw.cs:37-189`); `Respond` falls back to Newtonsoft for non-strings (`:681`). Content-Type hardcoded `application/json` (`:683`). CORS on every response (`:699-704`).
- **TOON / non-JSON encodings — none in the mod.** Every "toon" occurrence is a `format` string selecting a flatter JSON shape (`TimberbotHttpServer.cs:308,387`; `TimberbotReadV2.cs:944,996,1233`; `TimberbotPlacement.cs:1854-1856`). No `Accept` negotiation. The header comment at `TimberbotHttpServer.cs:13-14` asserting a TOON path is wrong.

## Contradictions found by this sub-audit

- `openapi.yaml` ↔ code: **exact match** (26 GET + 35 POST; `openapi.yaml:882-2138`). `/api/ws` is absent from OpenAPI (covered by `docs/websocket-protocol.md`).
- Missing from `docs/api-reference.md`: `POST /api/agent/message` (`TimberbotHttpServer.cs:575`, `openapi.yaml:1000`) and `POST /api/automation/rename` (`TimberbotHttpServer.cs:565`, `openapi.yaml:2082`).
- `docs/api-reference.md:2076-2185` "Python CLI Helpers" headings (`map`, `find`, `place_path`, `launch`, `top`) are CLI verbs, not routes; `place_path` ↔ `POST /api/path/place`.
- `UnknownEndpoint()` list (`:483-501`) omits `/api/agent/state`, `/api/debug`, `/api/benchmark`.
- `docs/api-reference.md:1083-1102` types `entrance/seedling/dead/contaminated/moist` as `bool` and "optional"; code emits `int` 0/1, always (`TimberbotReadV2.cs:1226-1231,1247`).
- No 404 is ever emitted; unknown routes and domain errors return 200.

## Observation reads (spatial)

`GET /api/tiles` (`TimberbotReadV2.cs:1103-1261`) is the only per-cell terrain read. Per-cell keys: `x,y` (`:1224`), `terrain` = ceiling of topmost terrain column (`:1202`), `water` = depth of first water column with `Ceiling >= terrainHeight` (`:1213`), `badwater` = water-column contamination (`:1214`), `entrance/seedling/dead` (`:1227-1229`), `contaminated`/`moist` = soil at `(x,y,terrainHeight)` (`:1221-1222`), `occupants` string or `[{name,z}]` merging buildings (`:1137-1153`), natural resources (`:1155-1166`) and blockers (`:1168-1182`).

- Bounding box: **yes, required**; clamped not rejected (`:1113-1116`); all-zero bbox returns `{mapSize}` only (`:1110-1111`).
- Layered read: **no** — 2.5-D, topmost terrain column and first qualifying water column only (`:1198-1215`); vertical info survives only as `terrain` and per-occupant `z`.
- Delta / since-version: **none anywhere** (no `since`, ETag, `If-None-Match`; internal `PublishSequence` referenced only at `TimberbotDebug.cs:469`).
- Coordinates: **`z` is up** — `mapSize{x,y,z}` from `_terrainService.Size` (`:1107`), `Vector2Int(x,y)` → `CellToIndex` (`:1197`), `VerticalStride` stepping (`:1201,1209`), `Vector3Int(x,y,terrainHeight)` for soil (`:1221-1222`).

| Endpoint | Spatial payload | Region param | Anchor |
|---|---|---|---|
| `GET /api/buildings` | `x,y,z,orientation` per building (origin, not footprint) | Manhattan `x,y,radius` only (`TimberbotPure.PassesFilter`) | `TimberbotReadV2.cs:2978`, `:2965`; `TimberbotHttpServer.cs:317-319` |
| `GET /api/trees/crops/gatherables/beavers` | `x,y,z` per entity | Manhattan proximity only | `:3130`, `:3141`, `:3153`, `:3074` |
| `GET /api/tree_clusters/food_clusters` | fixed-grid centroids | none (`cellSize=10`, `top=5` hardcoded) | `:860-898`, `:899` |
| `POST /api/placement/find` | candidate cells with `flooded,waterDepth,reachable,…` | `x1..y2` or `x,y,radius` (default 30) | `TimberbotHttpServer.cs:548-559`; `TimberbotPlacement.cs:1856-1873` |
| `POST /api/path/place` | route cells + stop point | `x1..y2` (+10 padding) | `TimberbotHttpServer.cs:547`; `TimberbotPlacement.cs:406,298-305` |
| area writes (`planting/*`, `cutting/area`) | rectangle at one `z` | `x1,y1,x2,y2,z` | `TimberbotHttpServer.cs:521-525` |
