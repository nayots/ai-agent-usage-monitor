<!--
  Two variants rather than one, because the wordmark's ice-to-gold gradient is only legible on
  deep navy: on GitHub's light theme the white middle of it would disappear. The transparent
  master serves dark theme, the plated master serves light. Neither is resized here - the
  lettering is 48px tall in both, and the plated file is taller only because it carries the
  brand's required clear space. Masters copied verbatim from the nayots branding repository;
  do not recolour, crop or rename them.

  Deliberately NOT wrapped in a link to nayots.com. GitHub's HTML sanitizer lifts an <img> out
  of any surrounding anchor and re-links it to the image file, and in doing so it lifts the img
  out of the <picture> too - leaving an empty picture inside the link and a bare image beside
  it, with the theme swap dead. Verified against the /markdown API. The site link lives in the
  footer as text instead.
-->
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/nayots-wordmark-transparent-h48.png">
  <source media="(prefers-color-scheme: light)" srcset="docs/assets/nayots-wordmark-on-navy-h48.png">
  <img src="docs/assets/nayots-wordmark-on-navy-h48.png" alt="nayots">
</picture>

# AI Usage Monitor

**See how much of your AI coding plan you have left — without opening a session to ask.** 📊

A small Windows desktop widget that shows live quota usage for the AI coding tools already installed on your machine: **Claude Code** and **Codex**. It sits in your notification area, and one glance tells you where you stand.

