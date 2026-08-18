<!-- codex-workflow:begin v7 -->
# Agent roles → see CLAUDE.md and docs/codex-workflow.md

This project defines its multi-agent workflow (Claude ↔ Codex) in two managed
files: **`CLAUDE.md`** holds the roles and the offload decision, and
**`docs/codex-workflow.md`** holds the procedure. This file is intentionally a
pointer only, to avoid duplicated, drift-prone role descriptions.

**Codex:** your role is defined in **`docs/codex-workflow.md`**, section
**"Codex's role"** (the last section). Read that file and follow it. You may be
invoked two ways:
- **Via the official Codex plugin** — Claude hands you a single task and either
  waits for it or polls the job record.
- **Via CLI / `/codex:transfer`** — the user runs you against the Superpowers
  plan at `docs/plans/<feature>.md`, or a transferred session.
Either way, you are the implementer; Claude plans and does the final review.

Two rules in that section are easy to miss and have both caused real damage —
read them before you clean up or give up:
- **Never delete untracked files you did not create.** Claude shares this
  working tree and writes into it.
- **If the sandbox blocks `.git` writes, stop with the work verified and
  uncommitted** and say so. Claude commits on your behalf.

**Any other agent (including Claude Code):** follow `CLAUDE.md` — it governs all
roles and points to the procedure when one is needed.
<!-- codex-workflow:end -->
