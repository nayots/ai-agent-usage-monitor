# AI Usage Widget

## Product Requirements Document

### 1. Project Overview

AI Usage Widget is a native Windows desktop utility that presents the current usage and quota state of locally installed AI coding tools in a compact, always-available interface.

Version 1 supports:

- Claude Code
- OpenAI Codex

The application is an observer. It shows provider-reported usage information without changing subscriptions, bypassing limits, automating authentication, or sending data outside the user’s machine.

The product must be a Windows-only WPF application built for developers who use one or both providers and want to understand their available capacity without interrupting their work.

The widget must prefer official local integrations exposed by installed provider tools, and may rely on a verified, clearly labelled, provider-owned unofficial mechanism only when no official mechanism can deliver the required capability, subject to the Unofficial Mechanism Policy in §4.1.1. It must not scrape websites, read browser cookies or browser profiles, or infer quota values from unsupported sources.

### 2. Product Goals

The application shall:

- Show usage and reset information for Claude Code and Codex in one desktop widget.
- Discover provider capabilities before relying on them.
- Represent quota windows generically rather than assuming fixed periods such as five-hour or weekly limits.
- Display every quota window that a provider exposes through a verified mechanism, official or unofficial, that the application can confirm.
- Clearly communicate provider availability, connection state, freshness, and errors.
- Make usage understandable within a few seconds.
- Run locally with minimal CPU, memory, and network impact.
- Preserve the user’s existing provider configuration.
- Provide diagnostics that make failures understandable without exposing private data.
- Remain maintainable when either provider changes its available quota windows, labels, or local integration behavior.

### 3. Out of Scope

Version 1 does not include:

- Managing subscriptions, billing, plans, invoices, or account settings.
- Purchasing, upgrading, or renewing provider plans.
- Sending prompts, code, repository contents, or usage data to third parties.
- Cloud synchronization, analytics, telemetry, or remote accounts for this application.
- Website scraping, browser automation, or cookie and browser-profile inspection.
- Circumventing, estimating, or attempting to bypass provider limits.
- Displaying historical usage charts or long-term trends.
- Notifications beyond the quota events named in §16.2. Scheduled digests, per-window thresholds the user configures, and notifications carrying anything other than a window's own label and reading remain out of scope.
- Support for macOS, Linux, mobile platforms, or web browsers.
- Support for providers other than Claude Code and Codex.
- A desktop pet or gamified visual layer.
- A plugin marketplace or provider extension system.

Reading a provider's own locally-stored authentication token, for the signed-in user's own account, solely to query that same provider's own usage API, is in scope when no official mechanism can deliver equivalent capability, subject to the Unofficial Mechanism Policy in §4.1.1. Transmitting data to any party other than the provider itself remains out of scope regardless of mechanism.

### 4. Guiding Principles

#### 4.1 Verified integrations, official first

Official, documented, or provider-supported local mechanisms are always preferred and must be used whenever they can deliver the required capability. An undocumented, provider-owned mechanism may be used only when no official mechanism can deliver the required capability, and only if it is:

- Verified by discovery at runtime rather than assumed.
- Clearly labelled as unofficial everywhere it surfaces in the application.
- Designed to fail safe into an explicit state rather than fabricate data if it breaks.
- Restricted to the provider's own first-party host.

If neither an official mechanism nor a qualifying unofficial mechanism is available, the application must show that limitation rather than inventing a workaround.

##### 4.1.1 Unofficial Mechanism Policy

Every provider mechanism must carry a tier: **Official** or **Unofficial**.

- The tier must be visible in the provider card and in diagnostics. A value obtained through an unofficial mechanism must never be presented as official.
- Unofficial mechanisms carry no stability guarantee and may break without notice. The application must detect breakage and degrade to an explicit state rather than fail silently or fabricate data.
- When an official mechanism exists that provides equivalent capability, it must be preferred automatically over an unofficial one.
- A credential obtained from a provider's local store must be used only in-memory, only against that provider's own first-party host over TLS, and must never be logged, persisted, copied, or displayed. It must never be sent to any destination other than that provider's own first-party host.
- No credential may be refreshed, rewritten, or invalidated by this application. Token lifecycle remains entirely the provider's responsibility.

#### 4.2 Discovery over hardcoding

The application must prefer capability discovery, provider-reported metadata, and structured responses over hardcoded assumptions.

The code must not internally encode assumptions such as:

- Claude Code always has exactly two quota windows.
- A quota window is always five hours or seven days.
- Codex always exposes a fixed subscription model.
- A provider always supports push updates.
- A provider version has a stable response schema.

Provider-specific display labels may be used only as optional presentation enhancements. The underlying model must preserve the provider’s reported identity and values.

#### 4.3 Accurate before attractive

A polished interface is important, but accuracy and transparency are more important. The widget must never present an estimated, stale, partial, or unavailable value as current without a visible qualifier.

#### 4.4 Local-first and private by default

The application must work entirely on the local computer. It must collect and retain only the information necessary to render the widget, settings, and diagnostics.

