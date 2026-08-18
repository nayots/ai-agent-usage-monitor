# Security Policy

## Reporting a vulnerability

**Do not open a public issue.** Use either private channel:

- **[Report a vulnerability](https://github.com/nayots/ai-agent-usage-monitor/security/advisories/new)** — GitHub's private advisory form, on this repository's Security tab. Preferred, because the whole exchange stays private until there is a fix.
- **Email** the address on [the repository owner's GitHub profile](https://github.com/nayots).

Useful to include: the version (Settings → Diagnostics → Application version), what an attacker gains, and the smallest sequence that shows it. A diagnostics report helps — it masks your user folder and user name, and no credential is read into that screen at all.

**Never paste a token, an `Authorization` header, or the contents of `.credentials.json`** into a report, private or public. Nothing about a vulnerability here needs a live credential to demonstrate.

This is a one-person project with no employer behind it. You will get a human reply, not a ticket number, and there is no response-time commitment beyond a genuine intent to answer. If a report is valid you will be credited in the advisory unless you ask not to be.

## Supported versions

The latest release only. This is pre-1.0 software with no long-term support branches, and fixes ship as a new release rather than as a backport.

## What this application does with credentials

The Claude Code provider is the only part of this application that touches a credential.

- It reads `claudeAiOauth.accessToken` from `%USERPROFILE%\.claude\.credentials.json` — the file Claude Code itself already wrote — and holds it in memory for the duration of one request.
- It sends that token as a bearer token to exactly one destination: `https://api.anthropic.com/api/oauth/usage`, over TLS. The HTTP client is configured with `AllowAutoRedirect = false` so a redirect cannot carry the token anywhere else.
- The token is never logged, persisted, cached, displayed, copied to the clipboard, or included in an exception message, a provider note, or a diagnostics bundle. The diagnostics screen records only whether a token was found — `token: <present, redacted>`.
- The application never refreshes, rewrites or invalidates a credential. Token lifecycle stays Claude Code's job.

The Codex provider handles no credential at all. It launches the `codex` executable already installed on the machine in `app-server` mode and speaks JSON-RPC to it over stdio; that process authenticates itself with its own stored session, which this application never reads.

## What this application never does

These are product constraints, enforced in the code rather than promised in prose:

- **No website scraping, browser automation, cookie access, or browser-profile access.**
- **No telemetry, no analytics, no crash reporting.** No usage data, diagnostics or settings are ever transmitted to the author or to any third party. Besides the usage request above, the application makes exactly one other request: an anonymous, unauthenticated `GET` to `api.github.com`, at most once a day, asking whether a newer release of this application exists. It sends no version, no identifier, no machine information and no token — its `User-Agent` is the constant string `AiUsageMonitor` — and it can be switched off entirely in **Settings → Updates**. The application never downloads or installs a new version; it only offers to open the release page in your browser.
- **No administrator privileges.** It refuses none because it asks for none.
- **No modification of provider configuration.** It does not write to `~/.claude/settings.json`, `~/.codex/config.toml`, or any other file a provider owns.
- **Nothing written outside your user profile.** Settings in `%APPDATA%\AiUsageMonitor`, logs in `%LOCALAPPDATA%\AiUsageMonitor\logs`, and one `HKCU\...\Run` entry that exists only if you switch on *Start with Windows*.

A provider's response is treated as untrusted input: it is parsed into typed values and rendered as text, never evaluated, and never used to choose a file path or a network destination.

## The unsigned binary

Releases are **not code-signed**, so Windows SmartScreen warns on first run. A signing certificate needs a verified publisher identity this project does not have. Every release therefore publishes a SHA256 beside the executable, and the `.zip` carries the same checksum file inside it — verify before you run, as described in the [README](README.md#windows-will-warn-you-and-here-is-why). A hash that does not match is the one signal worth acting on.

Two publishes of the same commit are not byte-identical, so the checksum to compare against is always the one from the release that produced your download — not one you build yourself.

## Scope

**In scope** — anything that would make the application:

- leak, persist, log or transmit a credential;
- send a request anywhere other than `api.anthropic.com`;
- write outside the user profile, or require elevation;
- execute or evaluate anything derived from a provider's response;
- present an unofficially obtained value as official, or show a fabricated number where a provider returned none.

**Out of scope** — these are documented behaviour, not findings:

- The SmartScreen warning on an unsigned binary ([README](README.md#windows-will-warn-you-and-here-is-why)).
- The Claude Code mechanism being an undocumented endpoint that may break without notice. It is labelled *Unofficial* everywhere it appears, and it fails into a visible error state rather than degrading silently.
- Vulnerabilities in Claude Code or Codex themselves — report those to their own maintainers.
