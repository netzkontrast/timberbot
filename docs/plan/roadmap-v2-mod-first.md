# Roadmap v2 — mod-first

| | |
|---|---|
| Status | Adopted 2026-09-02 ("Finish Mod" directive). Supersedes the kickoff phase plan (§6–§10) where they differ. **Progress:** slice 3c (in-mod MCP endpoint, read tools) and the MRTR part of 3d are implemented in `TimberbotMcp.cs`/`TimberbotHttpServer.cs` with 39 xUnit tests; the error contract (3a) is applied on the MCP surface via `TimberbotErrors.cs`; the plain-text map view (part of 3b) exists as `TimberbotMapText.cs`; the `TimberbotJw` cross-thread race (G-MOD-3) is fixed; `get_prefabs` no longer runs on the listener thread. **Not yet verified against the game**: the mod DLL has not been compiled against the Timberborn assemblies or exercised with MCP Inspector (Phase 1 spike S3) because this environment has no game install. |
| Inputs | Kickoff prompt · `docs/audit/00-repo-audit.md` (Phase 0) · research report "Architektur und Implementierung eines autonomen LLM-Agenten … via MCP (2026-07-28)" (report 1) · research report "Technische Modding-Architektur, Ökosystem-Analyse und Claude-Code-Skill für Timberborn 1.0 und 1.1" (report 2) · primary-source checks made on 2026-09-02 (MCP changelog 2026-07-28, NuGet `ModelContextProtocol.Core`) |
| Rule | Code and primary sources win over both reports; disagreements are logged in `docs/audit/contradictions.md` R1–R10. Identifiers carry a verification tag: **[code]** seen in this repo, **[spec]** read in the MCP changelog, **[nuget]** read on nuget.org, **[UNVERIFIED]** needs the game DLLs (Phase 1). |

## 0. What changed and why

The new direction is: **the C# mod is the product, and the MCP endpoint lives in it.** The Python layer stops being the integration point and becomes test tooling. Three verified facts make this the right order:

| Kickoff / report assumption | Verified reality | Consequence |
|---|---|---|
| MCP layer is "greenfield", built as a Python/TypeScript sidecar (kickoff §0, ADR-001) | The mod already runs an `HttpListener` with routing, auth, body caps and a main-thread write-job queue (`TimberbotHttpServer.cs:83-129,140-224` [code]). `ModelContextProtocol.Core` 2.2.0 (2026-08-13) targets **netstandard2.0** [nuget]; `ModelContextProtocol.AspNetCore` does not. | The stateless Streamable-HTTP endpoint is added **inside the mod** on the existing listener. Sidecar becomes the fallback, not the plan. |
| Report 1, A3: embed `ModelContextProtocol.AspNetCore` in the Unity process | ASP.NET Core / Kestrel cannot load into Unity Mono (netstandard2.1 profile, `Timberbot.csproj:4` [code]). Only the `Core` package is netstandard-compatible, with 9 transitive packages (`System.Text.Json` 10.x, `Microsoft.Extensions.AI.Abstractions`, `System.Net.ServerSentEvents`, …) [nuget]. | ADR-001 becomes a three-way choice (§2). Phase 1 gets a spike that decides it with evidence, not opinion. |
| Report 1, Phases 1 and 3: build a "Proof of Infrastructure" HTTP listener on port 3001 and an action queue | Both exist and are tested in the fork (`TimberbotHttpServer.cs:226-341`; `ITimberbotWriteJob.cs`; `TimberbotService.cs:357-365` [code]). | Skipped. Effort goes to the three things that do **not** exist: semantic observation layer, in-mod MCP endpoint, tick control. |
| Report 1, A6: "Timberbot uses BepInEx / DLL injection / UI automation — ignore" | The fork is a native Bindito mod (`TimberbotConfigurator.cs:10-11` [code]); BepInEx is a build-time publicizer only; no Harmony; the in-game panel is a Launch/Stop control surface, not UI automation. | The fork stays the base (kickoff §3.6 additive rule holds). |
| Kickoff §1 MCP facts | Confirmed and sharpened by the changelog [spec]: `initialize` and `Mcp-Session-Id` removed; `_meta` keys `io.modelcontextprotocol/protocolVersion`, `clientCapabilities`, `clientInfo`, `serverInfo`; **`server/discover` MUST be implemented**; `ping` removed; every result carries `resultType` (`complete` / `input_required`); MRTR via `InputRequiredResult` with `inputRequests`, `inputResponses`, `requestState`; `Mcp-Method` and `Mcp-Name` headers **required** on POST; `ttlMs` and `cacheScope` **required** on list results; `subscriptions/listen` replaces the GET stream; HTTP+SSE deprecated; Roots/Sampling/Logging deprecated. | The in-mod endpoint surface is small and fully enumerable (§3, slice 3c). No handshake state to keep. |
| Report 2: Bindito entry via `IModStarter` + `IConfigurator`/`IContainerDefinition` | The fork uses the `Configurator` base class with `Bind<T>().AsSingleton()` and has **no** `IModStarter` (grep: zero hits) [code]. Mod-folder paths are hard-coded to `Documents/Timberborn/Mods/Timberbot` (`TimberbotPaths.cs:12-25` [code]), which breaks for Workshop installs. | Phase 1 verifies both entry patterns against the DLLs; slice 3a adopts the mod-environment path if `IModStarter`/`IModEnvironment` exist [UNVERIFIED]. |
| Report 2: `.timber` map format (ZIP with `version.txt` + `world.json`, `Singletons.TerrainMap.Heights.Array`, row-major, Z up) | Consistent with the coordinate convention found in code (`TimberbotReadV2.cs:1107-1222` [code]); the format itself is [UNVERIFIED] here. | Adopted for the eval harness (slice 3f): procedural scenario maps give deterministic test worlds. Blueprints, AssetBundles and Timbermesh are **out of scope** (no assets, no in-game entities needed). |
| Report 1: writes must commit "at tick boundaries" (A5) | The fork commits under a per-frame budget in `UpdateSingleton`; `Timberborn.TickSystem` is referenced but unused (`Timberbot.csproj:73` [code]). | ADR-005 decides frame budget vs tick hook after Phase 1 spike S2. |