#### 4.5 Graceful partial functionality

Failure of one provider must not make the other provider unavailable. A missing integration, unavailable CLI, unsupported version, or temporary error must be isolated to its provider card.

#### 4.6 Calm desktop behavior

The widget should be informative without becoming distracting. It must avoid flashing, intrusive modal dialogs, noisy notifications, unnecessary polling, and visual effects that obscure usage information.

### 5. Success Criteria

The first release is successful when all of the following are true:

- The application detects whether Claude Code and Codex are installed through supported local discovery.
- The application displays the installed version of each detected provider when that version can be obtained through an official local mechanism.
- Each provider card accurately shows its connection state.
- Each provider card displays all verified quota windows exposed through an official or unofficial mechanism.
- Unknown quota window names remain visible rather than being discarded.
- The UI adapts to one, two, or many quota windows without redesign.
- Usage percentages, reset timestamps, and countdowns match provider-reported values when available.
- Every shown value includes an understandable freshness state.
- Stale values remain visible only when clearly marked as stale.
- A provider integration can fail, recover, or become unavailable without crashing the application.
- The widget remains responsive during provider discovery and refresh.
- No provider credential is read except to authenticate to that same provider's own first-party usage API on the user's behalf, and no cookies, browser profiles, prompts, repositories, or source code are read directly.
- No application data leaves the computer.
- Diagnostics allow a user or developer to understand installation, version, capability, freshness, and error status without revealing secrets.

### 6. Discovery and Verification

Discovery is a first-class product capability, not a one-time setup step.

Before implementing provider-specific behavior, the project must verify the current capabilities of the installed Claude Code and Codex versions. Verification must use official documentation, official local commands, supported local protocols, provider-supported integration points, or, when no official source exists, direct empirical verification of a provider-owned mechanism carried out under the Unofficial Mechanism Policy in §4.1.1.

The implementation must record the following discovery results for each provider:

- Installation status.
- Executable or supported local integration availability.
- Provider version.
- Supported local communication mechanism.
- The tier, Official or Unofficial, of each discovered mechanism.
- Supported request and response schema version, when available.
- Whether quota information is available.
- Whether updates are push-based, pull-based, or unavailable.
- The quota windows returned by the provider.
- The fields available for each quota window.
- The timestamp of the most recent verified discovery.
- Any unsupported, malformed, or unavailable capability.

When more than one verified mechanism can satisfy a required capability, discovery must prefer the official mechanism.

Discovery must run:

- On first application launch.
- When the user selects **Re-run Discovery**.
- After an application upgrade that changes provider integration code.
- After a provider-related error that indicates a possible version or schema change.
- When a detected provider executable or integration endpoint changes.
- At a controlled interval only if needed to detect provider upgrades without excessive background work.

Discovery must never:

- Request elevated privileges.
- Modify provider installation files.
- Modify provider authentication settings.
- Replace existing user configuration.
- Read cookies, browser profiles, or web session storage.
- Read a provider's token store or credential file except as permitted under §4.1.1 to authenticate to that same provider's own first-party usage API.
- Treat undocumented observed behavior as a supported contract.

### 7. Design-First Workflow

Before implementation begins, the team must generate and review a Claude Design prompt that defines the visual direction for the WPF application. The prompt is maintained at `docs/design/design-prompt.md`.

The design must be derived from the verified provider behavior recorded in `docs/provider-capability-findings.md`. It must not be derived from assumptions about plan periods, quota counts, or field availability. Verification demonstrated that a live account exposes an undocumented quota window carrying a usage percentage but no reset time, and that a provider may expose a single window while another exposes three. A design that assumes a fixed set of fully populated windows is invalid before it is drawn.

#### 7.1 Required layout coverage

The design brief must cover:

- Default widget layout.
- Compact widget layout.
- Expanded provider-card layout.
- Light and dark themes.
- Connection-state indicators.
- Stale-data presentation.
- Progress bars, both with and without elapsed-time markers.
- Settings and diagnostics layouts.
- System tray behavior.
- Typography, spacing, colors, iconography, and accessibility.
- Multi-monitor and high-DPI behavior.

#### 7.2 Required state coverage

The design is incomplete unless it presents each of the following. Every item is a state the verified provider data can already produce, not a hypothetical.

