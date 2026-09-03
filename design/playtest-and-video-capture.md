# Timberbot as a playtest and video-capture harness

> **Status:** requirements, checked against Timberborn 1.1.2.4 decompile (2026-09-03). Nothing implemented beyond the one-line faction-id fix in `/api/summary`.

Two jobs the Wardens work needs from Timberbot that it doesn't do yet: let an agent **playtest a faction mod end to end without a human at the keyboard**, and let it **record a planned camera move** ("Kamerafahrt") of what it built. This doc lists what each needs, what the game already provides, and what has to be added to the mod.

## 1. Playtesting a faction mod

### Already there

| Need | Endpoint |
|---|---|
| Which faction is running | `/api/summary.faction` — now any faction id (was hard-coded to Folktails/IronTeeth) |
| Bots vs beavers, needs per character | `/api/beavers?detail=full` → `isBot`, `needs[]`, `critical`, `unmet` |
| Contamination | `/api/tiles`, `tbot map` |
| Build / wire / configure | all write endpoints |
| Events (building finished, beaver born, drought) | WebSocket `/api/ws` |
| Boot into a save | `TimberbotAutoLoad` + `tbot launch --settlement --save` |

### Missing

1. **New game from the API.** `TimberbotAutoLoad` calls `ValidatingGameLoader.LoadGame()`; there is no "start a new game with faction X on map Y in mode Z". The game's own path is `NewGameConfiguration` → `GameSceneParameters` → scene load, all reachable from the `MainMenu` context. Add `autoload.json` fields `{ "newGame": { "faction": "Wardens", "map": "...", "gameMode": "Normal" } }` and a `tbot newgame` command. Without this, every playtest starts from a hand-made save.
2. **Screenshot endpoint.** `GET /api/screenshot?w=1280` returning PNG. Unity: `ScreenCapture.CaptureScreenshotAsTexture()` on the main thread via the existing write-job queue. This is also the first building block for video.
3. **Deterministic stepping.** `POST /api/step {"ticks": N}`: run N ticks then pause. `SpeedManager` already exposes pause/speed; stepping needs a tick-counting `ITickableSingleton` that re-pauses. Makes smoke tests reproducible.
4. **Need satisfiers in the read model.** Which building satisfies which need, so an agent can act on "Calibration unmet" instead of guessing. The data is in `NeedApplierSpec`-style specs on building blueprints.
5. **Mod list.** `/api/ping` should report loaded mod ids + versions so a test can assert both `abix-.Timberbot` and `Wardens` are active.

## 2. Recording a camera move

### What the game gives us

- **`CameraService`** (`Timberborn.CameraSystem`, publicized): `Target` (Vector3, via `MoveTargetTo`), `HorizontalAngle`, `VerticalAngle`, `ZoomLevel` (`SetZoomLevel`), `FreeMode`, `MoveCameraBy`, `Transform`, `ProjectionMatrix`. Everything a dolly/orbit/zoom needs is a settable property; no reflection required.
- **`Unity.Recorder.dll` ships with the game.** That is Unity's own frame-accurate MP4/PNG-sequence recorder. Whether it is initialised at runtime needs a spike; if it is, video capture is a settings object away instead of a screenshot loop.
- **Reference mods on disk:** *Camera Bookmarks* (save/jump camera poses — shows how to serialise a pose), *TimberLapse* (captures frames on a schedule and writes them to disk — the fallback capture path), *FPP Camera* (drives `CameraService` in free mode every frame).
- `SpeedManager` for game speed, and the UI layer can be hidden through `UILayoutSystem`.

### What Timberbot needs to add

| Feature | Endpoint / CLI | Notes |
|---|---|---|
| Read camera pose | `GET /api/camera` | target, angles, zoom, freeMode |
| Set camera pose | `POST /api/camera` | instant jump; the primitive everything else builds on |
| Keyframed move | `POST /api/camera/path {"keyframes":[{pose, t}], "easing":"smooth"}` | main-thread interpolation across ticks or wall-clock; posts `camera.path.done` on the WS |
| Frame capture | `GET /api/screenshot`, `POST /api/record/start|stop` | PNG sequence to a folder, or Unity Recorder if it works at runtime |
| UI hide | `POST /api/ui {"visible": false}` | clean footage |
| Shot list | `tbot shoot shots.yaml` | YAML: shots → keyframes + game speed + duration + capture; runs sequentially, writes frames + a manifest |

A shot list an agent could write after a playtest:

```yaml
settlement: Wardens
save: smoke
fps: 30
shots:
  - name: wasteland-establish
    ui: hidden
    speed: 1
    seconds: 8
    keyframes:
      - { target: [64, 12, 64], h: 35, v: 55, zoom: 0.9, t: 0 }
      - { target: [64, 12, 64], h: 80, v: 45, zoom: 0.6, t: 8 }
  - name: numbercruncher-push-in
    speed: 3
    seconds: 6
    keyframes:
      - { target: [71, 14, 58], h: 20, v: 40, zoom: 0.5, t: 0 }
      - { target: [71, 14, 58], h: 20, v: 30, zoom: 0.2, t: 6 }
```

### Build order

1. `/api/camera` GET + POST (one afternoon; unlocks bookmarks for the agent immediately).
2. `/api/screenshot` (also needed by playtesting).
3. Keyframe interpolation + `camera.path.done` event.
4. Capture: spike Unity Recorder at runtime; fall back to a TimberLapse-style PNG sequence and `ffmpeg` on the Python side.
5. `tbot shoot` on top of the above.

### Open questions

- Does `Unity.Recorder` initialise in a player build, or is it editor-only despite shipping? (Decides step 4.)
- Frame pacing: capture per rendered frame at fixed game speed, or per tick with the game paused between frames (slower, but deterministic)?
- Where do frames go on Linux/Proton — same `TBOT_DOCUMENTS_DIR` resolver as the mod folder?

## Status 2026-09-03

The camera part exists, but inside the Wardens mod's in-game MCP server rather than as Timberbot HTTP
routes: `camera` (get/set/fly keyframes `{x,y,z,h,v,zoom,t}`/stop) and `cutscene`, both backed by
`WardensCameraDirector` (unscaled time, smoothstep, works while paused). Still missing: `/api/record`
(Unity.Recorder at runtime), screenshots, new-game-from-API.
