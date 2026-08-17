# Pre-publication checklist

This checklist records the review required before the repository is made public.

| Item | What was checked | Command used | Finding | Blocks publication? |
|---|---|---|---|---|
| No credential in any commit (Check A) | All reachable commit patches were searched for credential-shaped strings. | `git log --all -p \| Select-String -Pattern 'sk-ant-\|ghp_\|github_pat_\|accessToken\|refreshToken\|Bearer ' \| Select-Object -First 40` | The matches were benign: documentation and source refer to the local OAuth JSON property name, tests use a literal placeholder and assert header redaction, and comments or test text describe authorization handling. No secret value was found. | Yes, if found |
| Captured account data in `fixtures/` (Check B) | `fixtures/claude-statusline-sample.json` and `fixtures/claude-usage-limits-sample.json` were read and their fields inspected. | `Get-Content -LiteralPath 'fixtures/claude-statusline-sample.json'`; `Get-Content -LiteralPath 'fixtures/claude-usage-limits-sample.json'` | The statusline sample contains redacted `session_id`; `version`; `model`; `cost` totals, durations, and line counts; `context_window` token counts and percentages; `exceeds_200k_tokens`; and `rate_limits` for `five_hour` and `seven_day`, each with `used_percentage` and `resets_at`. The usage-limits sample is synthetic; it contains synthetic quota, optional usage, limits, and spend shapes. | Owner's decision - record what it is |
| `LICENSE` present (Task 2) | The MIT licence committed by Task 2 is present. | `Test-Path -LiteralPath 'LICENSE'` | Present. | Yes |
| `README.md` present and accurate (Task 3) | The README landed after this checklist was first compiled. Every factual claim in it was checked against the code that makes it true: the endpoint, header and redirect policy in `ClaudeOAuthUsageProbe.cs`; the mechanism and tier in `CodexProbe.cs`; what "redacted" covers in `DiagnosticRedaction.cs`; `MinimumRefreshSeconds` in `AppSettings.cs`; the `HKCU` entry in `StartupRegistration.cs`; and the two storage paths in `AppSettingsStore.cs` and `RollingFileLoggerProvider.cs`. | `Test-Path -LiteralPath 'README.md'` | Present and verified. | Yes |
| No hardcoded user path in source or docs | The public documents were checked for an author-specific Windows home path, after the README landed. | `grep -n "C:\\\\Users\\\\\|sgrig" README.md docs/pre-publication-checklist.md LICENSE` | No matches. | Yes - the standing user-agnostic constraint |
| Repository description and topics set | Both are set. The description states what the application is; 14 topics cover the platform and stack (`windows`, `wpf`, `dotnet`, `csharp`, `desktop-widget`, `system-tray`, `developer-tools`), the providers it reads (`claude-code`, `codex`, `anthropic`, `openai`) and the subject (`quota`, `rate-limits`, `usage-monitor`). | `gh api repos/nayots/ai-agent-usage-monitor/topics` | Done 2026-08-17. | No |
| Issue template asking for the redacted diagnostics bundle | `.github/ISSUE_TEMPLATE/bug_report.yml` asks for the **Settings → Diagnostics → Copy** report, states that it is redacted and that no credential is read into that screen, and offers an explicit escape for a build that will not start. Two required checkboxes gate submission: one confirms the report is attached or explains its absence, the other confirms the post carries no token, credential or `Authorization` header. The form also routes security reports to email rather than a public issue. `feature_request.yml` states the permanent scope limits and asks a new-provider request to name its mechanism and tier. | GitHub parsed both forms - confirmed via `gh repo view --json issueTemplates`. | Done 2026-08-17. | No |

Compiled on 2026-08-17 against the commit this checklist is committed with; re-run it if publication happens materially later.

## Outcome

The repository was made **public on 2026-08-17**, at commit `e134277`, with the owner's
explicit decision on the one row that was a judgement call rather than a defect: the
captured quota percentages and session cost in `fixtures/claude-statusline-sample.json`
are published deliberately. Check A found no secret in any commit.

The two rows marked "No" — repository topics, and an issue template asking for the redacted
diagnostics bundle — were closed on 2026-08-17, after publication. Every row in this
checklist is now settled, so it is a historical record rather than an open list.