1. **Complete quota row** — display label, usage percentage, progress bar, elapsed-time marker, and reset countdown.
2. **Partial quota row** — a provider-supplied label and a usage percentage only, with no reset countdown and no elapsed-time marker. This state must read as deliberate and complete, not as a row that failed to load.
3. **Progress bar without an elapsed-time marker** — the marker is the exception rather than the default, because it may be shown only when the window duration is verified per §16. The unmarked bar is the baseline form and must be designed as such.
4. **Elapsed-time marker at both extremes** — at 0% and at 100% of the window. At 100% the marker must not collide with or be obscured by the bar's end cap, and it must remain distinguishable from the fill edge when marker and fill coincide.
5. **Exhausted quota window** — 100% used with a provider-reported rate-limit-reached indication. This must be visually distinct from a merely very full bar, and must promote reset timing to the primary information in the row.
6. **Usage far ahead of elapsed time** — a window at 100% used with roughly a quarter of the window elapsed. This was the verified Codex state and is the clearest demonstration of why the marker exists.
7. **Mechanism tier badge** — Official or Unofficial, on every provider card and in diagnostics, as required by §4.1.1. A value obtained unofficially must never be presented as official.
8. **Mechanism unavailable** — a prominent state for a provider whose sole mechanism has stopped working. Claude Code has no fallback per §11, so this state must communicate an unrecoverable condition and must be distinct from stale data.
9. **Every connection state defined in §10**, including Not Installed, Discovering, Waiting, and Unsupported.
10. **Unknown quota label treatment** — a window name that resolves to a known duration is rendered as a human-readable label; a name that does not resolve is rendered as the provider's literal token, typographically distinguished so it cannot be mistaken for a label this application understands. This satisfies §13's requirement to display an unfamiliar provider-supplied name, and is the sole exception to §15's prohibition on opaque provider fields in the default card; it does not license displaying protocol names, method names, or wire-format field names anywhere in the widget. The provider-supplied identifier must remain available in a tooltip and in diagnostics for every window.
11. **Asymmetric providers** — a card exposing one quota window beside a card exposing three, plus a card exposing none and a card exposing six. The layout must not assume that providers have equal window counts, nor that any count is stable across accounts, plans, or time.
12. **Stale presentation** as defined in §18.

#### 7.3 Design-to-data contract

The design must not require any value that a verified provider mechanism does not supply. Every field other than the usage percentage is nullable and has been observed null in practice.

Consequently:

- No layout may reserve space that only looks correct when fully populated.
- No element may be rendered from a fabricated, estimated, or inferred value when the provider did not supply it. Absence is shown as absence.
- Windows must not be sorted, grouped, or laid out on an assumed duration. Verification recorded a seven-day window resetting sooner than a five-hour window on the same account.
- Context-window fill is not subscription quota and must never appear as a quota row.

#### 7.4 Prompt requirements

The Claude Design prompt must request a restrained native-Windows utility aesthetic: compact, readable, calm, and appropriate for a developer desktop. It must explicitly avoid dashboard clutter, decorative illustrations, gamification, and ambiguous color-only status signals.

The prompt must additionally:

- Supply concrete fixture snapshots taken from the verification record, and state which figures are verified and which are illustrative.
- State the deliverable format, which is a single self-contained HTML page presenting every required state, accompanied by a token document mapping colors, spacing, radii, and type styles to proposed WPF resource keys.
- Forbid projected-exhaustion language, per §16.
- Forbid displaying any credential, token, session identifier, or user-identifying path in any mockup, including diagnostics mockups.

#### 7.5 Status of the approved output

The approved design output is a required input to implementation. Engineering may adapt details required for WPF accessibility, performance, or platform conventions, but must preserve the approved information hierarchy and state communication.

The design output is a reference, not a source of shipping code. No generated markup or styling is compiled into the application.

The approved output was accepted on 2026-08-11 and is recorded in `docs/design/`: `widget-states.html` renders every state required by §7.1 and §7.2, `tokens.md` carries the token layer with measured contrast ratios, and `rationale.md` records the hierarchy decisions and the conflicts between requirements. The `.dc.html` files are the editable sources; `README.md` explains which file to read for what.

The design raised two departures from `docs/design/design-prompt.md`. Both are **accepted**:

- **Colouring quota bars by usage band** departs from the brief's "at most one accent hue" restraint rule. It is adopted as a user-controlled setting, bounded by §16.1 and listed in §19.
- **The brief's 360×420 size target is superseded** by the measured design: 360×410 default and 360×326 compact at 100% scaling, with the provider list scrolling above 520. The difference was spent on the column captions and duration provenance, which the design moved into the expanded provider card rather than shrinking type below 11px or dropping a quota window. Implementation targets the measured values in `tokens.md` §3.

### 8. Generic Provider and Quota Model

The application must use a provider-neutral domain model.

A provider represents a locally discoverable AI coding tool with a name, version, connection state, discovered capabilities, refresh metadata, diagnostics, and zero or more quota windows.

A quota window represents a provider-reported usage allowance with a stable internal identifier when available, a display label, current usage or remaining capacity, optional reset information, optional window-start information, freshness metadata, and source confidence.

The model must support:

- Percentage used, percentage remaining, or raw values when a provider exposes only one of these.
- Known and unknown quota labels.
- A reset timestamp, reset countdown, or neither.
- A window start timestamp when available.
- Multiple limits within a provider.
- Provider-defined ordering.
- Optional provider-specific metadata kept outside the shared UI model.
- Partial data without fabricating missing fields.

The generic quota model must not contain properties named after current provider plan periods such as `FiveHourQuota` or `WeeklyQuota`.

## Provider Capability Verification

A provider is eligible for usage display only after the application has verified a supported local path to the relevant information.