## 1. Target architecture

```
Timberborn.exe (Unity 6000.3.6f1, Mono, netstandard2.1 API)
└── Timberbot.dll  (Bindito [Context("Game")])
    ├── TimberbotHttpServer  (HttpListener, port 8085, listener thread)
    │     ├── /api/*   legacy REST (kept, frozen; used by tbot + integration tests)
    │     └── /mcp     stateless Streamable HTTP, JSON-RPC 2.0        ← NEW (slice 3c/3d)
    │            server/discover · tools/list · tools/call · resources/* · subscriptions/listen
    ├── Write-job queue → UpdateSingleton (frame budget) | ITickableSingleton [UNVERIFIED] (ADR-005)
    ├── ReadV2 snapshots → Observation v2: semantic summary, layered/bbox/delta tiles, handles ← NEW (3b)
    ├── Session recorder: tool call + tick + result → JSONL                                  ← NEW (3e)
    └── Error contract {ok,code,reason,hint,at}                                              ← NEW (3a)

Clients (all thin):  MCP Inspector · Claude Code / Claude Desktop (Streamable HTTP) · eval harness · tbot CLI (legacy)
```

Design invariants (unchanged from the kickoff, now enforced by the drift detector and tests): main thread is sacred; every identifier has provenance; additive over rewrite; localhost only; no machine paths in git.

## 2. Decisions (revised ADR list) — with the recommendation I will argue in Phase 2

