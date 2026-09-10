# Delegating within a package

When to hand part of a package to a subagent or a second session, and the rules that keep the
hand-off from losing work. Condensed from the `dispatch-decision` and `jules-dispatch` skills and the
Ralph scheduler notes in `netzkontrast/agency` (MIT).

## Decide before dispatching

Two disqualifiers, checked first:

1. **It mutates and leaves no record.** A helper that edits files, places buildings or writes the Ledger without its change landing in a branch, a run record or a Ledger line stays inline. Provenance first, parallelism second.
2. **The parent already holds the working set.** If the files are open in this context and the answer is under a page, a fresh agent only re-reads them cold.

Then any one positive signal is enough to dispatch: the result would be long (a report, a survey of
many files); four or more unfamiliar files; the work is a search whose path is unknown; three or more
independent siblings; fifteen minutes or more of wall-clock. Read-only work amplifies a yes; it never
justifies dispatch alone.

Anti-patterns, from the same source:

- **Known-path lookup.** You can name the file and the symbol: read it yourself.
- **One-shot mutation.** A single edit is cheaper inline than dispatch and review.
- **Loop-body dispatch.** Do not dispatch inside a loop; fan the items out once as siblings.
- **Recursive dispatch.** A dispatched agent does not dispatch its own agents; it inlines or returns.
- **The human is waiting.** A direct question is answered inline, not queued.
- **Over-inlining.** Reading twelve unfamiliar files "to be sure" is the signal to dispatch, not to continue.

## The main context schedules; it does not read everything

The Ralph rule: the orchestrating context decides and records; bulk reading, building and long
searches go to helpers that return a conclusion with its evidence lines. State between iterations
lives on disk (the plan, the HANDOVER entry, the run record), never only in the conversation, so a
cold restart resumes from files. Operational files stay lean: status goes into the entry, not into
the plan. Before building anything, search the code for it; "assume not implemented" duplicates what
exists.

## Rules for a second session (cloud or game machine)

1. **Dispatch is a one-way door.** Confirm the spec, the branch and the scope before starting a session; do not start one while a decision is in flux.
2. **Disjoint scopes.** One package per session, its files named; a broad "do everything" brief causes duplicated and conflicting work.
3. **Follow-up messages are input, not control.** They add facts; they do not cancel, redirect or revive a failed session. A failed session is re-dispatched fresh with the lesson in its brief.
4. **A waiting approval expires.** A session parked on a question is answered promptly or closed; it is not left to time out.
5. **Completed is not done.** A session's own "done" is checked against origin (`git ls-remote`) and against the evidence lines it pasted.
6. **Recover, do not re-run.** A session that reports done with nothing on origin is probed once ("push and reply with the SHA, or reply EMPTY"); if nothing comes, its patch is extracted and applied. Re-dispatching the same work wastes the slot and can diverge.

## The brief a helper gets

Three slots, every time: what to read first (paths), what to run before reporting (the exact
commands), and the one structured line to end with (the status line of `templates.md` §1). A helper
that returns a conversation instead of a result was briefed without the third slot.