For each provider, verification must distinguish between:

- Installed but not yet interrogated.
- Installed and supported by the current application version.
- Installed but unsupported by the current application version.
- Installed but unavailable because the local integration is inactive.
- Installed and available, but not currently authenticated or reporting usage.
- Available and returning valid usage data.
- Available but returning malformed, incomplete, or incompatible data.

Verification must be implemented as capability checks, not version-number allowlists. A provider version may be shown as unsupported only when the application cannot complete the supported discovery or usage operation.

If a provider exposes a machine-readable capability description, schema version, feature list, or rate-limit metadata, that information must take precedence over assumptions in application code.

When a provider reports fields that are unknown to the application:

- Preserve safe displayable data.
- Record the unknown fields in diagnostics only when they contain no secrets.
- Avoid treating unknown data as an error solely because it is new.
- Avoid inventing meanings for undocumented fields.
- Render the affected quota with a neutral fallback presentation where possible.

### 9. Functional Requirements

The widget must provide a concise at-a-glance view of every verified provider and quota window.

For each provider, the application must show:

- Provider name.
- Provider icon or textual identifier.
- Installed version, if discoverable.
- Current connection state.
- Last successful update time.
- Freshness or stale state.
- Zero or more dynamically discovered quota windows.
- A localized, understandable error state when data cannot be obtained.

The widget must remain useful when only one provider is installed or connected.

The application must not show fabricated quota data. When no supported quota data is available, the provider card must explain the state and offer an appropriate next action, such as re-running discovery or opening diagnostics.

### 10. Provider Connection States

Each provider must expose one current state from the following baseline set:

- **Not Installed**: the provider cannot be discovered through its supported local installation path.
- **Discovering**: provider capability discovery is in progress.
- **Waiting**: the provider is installed but has not produced usable quota data.
- **Connected**: valid quota data has been received within the freshness threshold.
- **Stale**: previously valid data exists but is older than the freshness threshold.
- **Unavailable**: the provider is installed but its supported local integration cannot currently be reached.
- **Unsupported**: the installed provider cannot be integrated through a verified supported mechanism.
- **Error**: a recoverable or non-recoverable integration error occurred.

The visual treatment must use text, iconography, and color together. Color alone must never communicate state.

### 11. Claude Code Integration

Claude Code integration must use a single verified mechanism, confirmed by the discovery phase for the installed version before it is relied upon.

- **Sole mechanism — Unofficial tier**: query the provider's own usage endpoint, authenticated with the OAuth access token that Claude Code itself writes to its local credential store, restricted to that provider's own first-party host. This mechanism is pull-based and may be queried on demand, so it does not go stale merely because no session is running. It must be verified by discovery at runtime, labelled as unofficial everywhere it surfaces per §4.1.1, and must fail safe into an explicit state rather than fabricate data if it stops working.

There is no official fallback mechanism for Claude Code. If the sole mechanism cannot deliver current data, the Claude Code provider must enter an explicit **Unsupported** or **Error** state as defined in §10, and must never fabricate, estimate, or infer a quota value to fill the gap.

#### 11.1 statusLine JSON Contract — Evaluated and Rejected

The documented statusLine JSON contract was evaluated as a candidate mechanism and rejected. It is not used by this application, as a fallback or otherwise, and must not be reintroduced without a new product decision. It was rejected because:

- It is push-only and event-driven: data is populated only while an interactive Claude Code session is active, and it never fires under non-interactive invocation (`-p`/print mode).
- Capturing it requires a user-approved modification of the user's existing statusLine configuration, an intrusive precondition that a pull-on-demand mechanism does not need.
- Data obtained this way is stale whenever no Claude Code session is currently running, which is the common condition for a persistent desktop widget.

This decision may be reconsidered if the unofficial usage endpoint ceases to function and no other verified mechanism is available.

The implementation must not assume a specific configuration location, quota count, quota period, account tier, or response schema for the sole mechanism.

The Claude integration must:

- Detect whether Claude Code is installed.
- Obtain and display the installed version when officially available.
- Verify the supported mechanism before relying on it.
- Receive or request usage information only through the verified sole mechanism.
- Discover all available quota windows from returned provider data.
- Preserve provider-reported quota labels and ordering when possible.
- Report whether updates are pull-based or unavailable.
- Report the tier of the mechanism currently supplying data.
- Preserve existing user configuration and never overwrite it silently.
- Treat missing usage information as a waiting or unavailable state, not as zero usage.
- Retain the last valid usage snapshot only while clearly marking it stale when necessary.

If integration ever requires optional user configuration, the application must:

- Explain the required change before performing it.
- Preview the exact intended change.
- Preserve unrelated user settings.
- Create a recoverable backup before changing an existing configuration file.
- Offer a restore action.
- Verify the result after the change.
- Stop and present diagnostics if safe modification cannot be guaranteed.

### 12. Codex Integration

Codex integration must use an official, locally supported Codex interface verified during discovery.

The implementation must not assume a fixed number of rate limits, a fixed reset period, a fixed account type, or a stable undocumented response shape.