| ADR | Question | Options | Recommendation and its named cost |
|---|---|---|---|
| ADR-001 MCP host | Where does the MCP server run and what implements the protocol? | **A** hand-rolled stateless JSON-RPC on the existing `HttpListener` (Newtonsoft, already shipped by the game) · **B** `ModelContextProtocol.Core` 2.2.0 embedded with a custom `ITransport` over `HttpListener` [nuget; loadability UNVERIFIED] · **C** Python/TS sidecar (kickoff default, now fallback) | **A**, unless spike S1 shows Core loads cleanly *and* saves real work. Cost of A: we own protocol conformance (mitigated by an Inspector-driven conformance test in CI); the surface is ~6 methods after the 2026-07-28 simplification. Cost of B: 10-package dependency tree inside Unity Mono, version pinning against game-shipped assemblies. |
| ADR-002 Observation encoding | Semantic vs raw; budgets; layering; delta; handles | measured: raw tiles 57 tok/cell, plain map 0.6–0.85 tok/cell, summary 0.7–1.3k | Semantic-first: `get_summary` ≤ 1.5k tokens on a mid-game colony; region reads return **handles** and a plain-text map (no ANSI) ≤ 3k tokens; raw tiles only via handle paging. Cost: a C# aggregation layer that must be kept honest by tests. |
| ADR-003 Error contract | Shape and status semantics | current: HTTP 200 + free text | `{ok:false, code, reason, hint, at:{x,y,z}}` + stable codes; MCP `isError` on tool results; REST keeps 200 for compatibility but adds the fields. Cost: touching every error site (≈60). |
| ADR-004 Tool set | 8–12 `timberborn_*` tools | 70 existing Python tools as the candidate pool | `get_summary`, `get_region` (handle), `get_buildings`, `find_placement`, `place_building`, `place_path`, `demolish`, `set_speed`, `set_workers`, `unlock_science`, `set_distribution`, `wait_ticks`; annotations honest; `additionalProperties:false`; `outputSchema` for reads. Cost: parity loss vs the 70-tool surface (automation wiring becomes a second discovery tier). |
| ADR-005 Tick, pause, determinism | Frame budget vs tick boundary; replay | `UpdateSingleton` (today) vs `ITickableSingleton` [UNVERIFIED] | Keep frame-budget commits for cheap writes; add a **tick-aligned barrier** (`wait_ticks`, `step`) on the tick hook if S2 confirms it; record `(tick, tool, args, result)` for replay. Cost: two commit paths to document. |
| ADR-006 Version drift | Detector, feature flags, matrix | Appendix B seeds | Detector in `make check` + CI; per-endpoint feature flags derived from detector output at load; compatibility matrix in README. Cost: CI needs the game DLLs (self-hosted runner, Q4). |
| ADR-007 Python layer status (new) | Keep, freeze, or delete `game_mcp/`, `connector/`, `user_api/` | — | **Freeze**: `tbot` CLI + integration runners stay as test tooling; `game_mcp/` marked legacy, not extended; removal decided after slice 3d proves the in-mod endpoint. Cost: two MCP surfaces for one release. |

## 3. Phase plan

### Phase 1 — environment, build, drift detector, spikes (gate: build green, detector passes, spikes answered)

| # | Deliverable | Notes |
|---|---|---|
| 1.1 | `scripts/locate-timberborn.ps1` (**done**) → you run it on the Windows machine and paste the JSON | Registry + `libraryfolders.vdf` + `appmanifest_1062090.acf`; prints Managed dir, game file version, mod dir, Workshop dir, .NET SDKs. `-AsProps` emits the props snippet. |
| 1.2 | Git-ignored `Directory.Build.props` + committed `Directory.Build.props.example`; csproj defaults kept as last resort | Kickoff §3.8. |
| 1.3 | Build green with minimal, documented changes; `scripts/deploy.ps1` twin of `deploy.sh` | Record output in `docs/audit/01-environment.md`. |
| 1.4 | Drift detector `scripts/verify-identifiers` (.NET console, `MetadataLoadContext` over `GameManagedDir`) seeded from Appendix B §1 **plus** report-2 identifiers: `IModStarter`, `IModEnvironment`, `IConfigurator`, `IContainerDefinition`, `Timberborn.ModSupport` [UNVERIFIED] and `ITickableSingleton` [UNVERIFIED] | Fails loudly; wired into `make check`. Doubles as the verification step for every `[UNVERIFIED]` tag in this document. |
| 1.5 | **Spike S1 — MCP protocol library in Unity Mono**: drop `ModelContextProtocol.Core` 2.2.0 + its netstandard2.0 dependency closure next to `Timberbot.dll`, load a save, log assembly-load results | Abort criterion (from report 1): `MissingMethodException` / `TypeLoadException` at load or first JSON serialisation → ADR-001 option A. Success: types load, `System.Text.Json` coexists with the game's assemblies. |
| 1.6 | **Spike S2 — tick hook**: does `Timberborn.TickSystem` expose an `ITickableSingleton`-style interface bound by Bindito, what is its cadence relative to `UpdateSingleton`, does it run while paused? | Answers ADR-005. Method: drift detector output + a 20-line probe singleton logging tick vs frame counters to `timberbot.log`. |
| 1.7 | **Spike S3 — Inspector handshake**: a stub `POST /mcp` answering only `server/discover` and `tools/list` (empty) with `_meta.serverInfo`, `ttlMs`, `cacheScope`, `resultType`; MCP Inspector must connect | Proves the transport shape before any tool exists. Also checks whether Claude Code accepts it. |
| 1.8 | `CLAUDE.md` + `.claude/skills/timberborn-modder/SKILL.md` (see §5) + root `.editorconfig` + `Makefile`/`justfile` | Note: `.gitignore` currently ignores `.claude/` entirely; the skill needs `!.claude/skills/` un-ignored. |
| 1.9 | Three cheap hazards fixed as separate PRs (audit G-MOD-2/3, C16): per-thread `TimberbotJw`, `summary`/`tiles`/`prefabs` reads moved onto snapshots, `NO_COLOR`/`isatty` in the CLI | Each with a unit test where the pure-test pattern allows it and a live smoke otherwise. Justified before Phase 2 because every later slice builds on the read path. |