[![Latest release](https://img.shields.io/github/v/release/nayots/ai-agent-usage-monitor?label=latest%20release)](../../releases/latest)
[![Licence: MIT](https://img.shields.io/badge/licence-MIT-blue)](LICENSE)
[![Windows 11 x64](https://img.shields.io/badge/Windows%2011-x64-0078D4)](#-limitations)
[![No telemetry](https://img.shields.io/badge/telemetry-none-brightgreen)](#-is-it-safe)

> 📦 The repository is called `ai-agent-usage-monitor`; the app calls itself **AI Usage Monitor**. Same thing.

---

## 🚀 Quickstart

Three steps, about a minute. No installer, no admin rights, no setup wizard.

**1. Download** ⬇️ — grab `AiUsageMonitor-v<version>-win-x64.exe` from the [latest release](../../releases/latest).

**2. Run it** ▶️ — just double-click. Windows will show a **"Windows protected your PC"** warning the first time, because the file isn't code-signed. Click **More info → Run anyway**. ([Why, and what to check instead ↓](#windows-will-warn-you-and-here-is-why))

**3. That's it** ✅ — the widget appears with a card per provider, and an icon lands in your notification area. Press **`Ctrl+Alt+Q`** any time to bring it back.

That one file carries its own .NET runtime, so there is nothing else to install. 🎒

<details>
<summary>🚫 My network blocks <code>.exe</code> downloads</summary>

<br>

Take `AiUsageMonitor-v<version>-win-x64.zip` from the same release instead — it holds the identical executable plus its checksum file, built in the same release run. Extract both and carry on.

If your filter also inspects inside archives, it will block this too. That's the filter doing its job, and the answer is your IT department, not a workaround. 🙂

</details>

---

## 🔒 Is it safe?

Short answer: **yes — and you don't have to take our word for it.** This section exists so you can check rather than trust.

### 🌐 Everything that leaves your machine

Exactly three things. That's the whole list.

| Request | Goes to | Carries |
|---|---|---|
| 📈 Your Codex quota | Codex's own `app-server`, **on this machine** | Nothing — it never leaves the PC |
| 📈 Your Claude Code quota | `api.anthropic.com` (Anthropic's own server) | The OAuth token Claude Code already stored, over TLS |
| 🔔 Update check, once a day | `api.github.com` | **Nothing.** No version, no ID, no machine info |

The update check is an anonymous `GET` of a public release listing. Its `User-Agent` is the constant string `AiUsageMonitor` — deliberately carrying no version — and there is no query string, no identifier and no token. The comparison happens on your computer. Switch it off in **Settings → Updates** and no unattended request is ever made; the *Check now* button still works when you press it. The app never downloads or installs anything either — when a new version exists, it offers to open the release page in your browser. 🧭

**Prefer to read it yourself?** Every network call in the shipped app lives in exactly two files:
[`ClaudeOAuthUsageProbe.cs`](src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs) and
[`GitHubReleaseClient.cs`](src/AiUsageMonitor.Infrastructure/Updates/GitHubReleaseClient.cs). There are no others.

### 🙅 What it never does

- ❌ No telemetry, analytics or crash reports — **nothing is ever sent to the author**
- ❌ No website scraping, no browser automation, no cookies or browser profiles
- ❌ No redirect-following — the one permitted destination is hardcoded
- ❌ Never writes, refreshes, rotates or modifies your credentials — token lifecycle stays your provider's job
- ❌ Never touches Claude Code's or Codex's own config files
- ❌ No administrator rights, no service, no driver, no scheduled task
- ❌ No history, no database, no cloud account, no sign-in

### 🔑 About that Claude Code token

It is read from `%USERPROFILE%\.claude\.credentials.json` — the file Claude Code itself already wrote — held in memory just long enough to build one `Authorization` header for one HTTPS request, and then dropped. It is **never** logged, saved, cached, shown on screen, copied to the clipboard, or included in any diagnostic output. 🔐

### 📁 Where it puts things

| Path | Contents |
|---|---|
| `%APPDATA%\AiUsageMonitor\settings.json` | Your settings. Plain JSON, hand-editable. |
| `%LOCALAPPDATA%\AiUsageMonitor\logs` | Rolling log, 1 MB × 5 files. |
| `HKCU\...\Run` | One entry, and **only** if you switch on *Start with Windows*. |

Nothing is written machine-wide. **To uninstall:** switch off *Start with Windows*, delete the `.exe`, delete those two folders. Done — there's nothing else of ours on your PC. 🧹

### Windows will warn you, and here is why

The executable is **not code-signed**, so SmartScreen shows *"Windows protected your PC"* on first run. This is identical whether you downloaded the `.exe` or extracted it from the `.zip` — Windows carries the download mark through extraction, so the zip is not a way around the warning.

A signing certificate costs money every year and requires a verified publisher identity this project doesn't have. So instead of asking you to shrug and click through, **every release publishes a SHA256 next to the binary** — verify it takes ten seconds:

```powershell
Get-FileHash .\AiUsageMonitor-v<version>-win-x64.exe -Algorithm SHA256
```

Compare that against the matching `.sha256` file — on the release page if you downloaded the `.exe`, or the copy extracted from the `.zip`. **If they differ, don't run it.** ⚠️

🛡️ Found a way to break any of the above? See [SECURITY.md](SECURITY.md) and report it privately — never as a public issue.

---

## 🔌 Providers

| Provider | What it reads | Tier | Verified against |
|---|---|---|---|
| 🟢 **Codex** | `codex app-server` over stdio, JSON-RPC `account/rateLimits/read` | ✅ **Official** | codex-cli 0.144.6 |
| 🟣 **Claude Code** | Anthropic's own usage endpoint, authenticated with the OAuth token Claude Code already stored on this machine | ⚠️ **Unofficial** | Claude Code 2.1.226 |

> ⚠️ **The Claude Code mechanism is undocumented and may stop working without notice.** It is not a published API and carries no stability guarantee. The app labels it *Unofficial* everywhere it appears, and when it breaks the card goes to a plain error state — it never falls back to a stale number, an estimate, or a zero. **If you see a number, a provider returned it.**

You don't need both. A provider that isn't installed simply shows **Not installed** and costs nothing. 🤷

---

## 🖥️ Using it

- 🃏 **Cards** — one per provider: name, version, mechanism tier, connection state, and a bar per quota window with its usage and a countdown to reset.
- ❎ **Closing hides it.** The close button, `Alt+F4` and the system menu all hide the widget to the notification area. Only **Exit** in the tray menu ends the process.
- 📍 **The tray icon is a live readout** — a miniature of the bars, redrawn as the state changes, coloured to stay legible against your taskbar rather than against the app's theme.
- ⌨️ **`Ctrl+Alt+Q`** brings the widget back from anywhere. Can be switched off.
- 📌 **Pin** keeps it above other windows; unpinned, it hides when you click away.
- 🤏 **Compact** tightens everything; **Mini** shrinks it to a docked strip.
- 🔔 **Notifications** fire as you cross usage milestones, and when a provider fails or recovers. An alert never says *why* something failed — that stays on the card.

---

## ⚙️ Settings

Everything applies immediately. There is no OK, no Apply, no restart. ⚡

| | |
|---|---|
| 🎨 **Appearance** | Theme (Light / Dark / High contrast / follow Windows), density, colour bars by usage |
| 🪟 **Window** | Always on top, mini mode and its dock edge, global hotkey, start with Windows, reset window position |
| 🔌 **Providers** | Order, visibility, and a per-provider refresh interval |
| 🔄 **Refresh** | Refresh interval (default 2 min) and the age at which data is called stale (default 5 min) |
| 🔔 **Notifications** | On or off, which usage thresholds alert, and quiet hours |
| 🩺 **Diagnostics** | Everything the app knows about each provider, plus a redacted copy button |

⏱️ **The refresh interval will not go below 120 seconds**, globally or per provider. Asking a provider for your quota far more often than it actually changes is how an app gets throttled — and a throttled provider tells you *less*, not more. A hand-edited smaller value in `settings.json` is read as 120 and left alone in the file.

---

## 🩺 Something's wrong?

**Settings → Diagnostics → Copy** puts a plain-text report on your clipboard: what each provider reported, when, how long it took, what's scheduled next, and why.

Your user folder and user name are replaced with `%USERPROFILE%` and `%USERNAME%` before it's copied, and **no credential is read into that screen at all** — so none can leak through it. 🧼

Please include that report when [opening an issue](../../issues). 🙏

---

## 🚧 Limitations

Worth knowing before you download:

- 🪟 **Windows only.** WPF and Windows-specific APIs throughout; there is no macOS or Linux build.
- 💻 **x64.** It runs under emulation on Windows on ARM; there's no native ARM64 build.
- 🕰️ **No history.** Every value is instantaneous. Nothing is recorded, charted or kept between runs.
- ⚠️ **The Claude Code mechanism is unofficial** and can break whenever the endpoint changes.
- 🧩 **Quota windows are whatever the provider reports.** Their number, names and durations are discovered at runtime, not hardcoded — so a provider adding a new window shows up without an app update, and one whose name the app doesn't recognise is displayed with the provider's own raw label rather than a guess.
- ⏳ Reset times and window durations appear only where the provider actually reports them. Where it doesn't, the app says so instead of inferring.
- 🧪 **Pre-1.0.** It works, it's used daily, and the version number stays honest about depending on an unofficial endpoint.

---

## 🛠️ Building from source

Requires the .NET 10 SDK (the feature band is pinned in `global.json`).

```powershell
dotnet build                                   # warnings are errors
dotnet test                                    # domain, infrastructure and app suites
dotnet run --project src/AiUsageMonitor.Poc    # console probe against your real installs
powershell -File build/publish.ps1             # the single-file release artifact
```

`build/publish.ps1` produces the self-contained executable and stages it with its checksum under `artifacts/`. The release artifact is **always** the self-contained build — the framework-dependent one is a few hundred kilobytes, runs fine on a machine that already has the .NET 10 Desktop Runtime, and fails everywhere else.

📓 `docs/provider-capability-findings.md` is the verification record behind the provider table above: how each mechanism was proven, the wire formats and schemas observed, the version behaviour, and the mechanisms that were investigated and rejected for carrying no usable quota signal.

---

## 📄 Licence

[MIT](LICENSE). Use it, fork it, ship it.

---

Built by [nayots](https://nayots.com). 🪶
