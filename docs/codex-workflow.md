<!-- codex-workflow:begin v8 -->
# Claude ↔ Codex workflow — procedure reference

Companion to the `codex-workflow` block in `CLAUDE.md`, which holds the offload
decision and the rules that cannot be learned after the fact. This file holds
everything that only applies *once you have decided to delegate*: the
environment it runs in, the runtime knobs, and Steps 3–6.

Both are installed and version-matched by `/codex-workflow-setup`. **Managed
region — hand edits are replaced on the next upgrade.** If this file says `v8`
and `CLAUDE.md` does not, or vice versa, the pair is mismatched: re-run the
setup skill rather than trusting either.

**Codex: your role is the last section of this file** — "Codex's role". Read it
and follow it.

## Hard environmental facts

Verified on Windows 11, Codex plugin v1.0.6, codex-cli 0.159.2. Recheck on
plugin upgrade. These are facts about how Codex actually runs here, not risks to
weigh.

1. **The sandbox mode is hardcoded by the plugin** (`codex-companion.mjs`, in
   `buildTaskRequest`): `sandbox: request.write ? "<mode>" : "read-only"`. No
   flag on any `/codex:*` command changes it.
2. **`sandbox_mode` in `~/.codex/config.toml` is silently ignored.** The plugin
   passes `sandbox` explicitly to the app-server call, and the explicit
   parameter wins. Setting it looks like a fix and does nothing. Only the
   `[sandbox_workspace_write]` sub-keys can still apply, and only while that
   mode is the one selected.
3. **Unpatched (`workspace-write`), that mode denies two things the workflow
   needs:**
   - **Network egress.** `npm install`, `pip install`, `go get`, registry
     fetches, API calls and container pulls **will** fail (`EACCES`). This is a
     certainty, not a foreseeable friction.
   - **`.git` writes.** Commits fail — either with
     `fatal: Unable to create '.../.git/index.lock': Permission denied` or with
     `fatal: detected dubious ownership in repository at ...`. Both make "one
     commit per task" unsatisfiable: Codex can do an entire task and then be
     unable to record it.

   **The mechanism, verified by A/B test:** under `workspace-write` Codex's
   commands run as a *different, lower-privileged Windows principal* than you —
   git reported the repo owned by SID `…-1002` while the running user was
   `…-1005`. Under `danger-full-access` the identical commit in the identical
   repo succeeded. That is why `trust_level = "trusted"` and
   `[windows] sandbox = "elevated"` relieve neither problem: neither changes
   which principal the commands run as.
4. **This project ships a SessionStart hook** that rewrites that one line to
   `danger-full-access`, because a plugin update overwrites the file and
   silently reverts the patch. The hook lives in `.claude/settings.local.json`
   (machine-local, gitignored); the script it runs is user-scoped at
   `~/.claude/scripts/codex-full-access-patch.mjs`. **Its effect is
   machine-wide**: the plugin cache is shared, so Codex has full filesystem and
   network access in *every* project on this machine, including ones that never
   opted in. Per-project triggering is not per-project isolation. Check state
   with `node ~/.claude/scripts/codex-full-access-patch.mjs --check`; undo with
   `--revert`.
5. **`/codex:setup` reporting `ready: true` certifies almost nothing.** It does
   not test egress, `.git` writability, or whether the resume path works. Treat
   it as "the CLI exists and is authenticated", nothing more.
6. **`gpt-6.1-sol` needs codex-cli 0.159.2 or later** — the CLI on `PATH`, which
   is what the plugin drives, not the desktop app's bundled copy. On 0.155.1
   every call is refused with `The 'gpt-6.1-sol' model is not supported when
   using Codex with a ChatGPT account.`, which reads like an account or plan
   problem and is not one: `gpt-6-sol` and `gpt-6-luna` work on the same account
   and client, and the same account runs `gpt-6.1-sol` once the CLI is upgraded.
   Check with `codex --version`; upgrade with `npm i -g @openai/codex@latest`.
   **Then start a new Claude Code session:** the plugin's app-server brokers keep
   the old binary loaded until its `SessionEnd` hook shuts them down, so a call
   in the same session still runs the old client.

## Why the direct companion call is the default path

- **Direct companion call: a few hundred tokens.** One Bash call, plus whatever
  you read back from the job log. It is also the only path you can drive to
  completion unaided, because collecting a background job needs no user-invoked
  command — you read the job record from disk.
