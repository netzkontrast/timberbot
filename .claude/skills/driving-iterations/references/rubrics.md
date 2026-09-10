# Rubrics: goal, verification, control

Three rubrics for shaping a package, typing its checks and bounding the loop that works it.
Adapted for this repository from the `loop` rubrics in `netzkontrast/agency` (MIT), which vendor
the rubrics of [looper](https://github.com/ksimback/looper) by Kevin Simback (MIT). The wording
below is rewritten for the Wardens' cloud/game split; the taxonomy and the anti-patterns are theirs.

## 1. Goal rubric: is the package well shaped?

A package (a WP in an iteration plan, a HANDOVER "what you should do" item, a `/goal`) is well
shaped when it:

- names the concrete outcome, not only the activity ("`ledger` returns the Ledger line", not "work on the ledger");
- names the artifact or state that proves it finished (a test that passes, a file that exists, a `campaign.json` field);
- sets scope: included work, excluded work, and how deep to go;
- names the context to read first instead of assumptions to make (the plan section, the design doc, the source file);
- names who consumes the result (the human at the game machine, the next cloud session, the reviewer).

Critique prompts, ask them of every package before starting it:

- What counts as done if two competent agents disagreed?
- Which words are subjective ("works", "clean", "playable") and need a measurable proxy?
- What must be read before drafting anything?
- What is explicitly out of scope?
- Can it be split into a plan artifact, a delivery artifact and a verification artifact?

Anti-patterns: "improve the playtest" with no target artifact; "make the map good" with no
criteria; "research the map format" with no decision the research feeds; a package whose success
depends on information the session cannot gather (a game-side observation, in a cloud session);
a package with no stop condition.

Weak: "Fix the flaky MCP connection."
Better: "Move each MCP request onto a pool thread so `ping` answers while `frame` long-polls;
prove it with `wardens/playtest/mcp_concurrency.py` on the game machine and record the output in
the run record."

## 2. Verification rubric: what kind of check is each claim?

Every claim of done rests on one or more criteria. Each criterion has exactly one type:

| Type | Definition | In this repository |
|---|---|---|
| `programmatic` | A command returns pass/fail deterministically. | `pytest wardens/tools`, `mapsmith check`, `check_cutscenes.py`, `validate.py`, `dotnet build`, `dotnet test`, `git ls-remote`, a `grep` that must be empty |
| `judge` | Someone other than the author scores a rubric and returns a verdict. | A fresh subagent reading the diff against the plan; a code review; a second agent re-running the checks |
| `human` | A person must observe or decide. | Anything seen in the running game; the author's calls (five bots or thirteen; a level's name) |

Rules:

- Prefer programmatic before judge before human. If a test or a checker exists, a judge or a human is not a substitute for running it.
- Each criterion checks one thing and says what failure means.
- A `programmatic` criterion is a command you can paste, with the expected outcome (exit 0, or a line the output must contain).
- A `judge` criterion names the artifact judged and the rubric; the author of the change is never the judge of it.
- A `human` criterion names the machine it needs and the exact observation ("the completion toast appears after the Charging Post's tutorial step").
- The host model grading its own work is not a criterion of any type.

Anti-patterns: every criterion is judge or human while tests exist; "no errors thrown" as the
only criterion; a rubric like "high quality" or "playable" without dimensions; a check that needs
context the judge was not given; a check written as prose when it could be a command.

Judge verdict shape, so different judges are comparable:

```json
{"verdict": "pass", "blocking_issues": [], "confidence": 0.8, "notes": "…"}
```

`verdict` is `pass` or `revise`. Unparseable output counts as `revise`.

## 3. Control rubric: is the loop bounded?

A loop that reworks a package until its criteria pass needs, before it starts:

- a maximum number of iterations;
- a maximum number of revisions per gate;
- a no-progress detector: the same blocker repeating N times stops the loop or asks the human;
- a stop condition that describes success, and one that describes repeated failure;
- a named execution boundary: which branch or worktree it may modify, which actions have side effects (push, PR comment, file delete), and whether those need approval or an idempotency check.

Gate placement: a plan gate before delivery work; a delivery gate after each delivery artifact;
programmatic checks before judge calls; human checkpoints at the high-leverage points (after
the plan, before anything reaches the game machine or an external service).

On failure: stop at the hard cap; write the latest state to the run record; keep the review notes
even when the gate failed; never let the loop revise forever.

Anti-patterns: no maximum iteration count; a judge gate with no judge; a budget in prose but not
enforced; no no-progress detector; a stop condition that requires the agent to feel satisfied.