### Phase 2 — ADRs and specs (gate: your approval)

ADR-001…007 as in §2, each with ≥ 2 real options, criteria, decision, costs. Specs (RFC 2119 + Gherkin, one per feature, explicit non-goals): `error-contract.md`, `observation-v2.md`, `mcp-endpoint.md`, `mcp-tools-read.md`, `mcp-tools-write-mrtr.md`, `tick-control.md`, `session-recording.md`, `eval-harness.md`. The `spec-skill` is used if the `agency` plugin connects; otherwise plain files.

### Phase 3 — vertical slices, mod-first (each PR-sized, tested, live-smoked, changelog entry, gate)

| Slice | Deliverable | Acceptance (Gherkin summary) | Size |
|---|---|---|---|
| **3a Mod hardening + error contract** | `TimberbotErrors` (codes, `hint`, `at`), applied to placement/unlock/id/enum paths; `IModStarter`-based mod path if available; prune unused references | Given an occupied tile, when `place_building` runs, then the result has `ok:false`, `code:"PLACEMENT_OCCUPIED"`, `at:{x,y,z}`, and a `hint` naming the occupant. REST responses keep their legacy keys. | M |
| **3b Observation v2 (C#)** | `SemanticSummary` (colony, per-district, water/food/wood days, alerts as sentences), `RegionHandle` reads (bbox, layered: terrain / water / soil / occupants selectable), delta since `PublishSequence`, plain-text map renderer moved into the mod (no ANSI), footprints | Given the mid-game fixture colony, when `get_summary` runs, then the payload is ≤ 1.5k tokens and a model answers "is there a drought and which food is scarcest?" correctly (report 1's success proof, adopted). Given a 40×40 region, then the map view is ≤ 3k tokens. | L |
| **3c In-mod MCP endpoint, read-only** | `POST /mcp` on the existing listener: `server/discover`, `tools/list` (deterministic order, `ttlMs`, `cacheScope:"private"`), `tools/call` for read tools, `resources/read` for handles, `Mcp-Method`/`Mcp-Name` validation, `_meta` protocol-version check → `UnsupportedProtocolVersionError`, `resultType` on every result, annotations `readOnlyHint:true` | Inspector lists the tools and calls `get_summary`; a conformance test replays canned JSON-RPC against the endpoint in CI (pure-C# test project, listener stubbed). | M |
| **3d Write tools + MRTR** | Write tools through the existing write-job queue; destructive tools (`demolish`, `unlock_science`, speed ≥ 3) return `InputRequiredResult` with `requestState` = mod-minted confirmation handle (TTL 60 s); retry with `inputResponses` executes | Given `demolish` without confirmation, then `resultType:"input_required"`; given the retry with the handle, then the building is demolished and the result is `complete`. | M |
| **3e Tick control + recording** | `wait_ticks`, `pause`, `step` as budgeted write jobs on the ADR-005 hook; recorder writes `(tick, tool, args, result_hash)` JSONL under the mod dir; `replay` tool re-issues a recording against a fixed save | Given a recording and the same save, when replayed, then result hashes match for read tools and placement ids match for writes. | M |
| **3f Agent loop + eval harness** | Thin client (Python or TS, ADR-001 decides) driving Reason→Act→Observe over `/mcp`; scenarios as procedural `.timber` maps (report 2 format, [UNVERIFIED] until loaded) + fixed saves; metrics (survival, population, wellbeing, tokens per day); first target "survive one drought" | Given the drought scenario, when the agent runs 3 seeds, then the harness reports survival and token cost per run, and a replay reproduces run 1. | L |

Python layer during Phase 3: `tbot` + integration runners are used as smoke/test tooling for every slice; `game_mcp/` is not extended; ADR-007 revisits deletion after 3d.

## 4. Observation semantic layer — what "semantic" means here

Report 1's central claim (LLMs fail on raw grids, FLE) matches the measurement: raw tiles are 57 tokens/cell. The layer therefore produces, in this order of preference:

1. **Sentences and small tables**: "District 1: water 3.2 days (critical), food 11 days, wood 40 days; 2 buildings unreachable; drought in 4 days." Derived entirely from existing snapshots (`CollectSummary` already computes most inputs, `TimberbotReadV2.cs:557-700`).
2. **Handles instead of dumps**: `get_region` returns `{handle, bbox, layers, cells, tokensEstimate}`; `resources/read` pages the payload; the model asks for a layer, not the world.
3. **Plain-text map** as the only spatial view, elevation as digits, water as `~` plus a depth legend, footprints as letters, north-up, ≤ 64×64 per call.
4. **Delta reads** keyed on the internal `PublishSequence` so a loop step costs tokens proportional to change, not to colony size.

Budgets are asserted by tests using a tokenizer in CI (`tiktoken` today; Claude's token counting endpoint when a key is configured).

## 5. Developer skill `timberborn-modder` — what goes in, with verification status

The skill from report 2 is adopted as a Phase 1 deliverable, but only rules that are verified or explicitly tagged go in. A rule the drift detector cannot confirm stays `[UNVERIFIED]` inside the skill text so a future agent does not invent from it.

| Rule (report 2) | Status | Anchor / action |
|---|---|---|
| Native pipeline only, no BepInEx runtime, no Harmony unless unavoidable | **[code]** | `AGENTS.md:37`, `Timberbot.csproj:28`, manifest without dependencies |
| Bindito `[Context("Game")]` / `[Context("MainMenu")]`, `Bind<T>().AsSingleton()` via `Configurator` base class | **[code]** | `TimberbotConfigurator.cs:10-29` |
| `IModStarter.StartMod(IModEnvironment)`, `IConfigurator.Configure(IContainerDefinition)` | **[UNVERIFIED]** | drift detector 1.4; adopt for mod path in 3a if present |
| Unity 6000.3.6f1, netstandard2.1 | **[code]** for the target (`Timberbot.csproj:4`), Unity version **[doc]** (`AGENTS.md:185`) | confirm via `Timberborn.exe` file version from the locator |
| Mod folder `Documents/Timberborn/Mods/<Id>/` with `manifest.json` (`Id`, `Version`, `MinimumGameVersion`, `RequiredMods`, `OptionalMods`) | **[code]** for the first four keys (`timberbot/src/manifest.json`); `RequiredMods`/`OptionalMods` **[UNVERIFIED]** | — |
| Main thread is sacred; writes via the job queue; reads via snapshots or `IThreadSafe*` services | **[code]** | `TimberbotService.cs:357-365`, `TimberbotReadV2.cs:50-56` |
| `.timber` = ZIP with `version.txt` + `world.json`; `Singletons.TerrainMap.Heights.Array` space-separated, row-major `Y*W+X`; X/Y horizontal, Z up | Z-up **[code]** (`TimberbotReadV2.cs:1107-1222`); file format **[UNVERIFIED]** | verify by unzipping a real save/map in Phase 1 before the generator is trusted |
| Blueprints `.blueprint.json`, `#append`/`#delete`, `.optional.json`, AssetBundles `_win.asset`/`_mac.asset`, Timbermesh | **[UNVERIFIED]**, **out of scope** for this project | kept as a pointer, not as a rule |
| Build/deploy/test/verify commands, mod-folder variable, gotchas from the audit (Jw races, off-thread reads, ANSI) | **[code]** | from Phase 1 environment doc |

## 6. Risk register (delta to the audit §10)

| Risk | Early indicator | Mitigation |
|---|---|---|
| Dependency conflicts of `ModelContextProtocol.Core`'s closure inside Unity Mono (`System.Text.Json` 10.x vs game assemblies) | `TypeLoadException` / `MissingMethodException` at mod load | Spike S1 decides before any code; default is option A (no new runtime dependencies) |
| Spec churn after 2026-07-28 (draft already moves `tasks` to an extension) | Inspector rejects the endpoint after an Inspector update | conformance test pinned to the 2026-07-28 schema; `server/discover` advertises exactly one version |
| Workshop installs break the hard-coded `Documents` path | `settings.json`/`state.json` written to the wrong folder or not at all | `IModEnvironment` path in 3a if available; otherwise resolve from the loaded assembly location |
| Two MCP surfaces (Python legacy, in-mod) confuse users | issues filed against the wrong one | ADR-007 freeze + README compatibility matrix |
| Tick hook runs while paused or not at all | S2 probe shows zero ticks at speed 0 | `wait_ticks` falls back to frame counting with an explicit `paused:true` result |

## 7. What I need from you

1. Run on the Windows machine and paste the output:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\locate-timberborn.ps1
   ```
   I cannot run it from this session: it is a Linux container without your file system (`uname`: Ubuntu 24.04, host `vm`, no `/mnt/c`, no Steam).
2. Answers to the audit's Q1–Q5, plus **Q6** (freeze vs delete the Python MCP layer, ADR-007) and **Q7** (accept option A as the ADR-001 default with S1 as the only thing that can overturn it).
3. "Weiter" for Phase 1 once 1 and 2 are in.
