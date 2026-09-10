---
name: driving-iterations
description: Use when working a package of a Wardens iteration plan (a WP marked [cloud] or [game]), when about to tell the human or the HANDOVER that something is done, built, ready, verified or complete, when writing or reading a docs/plan/HANDOVER.md entry, when a check needs a machine this session does not have (dotnet, the game), when recording a playtest finding, when an open question stalls a package, or when touching WARDEN.md, warden-play SKILL.md, BuildInstructions, wardens-play.md or the warden_boot prompt.
---

# Driving an iteration across the cloud/game split

## Overview

Two machines work every Wardens iteration: a cloud container that can read everything and run the
Python checks but cannot compile `wardens/src` or load a map, and the game machine that can. Work
therefore changes hands through `docs/plan/HANDOVER.md`, and the failure that costs the most is a
claim of "done" that the receiving side then has to disprove.

**Core principle: built is not verified, and a claim is only as true as the check behind it. Every
status names the type of check and the machine that ran it.** A session that cannot run the check
says so, in those words, and hands over the command.

This skill distils what `netzkontrast/agency` learned driving remote agents ("COMPLETED ≠ done") and
the looper rubrics for bounded, verified loops. Rubrics, methods and templates are in `references/`.

## The state vocabulary

Use exactly these words for a package or a file. Each carries its evidence.

| State | Means | Evidence that must be pasted |
|---|---|---|
| `written` | The change exists on a branch. Nothing compiled it. | the diff, `git ls-remote origin <branch>` showing the commit |
| `checked` | The offline checks passed. | the last line of each: `pytest`, `mapsmith check`, `check_cutscenes.py` |
| `built` | The game machine compiled it. | the `dotnet build … -c Release` result line |
| `tested` | The unit tests ran where they can run. | the `dotnet test` summary line |
| `verified` | Seen in the running game, on the map it is for. | the row in `wardens/playtest/runs/<date>-level-01.md` |
| `blocked` | Cannot proceed without a named input. | the question or the option table, one per package |
| `deferred` | Deliberately not done. | the reason and where it is recorded |
| `moot` | No longer needed. | what made it unnecessary |

"Done", "ready", "complete", "ready to hand off" and "code complete" are not states. When one of them
is about to leave your keyboard, replace it with the row above that the evidence supports. A C# change
in `wardens/src` is at most `checked` in a cloud session, whatever its size.

## The package loop

Work one package at a time through five steps, and stop on a named outcome:

1. **Observe.** Read the plan section and the newest HANDOVER entry. Re-read current state before any consequential action; do not act on what you remember from an hour ago.
2. **Choose.** Shape the package against the goal rubric (`references/rubrics.md` §1): the concrete outcome, the artifact that proves it, what is out of scope. Type each acceptance criterion `programmatic`, `judge` or `human` (§2). If every criterion is `human`, the package belongs to the game machine.
3. **Act.** One bounded, reversible change on its own branch. A path outside the package's files is a scope change: stop and ask, do not widen quietly.
4. **Verify.** Run every `programmatic` criterion you can run here and keep the output line. What you cannot run is listed as not run, with the command, not omitted.
5. **Record.** The status line, then the HANDOVER entry. Only then say anything to the human.

Guards, set before step 3: a maximum number of attempts, and a no-progress rule (the same failure
line twice means stop and report, not a third variation). An error, an exhausted budget or a
blocked step is never reported as success. `references/rubrics.md` §3 has the full control rubric.

## The status line

The reply to "is it done?" has this shape and nothing before it:

```
WP1: checked (cloud). Programmatic: pytest wardens/tools → 95 passed; check_cutscenes → problems: none.
Not run here: dotnet build wardens/src/Wardens.csproj -c Release; dotnet test wardens/test (game machine).
Branch claude/wp1-loopback pushed; git ls-remote confirms 3f2a9c1. PR #14 open.
Blocked: none. Next on the game machine: build, run the test, paste both result lines into HANDOVER.
```

State, then evidence, then what was not run and where it runs, then the publish check, then the
blocker, then the next command. Risk is a named failure mode with its mitigation, or the sentence "no
risk found after a premortem" with the premortem's top cause beside it. "Low", "minimal" and "none
identified" are not risks. A reviewer's approval is not a check type; it does not move a state.

## The HANDOVER entry

Use the template at the bottom of `docs/plan/HANDOVER.md`, every section, in its order. Three
additions make the entry checkable across sessions (`references/templates.md` §2):