The Codex integration must:

- Detect whether Codex is installed.
- Obtain and display the installed version when officially available.
- Verify the supported local protocol, command, or provider interface before use.
- Discover all returned rate-limit or quota windows dynamically.
- Preserve provider ordering when it is meaningful and available.
- Map provider data into the generic quota model without encoding plan-specific assumptions.
- Distinguish absent quota information from an exhausted quota.
- Support event-driven updates when officially available.
- Use lightweight periodic refresh only when an official event mechanism is unavailable or insufficient.
- Recover after Codex restarts, upgrades, or temporary local communication failures.

### 13. Dynamic Quota Discovery

For both providers, the application must discover quota windows from verified provider output.

Each discovered quota window should capture, when available:

- Provider-supplied identifier.
- Provider-supplied display name.
- Provider-supplied ordering or priority.
- Used amount, remaining amount, or percentage.
- Unit of measurement.
- Reset timestamp.
- Window start timestamp.
- Duration or period metadata.
- Model, plan, or scope metadata.
- Source timestamp.
- Whether the value is complete, partial, or unavailable.

The application must derive a friendly label only when doing so does not obscure provider meaning. If a provider returns an unfamiliar quota name, the widget must display the provider-supplied name.

The application must render additional discovered quota windows automatically. A new window must not require a code change unless its data cannot be represented safely by the generic model.

### 14. Refresh and Freshness

Provider communication must be separate from local display updates.

The application must update reset countdowns locally without querying a provider every second.

A successful provider refresh must record:

- The local receipt timestamp.
- The source timestamp, if provided.
- The provider version.
- The discovered capability set.
- The complete or partial status of the snapshot.

Freshness thresholds must be configurable per provider integration and conservative by default. A value becomes stale when it exceeds the applicable threshold or when the application knows the source is no longer active.

Stale data must remain visually distinct from current data and must display the age of the most recent successful update.

The widget must never silently replace stale data with an empty or apparently current value.

### 15. User Interface Requirements

The default experience must be a compact desktop widget suitable for persistent placement on a Windows desktop.

The interface must prioritize, in order:

1. Current remaining capacity.
2. Reset or availability timing.
3. Provider connection and freshness state.
4. Provider version and supporting metadata.
5. Detailed diagnostics and configuration.

The default widget must contain one provider card per discovered provider. A provider card must remain visible when the provider is unavailable, unless the user explicitly hides unavailable providers in settings.

Each provider card must include:

- Provider name and icon.
- Connection-state indicator and accessible text.
- Provider version when available.
- Last updated time or stale age.
- A quota list that grows or shrinks based on discovered data.
- An understandable empty, waiting, unsupported, or error state when quotas cannot be shown.

Each quota row must include, when the provider supplies the relevant values:

- Quota display label.
- Remaining percentage or equivalent usage measurement.
- A progress bar.
- An elapsed-time marker when the quota start and end boundaries are known.
- Reset countdown.
- Reset timestamp in an accessible tooltip or expanded view.
- A partial-data indicator when only some fields are available.

The default card must avoid displaying implementation-specific identifiers, raw protocol names, or opaque provider fields.

### 16. Progress Bars and Elapsed-Time Markers

The filled region of a quota progress bar represents used capacity by default. The visible percentage text must make the direction explicit, such as “72% remaining” or “28% used.”

When the provider exposes a known quota-window start and reset time, the progress bar must include a thin elapsed-time marker.

The elapsed-time marker represents the proportion of the known quota window that has passed:

- At the beginning of the window, the marker is at the start.
- At the reset boundary, the marker is at the end.
- It advances locally as time passes.
- It must not require additional provider calls.
- It must be omitted when the window start or duration cannot be verified.

The marker allows the user to compare consumption with elapsed time:

- Usage behind the marker suggests consumption is below an even time-based pace.
- Usage near the marker suggests consumption is approximately tracking elapsed time.
- Usage ahead of the marker suggests consumption is being consumed faster than time is passing.

This comparison is informative only. The UI must not claim that a user will run out of quota, calculate projected exhaustion, or imply a provider-defined safe usage rate.

The marker must remain visually secondary to the primary usage fill, meet contrast requirements, and remain visible in light, dark, high-contrast, and scaled display modes.

#### 16.1 Bar tone by usage

A quota bar may take its fill color from the band its reported percentage falls in: one tone below 75%, a distinct tone from 75% through 99%, and the exhausted tone at 100%. This is controlled by the **Color bars by usage** setting defined in §19, which is on by default.

This is the single accepted departure from the design brief's one-accent-hue restraint rule, per §7.5. It is bounded by all of the following:

- Exactly three fixed bands. Never a gradient, never an interpolated color. A continuously varying color communicates a rate, which this section forbids.
- The band boundaries are fixed thresholds on the reported percentage alone. They are not derived from the elapsed-time marker, the window duration, the time remaining, or any provider signal, and they assert nothing about whether a rate of consumption is safe.
- Color never carries a value by itself. Bar length and the written percentage remain the primary signals and tone only reinforces them. Where no percentage is written, such as the tray glyph, a shape overlay must carry what color would otherwise have to.
- The exhausted treatment at 100% is not part of this setting. A window at 100% renders as exhausted whether the setting is on or off, as required by §7.2 item 5.
- With the setting off, every bar below 100% uses the single accent fill.
- A band fill must not reuse a text-weight state color. Each band must clear 3:1 against both the bar track and the elapsed marker in both themes; `docs/design/tokens.md` §1 records the values that do.

The application's own spelling is en-US throughout, so the setting is named `ColorBarsByUsage` and labelled "Color bars by usage". The approved design render spells the label "Colour bars by usage"; this one-word difference is deliberate and is the only permitted copy divergence from that render.

#### 16.2 Quota event notifications

A widget that spends most of its life in the notification area cannot report anything by being looked at, so it may raise a notification-area balloon on a change worth interrupting for. This is controlled by the **Notify on quota milestones and resets** setting defined in §19, which is on by default. It is bounded by all of the following:

- **A fixed ladder, applied to every reported window.** The rungs are 10 through 80 in tens, then 85, 90, 95 and 100. They are thresholds on the reported percentage alone and are not derived from a window's duration, its countdown, or any provider signal. The ladder is applied to whatever windows a provider reports, in the provider's own order, and asserts nothing about how long any of them lasts — §7.3 forbids assuming a window's period, and a notification is not an exemption.
- **Edge-triggered, once per crossing.** A rung already crossed is never announced again until the reading falls back below it. The first reading observed for a window sets its rung silently, so opening the application does not announce where usage already stood.
- **Four quota events.** A rung crossed upward; the limit reached at 100%; a window that had reached 80% or more falling back below it; and a provider that had been answering ceasing to answer, or resuming. Nothing else notifies.
- **Only fresh data notifies.** A card that is not connected is not evaluated, so a stale reading never raises a notification and never advances a rung.
- **Missing data raises nothing.** A window reporting no percentage produces no notification, per §4.3 — never a 0% one.
- **A failure notification carries no reason.** It states that a provider stopped reporting and directs the user to the widget. No error text, response body, header, path or credential may appear in a notification, per §4.1.1 — a card is read deliberately, whereas a notification appears unbidden over whatever the user was doing.
- **Silent except at the limit.** Only the 100% notification uses the shell's default alert sound. §4.6 requires calm desktop behavior, and a sound every ten percent is the opposite of it; the moment work actually stops is the exception worth hearing.
- **The label is the provider's own.** A window whose name did not resolve is announced under its raw provider token, per §7.2 item 10, never under an invented name.
- **The setting gates delivery, not observation.** Rungs continue to advance while notifications are off, so switching them back on releases no backlog of crossings that have already happened.

Delivery uses the notification-area balloon the application's existing tray icon already owns. The Windows toast API is not used: it requires a Start-menu shortcut carrying a registered AppUserModelID, which the single self-contained executable required by §23 cannot install.

### 17. Widget Modes and Window Behavior

The application must provide:

- **Expanded mode**, showing all available provider metadata and quota windows.
- **Compact mode**, showing provider state, the most important visible quotas, remaining capacity, and reset countdowns.
- **Settings window**, for configuration and diagnostics access.
- **System tray integration**, allowing the widget to remain available without occupying taskbar space.

The widget must support:

- Dragging.
- Remembered position and size.
- Multi-monitor configurations.
- Windows display scaling and high-DPI monitors.
- Optional always-on-top behavior, offered as a pin on the title bar and nowhere else. It is session state, not a stored preference: the widget starts unpinned every time, because pinning answers what the user is doing now rather than how they want the widget to behave from here on. The settings window says where the pin lives so that removing it from there is not a disappearance.
- Standard window focus and keyboard behavior.
- Dismissal when the focus leaves the application: the widget's other windows close and the widget itself hides to the notification area, exactly as its close action does. Focus moving between the application's own windows — its settings window, its tray menu, its tooltips — is not a dismissal, and neither is the absence of a foreground window. A widget set to stay above other windows is exempt: that setting exists to keep it visible while another window is worked in, which a dismissal would defeat.
- Safe placement recovery when a previous monitor configuration no longer exists.

The system tray menu must provide:

- Open or focus widget.
- Refresh all providers.
- Open settings.
- Open diagnostics.
- Exit application.

### 18. Stale Data Presentation

A stale provider card must retain the last known valid data only when it improves user understanding.

Stale presentation must include:

- A visible stale state indicator.
- The age of the latest successful update.
- A statement that displayed values may no longer be current.
- A refresh action when refresh is supported.
- The error or availability reason when it can be shown safely.

Stale quota values must be visually de-emphasized without becoming unreadable. The widget must not continue animating provider-derived usage values after they become stale, except for locally calculated countdowns that are clearly associated with the last known reset timestamp.

### 19. Settings

Settings must be stored separately from provider configuration.

The settings experience must support:

