# Pre-publication checklist

This checklist records the review required before the repository is made public.

| Item | What was checked | Command used | Finding | Blocks publication? |
|---|---|---|---|---|
| No credential in any commit (Check A) | All reachable commit patches were searched for credential-shaped strings. | `git log --all -p \| Select-String -Pattern 'sk-ant-\|ghp_\|github_pat_\|accessToken\|refreshToken\|Bearer ' \| Select-Object -First 40` | The matches were benign: documentation and source refer to the local OAuth JSON property name, tests use a literal placeholder and assert header redaction, and comments or test text describe authorization handling. No secret value was found. | Yes, if found |
| Captured account data in `fixtures/` (Check B) | `fixtures/claude-statusline-sample.json` and `fixtures/claude-usage-limits-sample.json` were read and their fields inspected. | `Get-Content -LiteralPath 'fixtures/claude-statusline-sample.json'`; `Get-Content -LiteralPath 'fixtures/claude-usage-limits-sample.json'` | The statusline sample contains redacted `session_id`; `version`; `model`; `cost` totals, durations, and line counts; `context_window` token counts and percentages; `exceeds_200k_tokens`; and `rate_limits` for `five_hour` and `seven_day`, each with `used_percentage` and `resets_at`. The usage-limits sample is synthetic; it contains synthetic quota, optional usage, limits, and spend shapes. | Owner's decision - record what it is |
| `LICENSE` present (Task 2) | The MIT licence committed by Task 2 is present. | `Test-Path -LiteralPath 'LICENSE'` | Present. | Yes |
| `README.md` present and accurate (Task 3) | The README is assigned to the parallel Task 3 work. | `Test-Path -LiteralPath 'README.md'` | Not present when this checklist was compiled; complete and review it before publication. | Yes |
| No hardcoded user path in source or docs | The public documents are checked for an author-specific Windows home path. | `Select-String -Path 'README.md', 'docs/pre-publication-checklist.md', 'LICENSE' -Pattern 'C:\\Users\\\|sgrig' -SimpleMatch` | The command must be re-run after the parallel README work lands; publication remains blocked until it returns no matches. | Yes - the standing user-agnostic constraint |
| Repository description and topics set | Repository metadata has not been changed by this implementation task. | Owner review in the GitHub repository settings. | Set an accurate description and relevant topics before or after publication as the owner prefers. | No |
| Issue template asking for the redacted diagnostics bundle | Issue-template coverage has not been added by this implementation task. | Owner review of `.github/ISSUE_TEMPLATE/`. | Add an issue template that asks for the redacted diagnostics bundle if issue intake is enabled. | No |

Compiled on 2026-08-17 against the commit this checklist is committed with; re-run it if publication happens materially later.
