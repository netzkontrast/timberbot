# The Wardens cutscenes: design

> **Status:** design and first implementation (2026-09-06). The system lives in
> `wardens/src/WardensCutsceneScript.cs` (the scene format), `WardensCutscenes.cs` (the runner and its
> triggers) and `WardensCutsceneOverlay.cs` (letterbox, caption, buttons); the prototype scene is
> `wardens/src/Cutscenes/ColdBoot.json`, the three shots of the mockup in
> [`wardens-ui/ColdBoot.dc.html`](wardens-ui/ColdBoot.dc.html). It replaces the hardcoded orbit of
> `WardensColdBoot.cs` (retired). Nothing compiled here; every game API used is one the repo already
> calls somewhere (the inventory is in §11). What the first in-game run must answer is §12. The shot
> list and the camera interpolator come from [`wardens-chapter-1-plan.md`](wardens-chapter-1-plan.md)
> §5 and [`playtest-and-video-capture.md`](playtest-and-video-capture.md); the readings and cards the
> campaign will need are in [`wardens-campaign-story.md`](wardens-campaign-story.md) §0.

## 1. Overview

**Problem.** The story has moments the tutorial engine cannot stage: the Cold Boot (a paused flight
over the wasteland while three lines are read), the chapter openings, the level-05 reading (six
cards on the Core), the level-10 epilogue (nine cards over a slow orbit). Today the only cutscene is
a C# orbit with no text, no skip and no way to tune it without a rebuild, and every future scene
would be another C# class.

**Solution in one paragraph.** A cutscene is a JSON file in the mod's `Cutscenes/` folder: a list of
shots, each with a camera flight (keyframes relative to a named anchor and to the pose the camera
had when the scene began), one caption, and optionally a pointer, a toast and an Uplink line. One
runner plays them: it pauses and locks the game, draws a letterbox with the caption, flies the
camera through the existing `WardensCameraDirector`, and hands the game back. Scenes start on
triggers (a new game, a chapter opening, a tutorial finishing) or from the MCP `cutscene` tool, which
also reloads the files so a shot can be tuned with the game running. A static checker resolves every
name a scene uses (loc keys, triggers, anchors) before an in-game run, like `validate.py` does for
blueprints.

**Goals.**

- Scenes are data. Writing or tuning one never needs a rebuild: edit the JSON, `cutscene reload`,
  `cutscene play`.
- One runner for the Cold Boot, the chapter scenes and the campaign's readings.
- The player can always skip; the agent can always tell a scene is playing and stays out of the way.
- Every name a scene resolves at play time is checked statically.
- Behaviour that exists is kept: the Cold Boot still pauses, locks the speed, orbits the Core and
  leaves the game paused for the Clock card.

**Non-goals (this design).** Choice cards with two answers (the level-05 question, the level-end
cards; §8); hiding the game's UI during a scene (no verified API; the letterbox covers the top and
bottom bars, the rest stays); material swaps such as the Core's light coming on; an Esc key binding
(the input API is unverified; the Skip button and the MCP `skip` action do the job); persisting
"played" across saves (§3.4 explains why v1 does not need it); sound.

## 2. Architecture

```
┌─ Game scene ──────────────────────────────────────────────────────────────────────────┐
│  WardensCutscenes            ILoadableSingleton, IUpdatableSingleton                    │
│    Load(): read <mod>/Cutscenes/*.json  -> WardensCutsceneScript.Parse                  │
│    triggers: NewGameInitializedEvent | WardensChapterService.ChapterOpened |            │
│              TutorialService._finishedTutorials (polled)                                 │
│    Play(id) / Skip() / Continue() / Reload() / State()                                  │
│    per shot: resolve anchors -> WardensCameraDirector.Fly, WardensPointer.Point,          │
│              QuickNotificationService, WardensChat.SystemSays, overlay caption           │
│                                                                                          │
│  WardensCutsceneOverlay      ILoadableSingleton (UI Toolkit, UILayout.AddAbsoluteItem)   │
│    letterbox bars, caption, step dots, Skip, Continue                                    │
│                                                                                          │
│  WardensFrames               observes Playing -> events cutscene.start/end, `cutscene`   │
│  WardensMcpTools             `cutscene` tool; wardens_status.cutscene                    │
│  WardensChapterService       + ChapterOpened event (raised when a chapter is announced)  │
└──────────────────────────────────────────────────────────────────────────────────────────┘
```