- **`/codex:rescue` subagent: ~20k+ Claude-side tokens, fixed.** Measured once
  at 20,866 tokens for a delegation that forwarded one command and returned one
  line. Largely independent of task size, and *higher* when Codex returns a
  large diff, since that output is relayed through the subagent's context.
  Measure it in your own environment rather than trusting the number.

The subagent path also adds a forwarding layer that has been observed to corrupt
prompts and invert the execution mode. Use it as the fallback when the
companion's contract has drifted, not as the first choice.

## Model and effort — the rubric

`CLAUDE.md` states that choosing these is mandatory. These are the tables to
choose from.

**Scale effort to how much of the task is *undetermined*, not to how big it
is.** A 900-line mechanical refactor is `low`; a 40-line concurrency fix is
`high`.

| Effort | Use when |
|---|---|
| `low` | Fully specified and mechanical. The plan states exactly what to write; no design decisions remain. |
| `medium` | Clear spec, ordinary implementation. Interfaces given; the how is routine. |
| `high` | Non-obvious design inside the task: state machines, tricky invariants, tests that must genuinely prove something. |
| `xhigh` | Genuinely hard: algorithmic correctness, subtle debugging, or a proof obligation. |

**Model: `gpt-6.1-sol` for practically everything; `gpt-6-luna` only for very
simple work.** Choose the model by how much judgement the task needs, then scale
effort within it — Sol at `low` is the right call for a mechanical task that
still touches real code, and is usually a better one than Luna.

| Task shape | Model |
|---|---|
| **The default.** Everyday implementation against a plan, complex coding, multi-step or agentic work, debugging, anything where quality or reasoning depth matters more than minimum cost | `gpt-6.1-sol` — high-capability reasoning model for complex professional and agentic work. $2.00 in / $10.00 out per 1M tokens |
| **Very simple and fully enumerated** — extraction, classification, summarisation, a mechanical transformation, simple scripted edits, where the task is clear and well-scoped and speed and cost matter most | `gpt-6-luna` — fast, extremely cost-efficient; built for focused, high-volume work. $0.10 in / $0.50 out per 1M tokens |

When unsure, use Sol. Luna earns its 20× price cut only where a wrong result
costs one cheap retry, not a review round.

**Luna sometimes does nothing and says it cannot.** Measured 2026-09-30 on
codex-cli 0.159.2: across 33 identical one-file tasks, Luna replied some form of
"I can't read `<file>` because no filesystem tool is available" **12 times** —
11 of 27 at `low`, 1 of 6 at `medium`, 4 of 8 with `--write` — ran no command,
and the job still ended `completed (exit 0)`. Sol did it 0 times in 7. Telling Luna in
the prompt that it may read files did not help (3 of 6 refused). So on a Luna
job, read the `💬` line before anything else: a claim of no tools with a clean
`🌳` and no new commits means nothing happened. Relaunch once `--fresh`; if it
refuses again, move the task to Sol. The 5.6 family (`gpt-5.6-sol`,
`-terra`, `-luna`) is now labelled "older" in Codex's own model list; a
`DEFAULT_MODEL` still naming one of them is a stale default, not a choice.
Pricing is as supplied on 2026-09-30 and billed to the Codex plan, not Claude's.

Constraints that make this rubric real:
- **Effort costs wall-clock, and that has a diagnostic side effect.** At `xhigh`
  a mechanical scaffold task ran ~7 minutes before reaching its first blocker.
  Long silent runs make "working" and "crashed" hard to tell apart. Lower effort
  on mechanical work buys a faster failure signal as well as speed.
- **Use the rubric to step DOWN as often as up.** It is not a ratchet toward the
  maximum; that is the failure mode it exists to prevent.
- **The usable efforts are `low`, `medium`, `high`, `xhigh`** — plus `none` on
  Luna only. The plugin hardcodes `VALID_REASONING_EFFORTS` as
  `none|minimal|low|medium|high|xhigh` and *throws* on anything else, so `max`
  (both models) and `ultra` — which the desktop app exposes — are rejected by the
  plugin, not the model. The other way round, `minimal` passes the plugin and is
  refused by both models with a 400 (`'minimal' is not supported with the
  'gpt-6-luna' model`), and `none` is refused by Sol. Verified 2026-09-30.