- Start with Windows.
- *(Always on top is deliberately not here. It is session state, offered by the title bar's pin — see §17.)*
- Compact or expanded default mode.
- Light, dark, or system theme.
- Color bars by usage, on by default, bounded by §16.1.
- Notify on quota milestones and resets, on by default, bounded by §16.2.
- Refresh behavior where provider integration permits configuration.
- Stale-data threshold display and behavior.
- Whether unavailable providers remain visible.
- Window position and size reset.
- Re-run provider discovery.
- Open diagnostics.
- Open local logs.
- Restore backed-up provider configuration when the application has made an approved change.
- Reset application settings without modifying provider configuration.

Settings must clearly distinguish application-owned settings from provider-owned settings.

Changes that may affect provider integration must explain the impact before taking effect and must be reversible where practical.

### 20. Diagnostics

The diagnostics screen must help identify why a provider is not displaying usable quota data.

For each provider, diagnostics must show:

- Installation detection result.
- Detected executable or supported integration path, where safe.
- Provider version.
- Current connection state.
- Last discovery time.
- Last successful refresh time.
- Freshness state and age.
- Verified capabilities.
- Discovered quota-window names and safe metadata.
- Whether updates are event-driven, polling-based, manual, or unavailable.
- The mechanism tier currently in use, Official or Unofficial.
- The specific mechanism supplying data.
- Whether a first-party network call was made to obtain the current data, where applicable.
- The most recent safe error code or error message.
- A copyable diagnostic summary with secrets removed.

Application diagnostics must show:

- Application version.
- .NET runtime version.
- Windows version.
- Current theme and display scaling context.
- Logging status and log location.
- Last startup result.
- Whether the application is running with standard user privileges.

Diagnostics must never display:

- Authentication tokens.
- Cookies.
- Full provider configuration contents.
- Prompt text.
- Repository paths unless the user explicitly chooses to include them.
- Raw provider messages that may contain secrets.

### 21. Architecture Guidelines

The application must use a WPF-compatible MVVM architecture.

The presentation layer must contain views, view models, commands, value converters, and accessibility-oriented presentation logic.

The domain layer must contain provider-neutral models, quota models, capability models, connection states, freshness rules, and interfaces.

The infrastructure layer must contain provider integrations, supported local process or protocol adapters, configuration storage, logging, diagnostics collection, and Windows integration.

Provider-specific code must remain behind provider interfaces. The UI must not contain branches that interpret Claude-specific or Codex-specific quota semantics.

The architecture must support adding a provider in the future by implementing discovery, capability verification, snapshot retrieval, and state reporting without redesigning the shared quota UI.

### 22. Technical Requirements

The application must be implemented as a native Windows WPF application using C# and a supported .NET runtime.

The implementation should use:

- MVVM with testable view models.
- Dependency injection.
- Structured local logging.
- Asynchronous, cancellable provider operations.
- `System.Text.Json` or an equivalent supported serializer for structured local data.
- Windows-safe configuration storage.
- Native Windows accessibility APIs and automation properties where applicable.

Dependencies must be minimal, actively maintained, and justified by clear product value.

The application must require no administrator privileges for normal operation.

The application must fail safely when a provider integration cannot be verified, returning an explicit unavailable, unsupported, or error state rather than attempting unsupported fallback behavior.

### 23. Privacy and Security

The application must follow least-privilege and local-first principles.

It must never:

- Require administrator privileges.
- Read browser cookies, browser profiles, or web session storage, or read a provider credential for any purpose other than authenticating to that same provider's own first-party usage API on the user's behalf.
- Log, persist, cache, display, or copy a provider credential.
- Capture prompts, responses, source code, repository contents, terminal history, or clipboard data.
- Transmit usage data, diagnostics, or settings to any party other than the provider's own first-party host, and never for any purpose other than retrieving that user's own usage.
- Modify provider settings without explicit user approval.
- Execute arbitrary provider commands constructed from untrusted data.
- Log secrets or raw provider payloads without a verified redaction strategy.

The application may retain only the minimum local data required for its function:

- User-selected application settings.
- Last known safe usage snapshots.
- Discovery metadata.
- Error summaries.
- Rotated local diagnostic logs.

Any stored usage snapshot must remain on the user’s computer and must be removable through application settings.

Provider communication must occur only through a verified mechanism at the appropriate tier defined in §4.1.1. If a supported integration uses local inter-process communication, the application must validate messages, handle malformed data safely, and avoid trusting provider-provided text as executable instructions.

### 24. Reliability and Error Recovery

The application must remain stable during normal Windows and provider lifecycle events.

It must handle:

- Provider startup and shutdown.
- Provider upgrades or replacement.
- Temporary local communication failures.
- Invalid or incomplete provider responses.
- Windows sleep, hibernation, lock, and resume.
- Network changes when provider tooling requires network access for its own operation.
- Monitor removal, display scaling changes, and theme changes.
- Application restart after an unexpected shutdown.

Provider refresh operations must be asynchronous, cancellable, and bounded by timeouts.

Transient failures may be retried using measured backoff. Repeated failures must stop aggressive retries, move the provider to an understandable state, and allow manual refresh or re-discovery.

The application must preserve a valid last-known snapshot until it expires under the stale-data policy or the user clears it.

An error in one provider, one quota window, one display converter, or one diagnostic operation must not crash the process or prevent other providers from functioning.

### 25. Testing Requirements

Testing must verify both shared behavior and provider-specific integration behavior.

Unit tests must cover:

- Generic quota normalization.
- Percentage and raw-value formatting.
- Quota ordering and fallback labels.
- Reset countdown calculation.
- Elapsed-time marker calculation.
- Freshness and stale-state transitions.
- Connection-state transitions.
- Settings validation and migration.
- Secret redaction in diagnostics and logs.
- View-model behavior for empty, partial, stale, and error data.

Integration tests must use controlled provider adapters, fixtures, or official test mechanisms where available.

Integration coverage must include:

- Provider absent.
- Provider installed but unsupported.
- Provider installed but waiting for usable data.
- Valid single-quota response.
- Valid multi-quota response.
- New unknown quota window.
- Partial response without reset information.
- Malformed response.
- Provider version change.
- Temporary connection failure and recovery.
- Stale snapshot behavior.
- Cancellation and timeout behavior.

UI verification must include:

- Light and dark themes.
- High-contrast mode.
- Keyboard-only navigation.
- Screen-reader labels for state, quota, and freshness.
- High-DPI scaling.
- Multiple monitor placement.
- Compact and expanded modes.
- Long, unknown, and localized provider quota labels.
- One, two, and many quota windows.

Manual verification must be performed against current installed Claude Code and Codex versions before release. Any capability that cannot be verified with a supported local integration must be documented as unavailable rather than represented as implemented.

### 26. Implementation Workflow

Implementation must proceed in the following sequence:

1. Review official provider documentation and verify the available local integration mechanisms.
2. Record provider capability findings, schemas, limitations, and version behavior in repository documentation.
3. Generate the Claude Design prompt and obtain an approved design direction.
4. Create the WPF solution, shared domain model, MVVM foundation, local settings, and logging foundation.
5. Implement the provider-neutral quota, freshness, and connection-state models with automated tests.
6. Implement provider discovery without rendering quota values.
7. Implement each provider adapter behind the shared interface.
8. Verify dynamic quota discovery using controlled fixtures and supported live integrations.
9. Implement the widget, provider cards, progress bars, elapsed-time markers, and stale presentation.
10. Implement settings, system tray controls, diagnostics, and recoverable configuration changes.
11. Complete automated, accessibility, performance, and manual provider verification.
12. Produce release documentation describing supported provider versions, verified capabilities, and known limitations.

At every stage, implementation must prefer verified behavior over speculative compatibility.

If a provider changes before its integration is verified, the team must update discovery and diagnostics first, then adapt the provider adapter without changing the generic quota model unless genuinely required.

### 27. Definition of Done

Version 1 is complete only when:

- The Windows WPF application builds without warnings treated as errors.
- Claude Code and Codex discovery are implemented through verified local integrations, official or unofficial per §4.1.1.
- The application displays the version and capability state of each supported provider when available.
- Dynamic quota discovery works for both providers without internally relying on fixed five-hour, weekly, daily, or plan-specific fields.
- The widget supports zero, one, or many provider quota windows.
- Progress bars accurately communicate usage or remaining capacity.
- Elapsed-time markers appear only when their time boundaries are verified.
- Provider connection states and stale data are clear, accessible, and accurate.
- Settings, tray controls, and diagnostics function without exposing sensitive information.
- Existing provider configuration is preserved and any approved modification is backed up and reversible.
- The application works without administrator privileges.
- No website scraping, browser cookie access, browser-profile access, telemetry, analytics, or third-party data transmission occurs. First-party calls to a provider's own usage API, using that provider's own locally stored credential, are permitted only when verified, tiered, and labelled per §4.1.1.
- Automated tests pass.
- Manual verification succeeds against supported installed provider versions.
- Accessibility, high-DPI, multi-monitor, sleep/resume, and provider-recovery scenarios have been verified.
- Repository documentation explains setup, supported capabilities, limitations, diagnostics, and privacy behavior.

### 28. Future Enhancements

Future releases may consider:

- Historical quota trends and local usage charts.
- User-configurable notification thresholds, replacing the fixed ladder in §16.2.
- Per-provider refresh preferences.
- Provider ordering and visibility preferences.
- Exportable, redacted diagnostic bundles.
- Localization.
- Additional officially supported AI coding providers.
- A Windows Widgets or WinUI presentation layer.
- Optional desktop-pet visualization driven by the same provider-neutral model.
- Customizable widget layouts.
- Update discovery for the application itself.

Future work must preserve the same core boundaries: official mechanisms preferred with verified unofficial mechanisms used only where necessary per §4.1.1, local-first privacy, dynamic discovery, and no assumptions about provider quotas or authentication internals.