Dependencies point one way: chapters, director, pointer, chat and the overlay know nothing about
the runner; the runner knows them; frames and the tool table know the runner. `WardensColdBoot`
is gone: its trigger policy (Wardens faction, tutorial on, new game only) is the runner's trigger
policy, and its orbit is the first scene.

## 3. Data design

### 3.1 The scene file (`Cutscenes/<Id>.json`)

```json
{
  "id": "ColdBoot",
  "on": ["new_game"],
  "pause": true,
  "leave_paused": true,
  "restore_camera": false,
  "letterbox": true,
  "skippable": true,
  "say": "Cold Boot. Orbiting the Core; the game is paused. Read the cards on the right.",
  "shots": [
    {
      "id": "orbit",
      "caption": "Wardens.Cutscene.ColdBoot.Orbit",
      "seconds": 8,
      "camera": [
        { "t": 1.5, "anchor": "core", "v": 60, "dzoom": 0.15, "dh": 20 },
        { "t": 8,   "anchor": "core", "v": 60, "dzoom": 0.15, "dh": 120 }
      ]
    }
  ]
}
```

| Field | Type | Default | Meaning |
|---|---|---|---|
| `id` | string | required | the scene's name; must equal the file stem (the checker enforces it) |
| `on` | string[] | `[]` | triggers (§3.3); empty means the scene plays only through the MCP tool |
| `pause` | bool | `true` | pause and lock the speed while the scene plays |
| `leave_paused` | bool | `false` | keep the game paused afterwards; `false` restores the speed from before the scene |
| `restore_camera` | bool | `false` | fly back (1.5 s) to the pose from before the scene when it ends |
| `letterbox` | bool | `true` | draw the black bars |
| `skippable` | bool | `true` | show the Skip button (the MCP `skip` action always works) |
| `say` | string | none | one Uplink line (system) when the scene starts |
| `shots` | shot[] | required, at least one | in order |

### 3.2 A shot