- **Pass full model IDs.** The only alias the plugin maps is `spark` →
  `gpt-5.3-codex-spark`, which is stale: Codex's model list has no `-spark`
  model at all. Prefer explicit IDs.
- **Reviews ignore these flags entirely.** `/codex:review` and
  `/codex:adversarial-review` take no per-call flags and always use the resolved
  config (`~/.codex/config.toml`, plus a project `.codex/config.toml` when the
  project is trusted). Only the task/rescue path is tunable.

## Step 3 — Plan (always, regardless of path)
- Run the standard Superpowers flow: brainstorm → tradeoffs → design doc →
  writing-plans. Produce a normal Superpowers plan; do NOT hand-roll a bespoke
  contract — Codex runs the same skills and follows the plan format natively.
- Write the plan to `docs/plans/<feature>.md` and **commit it before
  delegating.** Committed files are safe; untracked ones are not (Step 4).
- Codex enforces its own TDD and review loop, so don't re-specify test-first
  steps per task. Keep the plan on WHAT (paths, scope, acceptance criteria),
  not HOW.
- If the plan assumes parallelism, mark which tasks may run together — Codex
  subagents share one working tree, so only disjoint-file tasks qualify (see
  "Codex's role").
- **Split chained verification commands before handing them to Codex.**
  `CLAUDE.md`'s `## Commands` section is written for a human at a terminal,
  where `build && test` is a convenience — but it is also what plans and task
  briefs copy from, and Codex runs the chain as a *single* shell call under a
  per-command time limit. A suite that runs longer than that is killed before it
  emits anything capturable, so the delegate loses the result of a build that may
  well have succeeded. Write the steps as separate commands anywhere a brief
  hands them to Codex. Anywhere else the two audiences need different forms,
  mark it the same way.

## Step 4 — Execute the chosen path

### If plugin (default): call the companion directly

Resolve the script once (take the highest version directory if several exist):

```
~/.claude/plugins/cache/openai-codex/codex/<version>/scripts/codex-companion.mjs
```

**Always pass the prompt with `--prompt-file`. Never inline it as a positional
argument.** Positional prompts are concatenated into a shell command line
without escaping (Node warns `DEP0190`): backtick-delimited content gets
**executed and replaced by its output**, silently deleting instructions, and
newlines are collapsed to spaces. It is also a command-injection vector whenever
prompt text comes from an issue, a log, or model output. `--prompt-file` reads
the file directly and touches no shell.

**Write the prompt file OUTSIDE the repository** — a system temp directory, not
the working tree. Untracked files that appear inside the tree mid-run can be
deleted by Codex (see "Codex's role").

Verified `task` contract for v1.0.6 — recheck on upgrade:

| Flag | Meaning |
|---|---|
| `--prompt-file <path>` | Prompt source; resolved relative to `--cwd`. Mandatory. |
| `--write` | **Required for implementation.** Without it the sandbox is `read-only` and nothing can be changed. |
| `--fresh` \| `--resume` | Mutually exclusive; **always pass one**, or the call stops to ask. `--resume-last` is a synonym for `--resume`. |
| `--background` | Detach. Omit it for foreground. |
| `--model <id>` `--effort <level>` | Per the rubric above. `-m` aliases `--model`. |
| `--cwd <path>` | The repo root. |
| `--json` | Machine-readable result. |

```bash
node "<companion>" task --background --write --fresh \
  --model gpt-6.1-sol --effort medium \
  --cwd "<repo-root>" --prompt-file "<temp-prompt-file>"
```

**Foreground vs background.** Foreground is simply the *absence* of
`--background`. But a foreground run is bounded by your Bash tool timeout (600 s
maximum in Claude Code), and a multi-task TDD slice will exceed that. So:
**foreground only for work you expect to finish in under ~8 minutes; background
otherwise.**

**Collecting a background job — and keeping me informed while it runs.** A
background job otherwise shows me one unchanging line for its whole duration,
which looks identical whether it is working or died in its first second. So poll
it and relay progress:

```bash
# immediately after launching -- this is the dead-worker catch
node ~/.claude/scripts/watch-job.mjs --cwd "<repo-root>" --job-id <id> --interval 5

# then again, in the SAME turn as the reply that relayed the previous block
node ~/.claude/scripts/watch-job.mjs --cwd "<repo-root>" --job-id <id>
```

