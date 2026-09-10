# Decision methods for an open question

Use when a package hits a decision the plan left open (five bots or thirteen; which map the
softlock ran on; whether to rename a level), or before committing a design. Pick the two or three
methods that fit; do not run all of them for every question. Condensed from the `thinking`
capability in `netzkontrast/agency` (MIT). Each method ends in a small written artifact; the
artifact goes into the design doc, the run record or the HANDOVER entry, not only into the chat.

| Method | Ask | Output |
|---|---|---|
| Decompose | Split the question into 3–6 sub-questions, mutually exclusive, together exhaustive. Which one sinks the goal if wrong? | `sub_problems`, the one load-bearing item |
| Assumptions | List 5–10 implicit assumptions without filtering. Mark each load-bearing or not. For each load-bearing one: cheapest way to refute it? Run it. | `[{claim, load_bearing, status: held/refuted, evidence}]` |
| Premortem | Assume the decision failed badly six months on. List 5–7 causes, rank by severity × probability, design a mitigation for the top three, implement the top one now. | `causes`, `mitigations` |
| Inversion | What would guarantee failure? List five patterns. Is any true of the plan today? Remove the worst. | `what_guarantees_failure` |
| Steelman | Write the strongest case for the option you are not choosing. Does your choice survive it? | the counter-argument, the answer |
| Red team | Take the attacker's stance against the system, not the argument: five ways the design breaks in play or at load. | attacks ranked by severity |
| Second-order | For the chosen option: what does it cause next, and what does that cause? Two steps out. | consequences |
| Tradeoffs | Name the two or three axes the options differ on, score each option, say which axis matters most and why. | a table and one sentence |

The pass that is usually enough: Decompose → Assumptions → Premortem → Steelman → a
recommendation with the evidence beside it. The recommendation is a hard gate: no "it depends"
without saying on what, and which observation would settle it.

Worked example, the open question from the 2026-09-10 handover:

> **Question.** Five bots or thirteen at the start of level 01?
> **Assumptions.** (a) The Chapter 1 economy was tuned for five, load-bearing, held (plan WP6 item 3). (b) `WardensStartingPopulation` replaces every spawned beaver, load-bearing, held (PLAYTEST.md counted 13). (c) The count is an author preference, not a design constraint, refuted: the story's first cards name "the five".
> **Premortem.** Thirteen: the food and energy curves of chapter 1 are wrong and the proof run softlocks for a reason that is not a bug. Five: the game mode's child spawns still appear as beavers; the replacement rule needs a filter, which is a C# change and a game build.
> **Recommendation.** Decide before WP4: cap the replacement at the design count and treat the rest as the game-mode's children, because the run's economy is the thing the run must prove. The observation that settles it: day-1 population in the run record.