| Field | Type | Default | Meaning |
|---|---|---|---|
| `id` | string | the shot's index | for `status` and the log |
| `caption` | string | none | a loc key (`Localizations/enUS.csv`), checked statically; resolved through `ILoc` at play time, the key itself as the fallback |
| `text` | string | none | a literal caption for prototyping; `caption` wins when both are set |
| `seconds` | number | the last keyframe's `t`, else 0 | the shot lasts at least this long (unscaled seconds, so a paused game counts) |
| `wait` | `time` \| `continue` | `time` | `continue` shows a Continue button and holds the shot after the flight and `seconds` until it is pressed (or the MCP `continue` action) |
| `camera` | keyframe[] | `[]` | the flight; empty leaves the camera where it is |
| `point` | pointer | none | a `WardensPointer` marker when the shot starts: `anchor` (`core`, `grid`, `selection`; the ones with grid coordinates), `x`/`y`/`z`, `offset`, `message`, `seconds` (default the shot's), `color` |
| `toast` | string | none | a quick notification when the shot starts |
| `say` | string | none | an Uplink line (system) when the shot starts |

A shot ends when its flight has finished, its `seconds` have passed, and, with `wait: continue`, the
button was pressed. The scene ends after its last shot, or on Skip.

### 3.3 A keyframe

| Field | Meaning |
|---|---|
| `t` | seconds from the shot's start (default 0); keyframes are sorted by `t`. When the first keyframe's `t` is above 0 the director inserts the current pose at 0, so the flight eases out of wherever the camera is; a keyframe at `t: 0` is a cut. |
| `anchor` | what the camera looks at: `core` (the district center), `start` (the target the camera had when the scene began; the default), `selection` (what the player has selected, falling back to `start`), `bot` (the first Warden, falling back to `start`), `grid` (`x`, `y`, `z` = height, as in every Timberbot endpoint), `world` (`x`, `y`, `z` in world space) |
| `offset` | `[dx, dy, dz]` in grid units added to the anchor (`dz` is height) |
| `h`, `v`, `zoom` | absolute pose values: degrees, degrees, `CameraService.ZoomLevel` units |
| `dh`, `dv`, `dzoom` | the same, relative to the pose the camera had when the scene began; an absolute value wins over a relative one for the same axis |

A pose field that is neither absolute nor relative keeps the value from the scene's start, so a
keyframe with only `dh` orbits without changing the tilt or the zoom. The units are the director's:
world space for the target, degrees, `ZoomLevel`; the zoom scale is the one number nobody has
measured yet (§12), which is why the prototype uses small relative `dzoom` values that are harmless
whatever the scale turns out to be.

### 3.4 Triggers

| `on` entry | Fires when | Once? |
|---|---|---|
| `new_game` | `NewGameInitializedEvent` (a new game; loaded saves never post it) | by construction |
| `chapter:<Id>` | `WardensChapterService` announces the chapter (its `ChapterOpened` event: a real opening or a forced one through `chapter unlock`, never the silent reconcile after a load) | once per chapter per game |
| `tutorial:<TutorialId>` | the id appears in `TutorialService`'s finished set, polled twice a second; the first poll after a load is silent | once per tutorial per save |

Policy for every trigger, the same one the Cold Boot had: the active faction is the Wardens, the
tutorial is on (the story is the tutorial), and `"cutscenes": true` in `settings.json` (the default;
`false` keeps the triggers off for playtest scripts that do not want a 22 s scene, the way
`chapterGating` works). The MCP `play` action ignores the policy: it is the tuning loop.

No save state. A trigger fires on an event, events happen once, and a loaded save re-fires none of
them: a chapter already open at load is reconciled silently, a finished tutorial is in the set
before the first poll, `new_game` never comes. A scene interrupted by a save and reload simply does
not resume, which is the right behaviour for a cutscene. Scenes that fire while another plays are
queued and start when it ends.

## 4. Component design

### 4.1 `WardensCutsceneScript` (the format)

Plain classes (`CutsceneScene`, `CutsceneShot`, `CutsceneKeyframe`, `CutscenePointer`) and one
`Parse(JObject)` that applies the defaults of §3 and throws `FormatException` naming the field on
anything malformed (a missing `id`, no shots, an unknown `wait`, a non-numeric keyframe field, an
unknown anchor). No game types: it is the one part that could be unit-tested outside the game, and
the Python checker implements the same rules.

### 4.2 `WardensCutscenes` (the runner)

- `Load()`: reads the settings, registers on the `EventBus`, subscribes to `ChapterOpened`, loads
  every `<mod>/Cutscenes/*.json`. A file that fails to parse is logged and listed under
  `State().errors`; it never stops the load. `<mod>` is the folder of `WardensSettings.Path`, the
  same resolution the `manual` tool uses for `docs/WARDEN.md`.
- `Play(id)`: stops a running scene (as a skip), records the start pose (`director.Current()`) and
  the speed, pauses and locks if `pause`, shows the overlay, says `say`, starts shot 0.
- `UpdateSingleton()`: polls finished tutorials (twice a second), drains the trigger queue when
  idle, and advances the shot when its end condition (§3.2) holds.
- A shot starts by resolving its keyframes against the anchors and the start pose, calling
  `director.Fly(frames, onFinished)`, placing the pointer, sending the toast, saying the line, and
  setting the caption and the step dots. The flight's completion is the callback, not a guess about
  the director's update order.
- `Skip()` ends the scene now; `Continue()` releases a `wait: continue` shot; `Reload()` re-reads the
  folder. `HasPlayed(id)` and `Playing` / `CurrentId` / `Summary()` / `State()` serve the tool table
  and the frames.
- Ending, in every path (last shot, skip, an exception mid-shot): stop the director, fly back if
  `restore_camera`, unlock the speed and restore it (or leave it paused), clear the pointers, hide
  the overlay, note the id as played. The unlock sits in a `finally`: a scene that throws must not
  leave the game locked at speed 0, which is the rule `WardensColdBoot` already followed.
- Everything runs on the main thread; the MCP tool is queued there like every other tool that
  touches game state, so no locks.

### 4.3 `WardensCutsceneOverlay` (the UI)

Built the way `WardensChat` and `TimberbotPanel` are built (UI Toolkit, `VisualElementInitializer`,
`UILayout.AddAbsoluteItem`; the panel's modal overlay is a zero-inset root added the same way, which is
the precedent for a full-screen element), after the mockup:

- a full-screen root with `pickingMode = Ignore`, hidden while idle and brought to the front when
  shown, so the game's own panels (the tutorial cards on the right) stay visible and clickable;
- two black bars, 12 % of the height each, top and bottom (`letterbox`);
- above the bottom bar, centered: a 48×2 cyan rule, the caption (26 px, wrapping, at most 760 px
  wide, the game's `text--centered` class), a row of step dots (filled for the shots played, an
  outline for the ones to come), and the Continue button when a shot waits for it;
- the Skip button at the top right, under the top bar, when the scene is skippable.

Only the buttons pick pointer events. The fonts and colours are the game's (`game-text-normal`,
the Wardens' cyan `#00E5FF`), the caption is white on the bar's black.

### 4.4 `WardensChapterService` (one event)

`public event Action<WardensChapter> ChapterOpened;`, raised in `Open()` under the same condition as
the toast (announced, and at least one building newly unlocked). Frames keep their own detection;
nothing else changes.

### 4.5 `WardensFrames` (two events, one field)

The frame carries `cutscene` (`playing`, `id`, `shot`, `shots`, `waiting`), notes
`cutscene.start:<id>` and `cutscene.end:<id>` as events, and, while a scene plays, puts
`{"what": "cutscene", "why": "a scene is playing: say nothing, leave the camera"}` first in
`attention`.

### 4.6 MCP `cutscene` tool (main thread)

| action | does | returns |
|---|---|---|
| `status` (default) | nothing | the runner's state: `playing`, `id`, `shot`, `caption`, `waiting` (`flight`, `time`, `continue`), `elapsed`, `played`, `queued`, `scenes` (id, triggers, shots, seconds), `errors`, `folder` |
| `list` | nothing | the same |
| `play` | plays `id` (default `ColdBoot`), replacing a running scene; ignores the trigger policy | the state |
| `skip` | ends the running scene | the state |
| `continue` | releases a `wait: continue` shot | the state |
| `reload` | re-reads `Cutscenes/*.json` | the state, with any parse errors |

`wardens_status` keeps `cutscene_played` (the Cold Boot has played this session) and gains a
`cutscene` block (the summary).

### 4.7 `tools/check_cutscenes.py` and `validate.py`

The checker reads a mod folder (or `wardens/src`) with no game files: every `Cutscenes/*.json`
parses, `id` equals the file stem and is unique, every `on` entry is `new_game`, `chapter:<Id>` with
an id from `WardensChapters.cs` (the table `validate.py` already parses) or `tutorial:<Id>` with a
tutorial the mod ships, every `caption` is a row of `Localizations/enUS*.csv`, every anchor is one
of §3.3, pointer anchors are the ones with grid coordinates, `wait` is `time` or `continue`, keyframe
fields are numbers, and a scene has at least one shot. `validate.py` calls it, so the one command
the playtest checklist already requires covers scenes too; the checker also runs alone and has a
pytest file next to it, which is the part of this design that is verified in this environment.

## 5. Interface contracts

| Contract | Owner | Checked by |
|---|---|---|
| A scene file's stem is its `id`; ids are unique | `Cutscenes/*.json` | `check_cutscenes.py` |
| Captions are `Wardens.Cutscene.<Scene>.<Shot>` rows in `enUS.csv`; both generators keep rows they do not own | `Localizations/enUS.csv` | `check_cutscenes.py` |
| Triggers name a chapter from the C# table or a tutorial the mod ships | `WardensChapters.cs`, `Tutorials/` | `check_cutscenes.py` |
| The parser and the checker apply the same rules | `WardensCutsceneScript.cs`, `check_cutscenes.py` | the pytest file (checker side); the smoke run (parser side) |
| Server `instructions`, `WARDEN.md` and `wardens-play.md` agree that a playing scene owns the camera and the Warden's silence | the three files | the three-way rule in `AGENTS.md` |

## 6. Flows

**New game.** `NewGameInitializedEvent` → bots replace the beavers (`WardensStartingPopulation`) →
the runner's handler finds the scenes bound to `new_game` (policy: Wardens, tutorial on, setting on)
→ `Play("ColdBoot")`: speed locked at 0, letterbox up, Uplink line, shot 1 eases into a high orbit
around the Core (8 s, "Nothing has grown here in 3,000 days."), shot 2 pushes in and keeps orbiting
(8 s, "We were not built to live here. We were built so that others could."), shot 3 settles back on
the start pose with the orbit completed (6 s, "Chapter 1: First Light. Keep the machines charged.
Find metal.") → overlay down, speed unlocked, the game stays paused (`leave_paused`) for the Clock
card, exactly where the old orbit left it. The tutorial's Wake and Directive cards are on the right
throughout, as before.

**Skip.** The player clicks Skip (or the agent calls `cutscene skip`): the flight stops where it is,
the overlay goes, the speed rule applies as at a normal end. `cutscene_played` is true either way.

**A chapter scene (none shipped yet).** `chapter:Badwater` → the chapter service opens the chapter,
posts its toast and raises `ChapterOpened` → the runner plays the scene (or queues it behind a
running one). With `restore_camera: true` the player's view comes back when it ends; the Warden,
which used to fly the camera at a chapter transition, sees `cutscene.start` in its frame and does
nothing.

**Tuning.** Edit `Documents/Timberborn/Mods/Wardens/Cutscenes/ColdBoot.json` with the game running →
`cutscene reload` → `cutscene play` → adjust → copy the file back to `wardens/src/Cutscenes/` →
`python wardens/tools/check_cutscenes.py wardens/src`.

**Reload of a save.** Nothing plays: no `new_game`, the chapter reconcile is silent, the finished set
is read once before it is compared.

## 7. Failure handling

| Failure | Behaviour |
|---|---|
| `Cutscenes/` missing or empty | one log line; `State().scenes` empty; the Cold Boot does not play and the log says why |
| A scene file does not parse | logged with the field path; listed in `errors`; other scenes load |
| A caption key is missing from the loc table | the caption shows the key (the checker catches it before that) |
| The district center cannot be found (`core`) | the anchor falls back to `start` |
| The director or a game call throws mid-shot | the scene ends as a skip; the speed is unlocked in `finally` |
| `play` with an unknown id | the tool returns an error naming the loaded ids |
| Two triggers in one frame | queued in order |
| A scene starts while the human is mid-flight with the `camera` tool | the runner's `Play` stops the director first |

## 8. Extensions (not in v1)

- **Choice cards:** a shot with `choices: [{"id", "text"}]` and a `chosen` result the campaign
  service stores in `campaign.json` (the level-05 question, the level-end *Continue* / *Stay*).
- **Readings:** already expressible (one shot per line, `wait: continue`, a slow orbit); the campaign
  adds the `level:<Id>` triggers and text with placeholders filled from the Ledger.
- **Esc to skip** once an input binding the repo has used exists; **hide the UI** once the service
  that does it is verified; **material swaps** (the Core's light) as a shot action.
- **Persisted "played"** through `ISaveableSingleton` if a scene ever needs to survive a reload,
  which none of the planned ones do.
- **Camera bookmarks** as anchors (`anchor: "bookmark:<name>"`), shared with the agent's `camera` tool.

## 9. Testing

- **Static (no game):** `python wardens/tools/check_cutscenes.py wardens/src` prints `problems: none`;
  `uv run --project python --extra dev pytest wardens/tools/test_check_cutscenes.py` covers a good
  scene and every rule with a bad one; `validate.py` includes the checker.
- **Smoke (with the game), added to `PLAYTEST.md`:** (1) a new Wardens game shows the letterbox, the
  three captions and the dots, the camera orbits the Core once and settles where it started, the
  game is paused afterwards and the tutorial cards are clickable throughout; (2) Skip at any point
  leaves the game paused and unlocked (the speed buttons work); (3) `cutscene status` shows `playing`,
  `shot`, `waiting`; `cutscene play` replays it on a loaded save; `cutscene reload` after an edit in
  the mod folder changes the next replay; (4) `wardens_status.cutscene_played` true after the scene,
  the `frame` events carry `cutscene.start:ColdBoot` and `cutscene.end:ColdBoot`; (5) with the
  tutorial off, or `"cutscenes": false`, nothing plays on a new game and `play` still works.
- **MCP:** `mcp_smoke.py` expects the `cutscene` tool in the list.

## 10. Rollout

1. This change: the format, the runner, the overlay, the Cold Boot as data, the checker, the docs.
   Behaviour change for the player: three captions and a Skip button on the Cold Boot, 22 s instead
   of 14.
2. After the smoke run: fix the zoom scale and the angles in `ColdBoot.json`; decide whether the
   Wake and Directive cards move into the scene (§12).
3. Chapter scenes, one per chapter, when the campaign's cards for them are written (the story doc
   has the lines); each is one JSON file and its loc rows.
4. Choice cards and the level triggers with the campaign service (§8).

## 11. Game APIs used (all already called elsewhere in the repo)

`EventBus.Register` + `[OnEvent] NewGameInitializedEvent` (`WardensStartingPopulation`),
`FactionService.Current.Id`, `TutorialSettings.DisableTutorial`, `TutorialService._finishedTutorials`
(`WardensChapters`), `SpeedManager.ChangeAndLockSpeed / UnlockSpeed / ChangeSpeed / CurrentSpeed`
(`WardensColdBoot`, `WardensMcpTools`), `DistrictCenterRegistry.AllDistrictCenters` +
`BlockObject.Coordinates` + `CoordinateSystem.GridToWorldCentered` (`WardensColdBoot`),
`EntitySelectionService.IsAnythingSelected / SelectedObject` + `Transform.position`
(`WardensMcpTools`), `CharacterPopulation.Characters` + `Bot` (`WardensFrames`), `ILoc.T`
(`WardensChapters`), `QuickNotificationService.SendNotification` (`WardensPointer`),
`UILayout.AddAbsoluteItem`, `VisualElementInitializer.InitializeVisualElement`, `NineSliceButton`,
`Label`, `VisualElement.BringToFront`, `PickingMode.Ignore` (`WardensChat`, `TimberbotPanel`),
`WardensCameraDirector.Fly / Stop / Apply / Current / IsFlying`, `WardensPointer.Point / Clear`,
`WardensChat.SystemSays`. The one style not used before is `Length.Percent` for the bar heights
(UI Toolkit core, `UnityEngine.UIElementsModule`); `unityTextAlign` was deliberately avoided because
`TextAnchor` lives in a Unity module no csproj here references.

## 12. Open questions (the smoke run answers them)

- The zoom scale: what `ZoomLevel` range the game uses, and therefore what `dzoom` values a "high
  shot" and a "push in" need. The prototype's ±0.15 is a placeholder that cannot hurt.
- Whether the letterbox and the tutorial panel collide on screen, and whether the Wake and Directive
  text should move into the scene (the Cold Boot tutorial would keep one empty stage so the Basics
  chain still starts from it).
- Whether `text--centered` exists in the game's stylesheets (harmless if not: the caption is then
  left-aligned inside its box).
- Whether a `NewGameInitializedEvent` handler may start a camera flight in the same frame the
  event is posted, or the first shot needs one frame of delay (the old orbit did it in the handler).

## 13. Decisions

1. **Scenes are data, in the mod folder, not blueprints.** Tuning with the game running and a static
   checker matter more than blueprint merging; `settings.json` is the precedent for a plain JSON
   file the mod reads itself. Rejected: `ComponentSpec` records (the discovery API for a new
   top-level spec is unverified), C# scene classes (a rebuild per tweak).
2. **The Cold Boot is the prototype and the old class goes.** One implementation of "cutscene", and
   the mockup already specified the three shots. Rejected: keeping the orbit in C# next to the
   runner (two cutscene systems), a second sample scene (story text nobody has written yet).
3. **Keyframes are relative to the start pose and to anchors.** A scene must work on any map and
   from any camera the player left; absolute poses are allowed for the shots that need them.
4. **Skip is a button; Esc waits.** No input API in the repo has been compiled against; a button is
   proven UI. The MCP `skip` action gives scripts the same power.
5. **The letterbox is drawn over the UI, not instead of it.** No verified way to hide the UI, and the
   tutorial cards must stay clickable; `pickingMode = Ignore` on everything but the buttons.
6. **No save state.** Every trigger is an event that a loaded save does not re-post; the first poll
   is silent. A scene that needs to survive a reload is a §8 problem.
7. **The trigger policy is the Cold Boot's.** Wardens only, tutorial on, setting on; `play` bypasses
   it because that is the tuning loop.
