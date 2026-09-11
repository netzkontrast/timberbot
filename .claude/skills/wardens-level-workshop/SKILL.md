---
name: wardens-level-workshop
description: How to work the Wardens campaign in this repo one level at a time — play a level to its end in the running Timberborn game AND develop it on the way (its tasks in Levels/<id>.tasks.json, its scenes in Cutscenes/*.json, its land in wardens/maps, its playtest record), plus the repo's hard-won working knowledge (build, deploy, launch a level, read the log, the shell traps). Use it whenever a session is about a campaign level rather than one file — "play level 02 and fix what breaks", "make level 03", "add a scene for the Creek task", "the gorge task passes too early", "playtest the new opening", "continue the Wardens work", "what is missing for level 05" — and whenever you are about to build, deploy or launch the Wardens mod, even if the request names only a task, a cutscene, a map or a camera path.
---

# The level workshop

A Wardens level is four things that have to agree: **the land** (a `.timber` map built by mapsmith),
**the tasks** (what the player is asked to do, checked against the game), **the scenes** (the opening
and a short beat per task), and **the record** (what a playtest saw). This skill is the loop that keeps
them agreeing: play the level in the running game, notice where land, tasks and scenes disagree with
each other or with the story, fix the one that is wrong, and prove it by playing again.

Two other skills and one agent carry the specialist detail; this one decides when each is needed:

| Need | Use |
|---|---|
| Play a level as the Warden (boot, frame loop, the act list) | `warden-play` |
| Change or build land | `timberborn-mapsmith` |
| Report a state, write the HANDOVER entry, record a finding | `driving-iterations` |
| Frame and tune a scene against the game | the `cutscene-director` agent, or the loop in §4 yourself |

The repo's working knowledge — paths, commands, the traps that each cost a session — is in
[`references/repo-howto.md`](references/repo-howto.md). Read it once per session before the first
build or launch. The level's file formats and rules are in
[`references/level-anatomy.md`](references/level-anatomy.md); read it before editing a task file or a
scene.

## 1. Start of a session

1. `git fetch && git status -sb`. A local `main` many commits behind origin is normal here; fast-forward
   before reading anything, or you will plan against code that no longer exists.
2. Read the top entry of `docs/plan/HANDOVER.md` and the plan it names (`docs/plan/iteration-*.md`).
   The newest entry wins over the plan for anything it reports as shipped.
3. `python .claude/skills/wardens-level-workshop/scripts/level_status.py` — every level in the
   campaign table with its map, spec, tasks, scenes and the tasks that still have no scene. This is
   the map of the work.
4. Run the offline checks (repo-howto §Checks). Green before you change anything, so a red after is yours.
5. Is the game machine available? `tasklist | findstr Timberborn` (or ask). Everything C# waits on it.

## 2. The level loop

Work one level at a time, and inside it one disagreement at a time.

**Play.** Launch the level (repo-howto §Launch: the handoff file starts it as a new game) and play it
through the Wardens MCP server with the `warden-play` plan. A session that develops rather than plays
takes the `warden_director` prompt first (`prompts/get`); it lists every scene, every task without one,
and the camera pose as a paste-ready keyframe. Watch four things as you go: does each task's scene
play when the task goes live and show what the task asks; can each task be done on this land with
the buildings on the bar; does a check pass only when the thing the task is about is true (not the
cheapest wall one tile short); does the opening plus the first beat tell the player what to do.

**Find.** Write each disagreement as a finding in the four slots (`driving-iterations`): symptom with
the log line or tiles, source (read or reproduced), consequence, remedy. Decide which of the four
parts is wrong: a check that passes early is the task file; a site that cannot be reached is the land;
a beat that shows the wrong place is the scene; a claim in a caption the game contradicts is the text.

**Develop.** Fix the part that is wrong, at its source: land in the mapsmith spec (never the `.timber`),
tasks in `Levels/<id>.tasks.json`, scenes in `wardens/src/Cutscenes/`, captions in
`Localizations/enUS.csv`, behaviour in C#. Keep the rules of level-anatomy: at most two tasks live at
once, an opening of at most five shots, a task beat of at most two shots.

**Verify.** Offline checks, then — for anything C# or a new loc row — a build with the game closed and
a fresh launch. A scene edit or a literal caption reloads live (`cutscene action=reload`). Replay the
stretch of the level the fix is about, not the whole level.

**Record.** A row in `wardens/playtest/PLAYTEST.md` under a dated section, the state line, and at the
end of the session a HANDOVER entry. Commit and push before the entry (the entry quotes `git ls-remote`).

Stop a level when every task can be done on its land, every task with land to show has its beat, the
level completes into its end scene and card, and a fresh start shows none of the findings you fixed.

## 3. A new level

Order matters, because each step checks the one before it:

1. **Brief.** The level's row in `WardensCampaign.cs` (`Levels`) and in `wardens/maps/levels.toml`;
   the land's contract from `design/wardens-campaign-map-set.md` §2; the story beat from
   `design/wardens-campaign-arc.md` and `-story.md`. If the design and the author's latest wish
   disagree, ask (one question, an option table).
2. **Land.** A spec with a `[contract]` that states what the level's decision needs (a single gorge,
   an island that cannot be walked to). `mapsmith check --level <id>` green, the walk report read.
3. **Tasks.** `Levels/<id>.tasks.json`: the decision the land sets up, as checks that are only true
   when the decision was made. Graph with `after`; at most two live.
4. **Sites.** Every task that places something has a place on this land that a Warden can reach on
   foot or by named Stairs; write the coordinates you will use into the act list.
5. **Scenes.** The opening, then one beat per task with land to show, then `L<id>.End`. Frame them in
   the game (§4). Captions in the Warden's voice with numbers read from the built map.
6. **Ship.** `shipped: true` in the C# row, the `.timber` in `wardens/src/Maps/`, `validate.py` green,
   a build, a fresh launch, a playtest row. Add the level's act list to the `warden-play` skill.

## 4. Scenes and camera paths in the running game

The game reads scenes from `Documents/Timberborn/Mods/Wardens/Cutscenes/`, not from the repo. With
the MCP server up:

1. Frame the shot: `camera action=set x= y= z= h= v= zoom=`, or ask the human to frame it by hand.
2. `cutscene action=keyframe t=<second>` returns that pose as a keyframe; one per camera stop.
3. `cutscene action=write id=T02.Creek scene={...}` validates and saves the scene in the mod folder
   and reloads it. Literal `text` captions show at once; loc rows need a restart.
4. `cutscene action=play id=T02.Creek`; screenshot each shot with `scripts/shot.ps1` (it refuses unless
   Timberborn is the foreground window — the author chats beside the game, and a capture of their
   screen is theirs, not a frame). Judge each frame against its caption.
5. At most five passes per scene; the same problem twice is a finding, not a sixth pass.
6. Copy the file to `wardens/src/Cutscenes/`, turn `text` into `Wardens.Cutscene.<Id>.<Shot>` rows,
   `python wardens/tools/check_cutscenes.py wardens/src` → `problems: none`.

Zoom is exponential (`1.3^zoom * 32` tiles away): 3 shows most of a 96 map, 0 is 32 tiles, -5 fills
the frame with one Warden. 0.25 and 0.6 are the same shot.

## 5. What not to do

- Do not deploy while the game runs: it keeps the old DLL and reads the new blueprints, and the next
  load throws `No type found for key <Spec>`. Close the game fully, build, relaunch.
- Do not edit a `.timber`, a generated blueprint or a generated loc row by hand: the generator or the
  spec is the source, and the next run erases the edit.
- Do not call a C# change done because it compiled; a check that passes only in theory is `built`.
  `verified` is a row in PLAYTEST.md from the game on the map it is for.
- Do not decide the author's calls (a level's premise, a building's arrival, a balance number) from a
  plausible sentence: ask with an option table, and keep working on what does not depend on it.
- Do not answer a choice card or press the level-end Continue for the human.