- **Evidence** is a table of commands and their literal result lines, for cloud and game entries alike.
- **Disposition** lists every "what you should do" item from the previous entry as `done`, `moot`, `deferred (reason)` or `blocked (question)` before new items are added.
- **Publish check**: the branch name and the `git ls-remote` line. Work that is not on origin is not handed over: push before writing the entry, and never list "push the branch" as a step for the next person.

The game-machine list is written as read X, run Y, paste Z, with paths and commands, because the
human at the game machine has your entry and nothing else.

## Findings and fixes

A playtest or review finding has four slots, all filled: **Symptom** (what was seen, with the log
line or the tiles), **Source** (the code or data that caused it, read or reproduced, say which),
**Consequence** (softlock, wrong economy, cosmetic), **Remedy** (the fix, or the decision recorded
and where). A finding without a Remedy is a note, not a finding. When nothing applies, write "no
findings", not a filler. Severity words: critical (breaks the run), warning (degrades the next
package), suggestion (fix when nearby).

The fixer is not the judge. A verdict on a proof run or on a fix is re-derived by someone who did not
write it, from the run record and the checker output, never from the agent's own `say` lines or the
author's reading of the diff.

## Open questions

One open question per package, asked as a blocking question, with an option table: option, cost,
risk, and one recommended row. A status message is not a question; the human reading "I assumed X"
three days later is the failure this rule prevents.

An unknown is not settled by a plausible sentence. If the code or the observation that would settle
it has not been read or seen in this session, the answer is `blocked (question)` and the option table
names what settles it ("read `TimberbotHttpServer.ListenLoop`; if it handles requests inline, the bug
exists there too"). "You know the code better, just decide" is an invitation to decide from evidence,
not to invent it.

## The five-document contract

The tool table's source of truth is the C# registration in `WardensMcpTools.cs`; `WARDEN.md` is the
authoritative prose; the other three mirror it. Each mirror gets `<!-- doc-source: … -->` and
`<!-- doc-hash: … -->` markers near its top (WP5 stamps them; until then the checker reports the
files as `UNMARKED`), and `scripts/check_doc_drift.py --strict` over the mirrors prints
`problems: none` before a contract change is pushed:

```bash
python .claude/skills/driving-iterations/scripts/check_doc_drift.py --root . --strict \
  wardens/WARDEN.md .claude/skills/warden-play/SKILL.md design/wardens-play.md
```

A stale doc is read against its source and fixed, then re-stamped with `--update`. Re-stamping
without reading is the same lie with a fresher hash.

## Relaying and recording

When a subagent or a command returns output, relay the conclusion and the evidence lines the
conclusion rests on, not the transcript. Stored records (the run record, the Ledger, frame captures)
are kept whole: a truncated record lies about what happened. Tests assert relationships
(`reachable > 0.9 * land`, "every referenced scene exists"), not today's counts.

## Rationalizations this skill exists to stop

| Excuse | Reality |
|---|---|
| "Small, focused change, low risk" | Size is not evidence. The loopback bug was one line. |
| "I re-read it twice, it looks clean" | The author is not the judge. Name the check that would catch the error. |
| "Code complete, just needs a build" | A C# change nobody compiled is `checked`, not complete. |
| "The next person will build it anyway" | Then say `not run here` and give them the command. |
| "Risks: none identified" | Run a premortem (`references/decision-methods.md`) or write "no risk found after a premortem". |
| "I'll push after they confirm" or "push is the next step" | You push, now, before the entry. Nothing is handed over until `git ls-remote` shows it. |
| "The human is in a hurry, skip the template" | The template is what saves the human's next hour. |
| "It is obviously the right call, no need to ask" | One blocking question with an option table costs a minute. |
| "Probably the other server class works differently" | Probably is not read. Name the file you would read to know, and ask. |
| "The reviewer approved it, so it is done" | Approval is a judge verdict on the diff. It compiles nothing. |

## Red flags

- The words done, ready, complete or working without a state from the table
- A status with no command output line in it
- A HANDOVER entry missing a section, or without the previous entry's items dispositioned
- "Push to main", or a HANDOVER entry whose branch is not on origin
- A finding with an empty Remedy
- The same failing command run a third time with a small variation
- Asking two questions at once, or none when one is needed
- A decision that rests on a sentence beginning "probably", "should", "I believe"
- "Ready" after a conditional ("once the build passes, it is ready")

## References

- `references/rubrics.md`: goal, verification and control rubrics (from looper via agency, MIT)
- `references/decision-methods.md`: assumptions, premortem, inversion, steelman, red team, tradeoffs
- `references/templates.md`: the status line, HANDOVER additions, the option table, the finding form, the tool-description grammar, the loop audit
- `scripts/check_doc_drift.py`: the doc-source/doc-hash checker and its tests