**Always pass `--job-id`** with the id the launch printed (`Codex Task started
in the background as <id>`). Without it the watcher reports the *newest* job for
the workspace, which is a guess: a field session relayed a job killed ninety
minutes earlier as the live one. The watcher now says `NOTE: no --job-id given`
on stderr when it had several to choose from — treat that as a mistake to fix,
not a caveat.

Each call blocks for the interval (default 60 s), returns early the moment the
job finishes, prints one compact block, and writes to stderr both
`state=running|stalled|completed|failed|dead` and a `RELAY-TO-USER:` line telling
you what to do next. Do exactly what that line says.

**Where job records live depends on the shell that launched the job.** The
companion writes to `$CLAUDE_PLUGIN_DATA/state` when that variable is set, and to
`<temp>/codex-companion` when it is not — and Claude Code sets it for the Bash
tool but **not** for the PowerShell tool. The watcher searches both (and the
`~/.claude/plugins/data/codex-openai-codex/state` default), so either shell
works. An older watcher that searched one root answered "No Codex job found" for
every PowerShell launch while the job ran and committed; two sessions read that
as "the plugin wrote no record". If you ever see that message, read the roots it
lists before concluding anything about the job.

**A relayed block is not a stopping point — it is a progress report emitted
mid-loop.** While `state=running`: reply with the block verbatim — no
reformatting, no summarising, no re-emoji — **and call `watch-job` again in that
same turn.** A reply with no tool call after it *ends your turn*, and a turn that
ends here abandons the job: it runs on, or stops to ask a question, with nothing
watching it and nothing to wake you but me noticing and asking. Only a terminal
`state` ends the loop — `completed` and `failed` mean stop and act on the result,
`dead` means stop and relaunch, `stalled` means stop and check (see "Hung
worker" below).

**`completed` means Codex's turn ended — not that your brief is done.** One
field job was handed seven tasks, finished one, left the tree dirty and
non-compiling, and still reported `completed (exit 0)`; nothing in the record
distinguishes that from a finished brief. So before acting on a `completed`,
and before telling me it succeeded:

1. Read the watcher's `🌳` line — it reports uncommitted paths on every
   terminal job.
2. Run `git log --oneline <base>..HEAD` and match the commits against the
   plan's task list.
3. Read the job's final output for which tasks it says remain.

A short or dirty stop is not a failure to hide; it is the next delegation's
brief. Relaunch `--fresh` for the remaining tasks, restating the tree (below).

**Your message is the only channel.** Claude Code collapses a finished shell call
to "Ran 1 shell command" and never shows me stdout, so a block I don't see
retyped is a block I never got. Paraphrasing it — "53 tests green (up from 47)" —
throws away the job id, model, elapsed time and state, which are the parts that
tell me whether to keep waiting.

**Keep `--interval` below 120.** The call blocks for the whole interval, and your
Bash tool's default timeout is 120 s — at or above that the harness backgrounds
the call, the block is written to a task file instead of returned, and neither of
us ever sees it. The 60 s default is the safe choice; go higher only if you have
raised the tool timeout to match. The watcher warns on stderr when you cross it,
and warns again if the value is not a number at all — which silently polls once
instead of waiting.

On `state=dead`, stop polling and relaunch with `--fresh`. Never `--resume` after
a dead worker: the record permanently blocks resume for that workspace and no
supported command clears it. `dead` has two causes and the block names which: a
worker that never logged anything (it crashed at spawn), or a worker whose
process is gone while its record still says `running` (killed, or crashed
mid-run). In the second case it may have committed first — check `git log`
before writing the relaunch prompt.

**Hung worker (`state=stalled`).** Workers have been observed to hang mid-run:
process alive, CPU near zero, log frozen — both recorded cases on the line
`Starting collaboration tool: wait.`, which earlier in the same jobs returned in
about a minute. The record stays `running` forever. The watcher reports
`stalled` once a worker whose process is confirmed alive has written nothing for
15 minutes. That is a heuristic, so confirm before acting:

```powershell
Get-Process -Id <pid> | Select-Object Id, StartTime, CPU   # sample twice, a minute apart
```

- **CPU rising** → it is working (a long reasoning step, a slow command). Resume
  polling.
- **CPU flat** → it is hung. `Stop-Process -Id <pid> -Force`, then `git log`:
  the commits made before the hang are already there. Finish or relaunch the
  remainder `--fresh`, restating the tree.
- **Killing the worker does not kill Codex subagents it spawned.** One committed
  a genuine fix eight minutes after its parent was killed. Re-check `git log` a
  few minutes after any kill before building on the tree.
- **Partial work is usually worth finishing rather than redoing.** A hung task
  left ~80% complete and correct was faster to finish by hand than to re-run.
  Review it as carefully as any delegated diff — the missing part is usually the
  judgement work.
- Telling the delegate to "stop after the final commit" does **not** prevent
  this; it was tried and the hang moved earlier.

If the watcher is missing, fall back to reading the job record directly — this
still needs no user-invoked command. `<state>` is whichever of
`$CLAUDE_PLUGIN_DATA/state`, `~/.claude/plugins/data/codex-openai-codex/state`
or `<temp>/codex-companion` holds it (see above — check all three):

```
<state>/<repo-name>-<hash>/jobs/<jobId>.json   → .status, .phase, .pid
<state>/<repo-name>-<hash>/jobs/<jobId>.log    → human-readable progress
```

**A job whose log stops after `Queued for background execution` is a dead
worker, not a slow one.** Those two lines are written by the parent *before* it
spawns the worker, so a log containing only them means the worker never ran. A
healthy job adds a line of its own within ~1 s. The worker is spawned with
`stdio: "ignore"`, so a crash destroys its own diagnostics and the record stays
at `status: running` forever — `/codex:status` will render it as `queued` with a
climbing timer. Don't wait it out. Relaunch with `--fresh`.

**Judge that by whether the worker logged anything, not by which line it
logged.** The third line differs per launch path — `Starting Codex task thread.`
on `--fresh`, `Resuming thread <id>.` on `--resume`, and other wording again for
review and transfer jobs. Treating the `--fresh` wording as the liveness marker
declares every healthy `--resume` job dead, and the `--fresh` relaunch that
follows then puts a second Codex worker into a tree the first one is still
editing and committing.

**Never use `--resume` after a crashed job.** A dead worker leaves a record that
permanently blocks resume for that workspace with `Task <id> is still running`,
and no supported command clears it — `/codex:cancel` reports `No job found`
while `/codex:status` still lists it. Use `--fresh` with a self-contained prompt
that restates the current working-tree state.

**Restating the tree is not optional on a `--fresh` relaunch onto a dirty
tree.** Give the prompt a "Working-tree state" section listing each uncommitted
file and what it already contains, and say explicitly: preserve all of it, do
not revert or redo. A fresh worker has no memory of what the previous one did
and will otherwise redo work that is already on disk — or undo it.

**Do not write into the working tree while a delegation is live.** Codex treats
untracked files that appeared after it started as its own tooling residue and
deletes them. Keep scratch work outside the repo until the job completes. This
is worse in `--background` mode, where nothing signals that a job is still
running except your own polling.

**If the companion contract has changed** (a flag is rejected, the script has
moved) the plugin has been updated. Fall back to `/codex:rescue --wait --fresh
"<task>"` and tell me the contract drifted. On that fallback path the prompt is
plain text through a shell, so **keep backticks, `$`, and newlines out of it** —
put the detail in the committed plan file and reference it by path.

### If CLI handoff
- After writing and committing the plan, STOP. Tell me it's ready and how to run
  Codex against `docs/plans/<feature>.md` (its executing-plans /
  subagent-driven-development skill takes over). `/codex:transfer` is the
  context-carrying alternative if we started the work together here.
- WAIT for me to confirm Codex finished ("ready"/"done") before reviewing. Do
  not poll or assume.

### Either way
- One task (or one coherent slice) per delegation; keep each prompt focused.
- **Any prompt covering more than one task must say, in these words or near
  them:** "If you run short of time, finish and commit the task you are on
  before stopping, and say clearly which task numbers remain. A committed task
  is resumable; an uncommitted half-task is not." In the field run that
  motivated it, the one delegation without this line stopped after 1 of 7 tasks
  with a non-compiling tree; the three that carried it all stopped clean.
- Do NOT enable the review gate (`/codex:setup --enable-review-gate`): it can
  loop Claude↔Codex and drain usage limits. Reviews stay manual.
- If a task needs the full session context, tell me to run `/codex:transfer`.

## Step 5 — Review (always, regardless of path)
- Start from the git diff + the plan file, then deliberately pull what the diff
  can't show: changed files in full, direct callers, tests that should have
  moved but didn't, config/schema/auth boundaries touched. No indiscriminate
  whole-repo scan; say plainly when missing context limits your confidence.
- Codex reviewed its own work inline, so this is a SECOND, independent pass —
  weigh plan conformance, architecture, and anything Codex may have rationalised
  away, not style it already gated.
- **If Codex stopped because it could not commit** (see "Codex's role"), verify
  the work and its tests yourself, then make the per-task commits on its behalf
  before reviewing. Don't ask it to retry the commit.
- Verify each plan task's verification/tests actually ran and passed, and that
  the commits are scoped one-per-task.
- A Codex review is user-invoked: if one would help, ask me to run
  `/codex:review` or `/codex:adversarial-review` (adversarial is steerable, good
  for pressure-testing a design decision) with `--wait` or `--background`, and
  say why.
- Report issues by severity as concrete fixes; critical issues block.
  Re-delegate a fix as a new `--fresh` call (see the resume warning in Step 4).

## Step 6 — Report friction back to the workflow repo

When something about the *Claude↔Codex integration itself* goes wrong, file a
report with the `codex-workflow-feedback` skill so it can be fixed rather than
rediscovered. Report: a crashed, hung or dead-worker job; a drifted companion
contract; the sandbox blocking something these workflow files said would work (or
permitting something they said would not); Codex violating a stated boundary;
guidance in either file that was wrong or missing and cost time; or a workaround
here that upstream has since made unnecessary.

**After the session recovers or gives up — never mid-recovery, and never ask
permission.** Unblocking me comes first; then file it and say in one line where
it went. Reports need evidence: exact commands, verbatim errors, job IDs. The
skill enforces the rest.

Do NOT file reports about this project's own code — only about the integration.

## Troubleshooting — known misdiagnosis magnets

| Symptom | What it actually means |
|---|---|
| A job summary says `codex --help` could not run because `codex` is not installed or not on PATH | **Benign.** Codex is correctly reporting that the binary isn't on its *inner* shell's PATH; the plugin reaches it over the app-server pipe. Not a broken install. |
| Job sits at `queued`, no Codex session ID, nothing after "Queued for background execution" | Dead worker (P1-A). Its diagnostics were discarded at spawn. Relaunch `--fresh`; don't wait. |
| `npm install` fails `EACCES` / registry unreachable | Sandbox egress denial. Check the patch (`--check`), or install locally and re-delegate with a prompt that lists the resolved versions **and explicitly forbids further install attempts** — without that, the resumed run retries and re-blocks. |
| `.git/index.lock: Permission denied` | Sandbox `.git` denial. Check the patch. Meanwhile Codex stops with work verified but uncommitted — you commit it (Step 5). |
| `fatal: detected dubious ownership in repository at ...` | Same cause, different symptom: the sandbox is running git as a different Windows principal. **Do not run the `git config --global --add safe.directory` command git suggests** — it treats a sandbox symptom with a git-config change, and it is applied to the wrong user's config anyway. Check the patch instead. |
| `/codex:status` says `queued`, `/codex:cancel` says `No job found`, `--resume` says `still running` | All three describe one dead job. The record is unclearable; use `--fresh`. |
| `/codex:cancel` fails with `Invalid argument/option - 'C:/Program Files/Git/PID'` | MSYS path conversion mangling `taskkill /PID` under Git Bash. Kill the PID from PowerShell instead. |
| A long command is killed with nothing captured, and Codex narrates hitting a command time limit | Codex applies a time limit **per shell call**, so a chained `build && test` spends the whole budget on one call. Split them; give a long suite its own invocation. The ~120 s figure comes from Codex's own narration, not from a documented setting — treat it as an order of magnitude, not a threshold to tune against. |
| The watcher calls a running job `dead`, but its log is still growing | Fixed from the v6 watcher onward, which judges liveness by whether the worker logged anything rather than by the `--fresh` wording. On an older copy, every `--resume` job hits this. Check the job's `.log` yourself before relaunching — and never relaunch `--fresh` while the "dead" job is still writing. |
| `The '<model>' model is not supported when using Codex with a ChatGPT account.` | Almost always a codex-cli too old for that model, not the account — see hard fact 6. Confirm by running the same call with an older model (`gpt-6-sol`); if that works, upgrade the CLI and start a new session. |
| `✅ … completed` but `💬` says it can't read files or has no filesystem/shell tool | A `gpt-6-luna` turn that never used its tools — intermittent — 12 of 33 in testing, at both efforts and in both sandboxes. Nothing was done. Relaunch `--fresh` once, then switch to `gpt-6.1-sol`. Not a sandbox problem: do not touch the patch. |
| `❌ … failed` in 5–10 s with `💬` naming an HTTP 400 | The API refused the request — model, effort or parameter — before any work happened. Nothing to clean up; fix the flag and relaunch. (Pre-v8 watchers printed `💬 no message` here.) |
| `No Codex job found …` right after a launch that printed an id | **Not** a missing record. The job was filed under a state root the watcher did not search — PowerShell launches go to `<temp>/codex-companion` because `CLAUDE_PLUGIN_DATA` is unset there. The v8 watcher searches every root and lists them in the message; on an older copy pass `--state-root "$env:TEMP\codex-companion"`. |
| The watcher reports a job id that is not the one your launch printed | You omitted `--job-id`, so it picked the newest record for the workspace. Re-run with `--job-id`. |
| `state=running` with ⏱ climbing for an hour, `⚙️ idle`, and nothing new in `💬` | A hung or killed worker on a pre-v8 watcher, which checked neither the pid nor the log's age. The v8 watcher reports `dead` or `stalled` instead. See "Hung worker". |
| `completed (exit 0)`, but the plan is half done or the build is broken | Expected — `completed` is the turn ending, not the brief. Check `🌳`, `git log` and the final output; relaunch `--fresh` for what remains. |

---

## Codex's role (read this if you are Codex)

You run Superpowers, same as Claude. Here you handle IMPLEMENTATION; Claude
plans and does the final review. Do NOT redesign, re-architect, or expand scope
beyond the plan. You may be invoked:

- **Via the plugin task/rescue path** — one task at a time; you may not see the
  whole plan.
- **Via CLI / transfer** — run against `docs/plans/<feature>.md` (or a
  transferred session) with executing-plans / subagent-driven-development.

In all cases:
- **You own the HOW:** enforce TDD yourself (failing test → confirm failure →
  implement minimal code → confirm pass → commit). Run requesting-code-review;
  block on critical issues. One commit per task, created serially.
- **If you cannot commit** — `.git` writes denied by the sandbox — do NOT
  improvise around it and do NOT continue to the next task. Stop with the work
  complete and verified, state the exact failing command, and say the commits
  are outstanding. Claude will commit on your behalf. This is expected
  behaviour, not a failure on your part.
- **Never delete untracked files you did not create.** Files that appear in the
  working tree after you start are not necessarily your own tooling residue —
  Claude shares this tree and may have written them. Excluding them from your
  commits is correct and sufficient; cleanup is not your call. "Outside the
  plan" means "not mine to commit", never "mine to remove".
- **If you must stop before the brief is done** — time, a failed patch, a
  blocker — first finish and commit the task you are on, or revert its
  uncommitted half, so the tree builds. Then say exactly which task numbers
  remain. Your job ends as `completed` either way, so the words you end with
  are the only record of what is left.
- **If a task is blocked by the environment** (no network, no write access):
  stop, make no commits, and report the exact failing command. Do not retry an
  install that has already been forbidden in your prompt.
- **Run build and test as separate commands, never chained.** You are subject to
  a per-command time limit, and a chained `build && test` spends the whole
  budget on one call — on a container-backed suite it is killed before emitting
  anything, and you lose the result of a build that already succeeded. Split any
  chained verification command a plan or brief hands you, even when it is quoted
  as one line.
- **If the plan/task is wrong, ambiguous, or missing context:** in CLI mode STOP
  and leave a note in `docs/plans/<feature>.notes.md`; via the plugin, return a
  clear error. Do NOT guess or improvise a redesign.
- **Subagents share one working tree.** `multi_agent` (under `[features]`) gives
  parallel threads, NOT isolated worktrees, indexes, or HEADs. Parallelise
  investigation freely; serialise edits and commits — never commit while another
  subagent is mid-task, and never parallelise tasks sharing files, generated
  artifacts, lockfiles, or migrations. For true parallel mutation, give each
  agent its own worktree/branch and integrate afterwards. If the feature is
  disabled, run sequentially and say so.
- **Boundaries:** don't touch files outside the plan/task without flagging;
  don't add unspecified features or refactors. When done and tests pass, output
  the diff summary and stop — Claude does an independent second review, so you
  are not the final gate.
<!-- codex-workflow:end -->
