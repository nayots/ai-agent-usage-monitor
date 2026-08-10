# Claude Design Prompt — AI Usage Widget

> **Maintainer note, not part of the prompt.** This file satisfies PRD §7. Everything from *"## 1. What you are designing"* onward is the prompt; paste it verbatim into Claude Design. It is deliberately self-contained, so it repeats facts that also live in `docs/PRD.md` and `docs/provider-capability-findings.md`. When those documents change, update this one. Expected output lands in `docs/design/` as `widget-states.html` and `tokens.md`.

---

## 1. What you are designing

A persistent desktop widget for Windows 11 that shows live quota and usage for AI coding tools installed on the user's own machine — currently Claude Code and Codex. It sits on the desktop or in the system tray and is glanced at, briefly, in the middle of doing something else.

The audience is one developer looking at their own machine. The question they are answering is *"how much have I got left, and when does it come back?"* — asked in about two seconds, several times a day.

It is not an analytics product, not a dashboard, and not a report. It shows numbers a provider gave it and nothing else.

The application is built in WPF (.NET 10). Your output is a visual reference that an engineer translates into XAML by hand. No markup you produce is compiled or shipped.

## 2. Hard constraints

- **It must survive translation to WPF.** Do not let meaning depend on web-only capabilities. Nothing may rely on backdrop filters, CSS grid auto-placement subtleties, `:has()`, viewport units, or scroll-driven effects. Layouts should express as nested stacks and grids with explicit sizing.
- **Motion is near-zero.** A short cross-fade on state change is the maximum. No progress-bar easing, no pulsing, no shimmer, no attention-seeking animation of any kind. This thing sits on screen all day next to an editor.
- **Color never carries meaning alone.** Every state is communicated by icon, text, *and* color together. The design must remain fully legible in Windows high-contrast mode and to a user who cannot distinguish red from green.
- **Scaling.** Legible and correctly proportioned at 100%, 125%, 150%, and 200% Windows display scaling.
- **Contrast.** 4.5:1 for text, 3:1 for meaningful non-text elements, in both themes. State the measured ratio for the elapsed-time marker against both the bar fill and the bar track — that pairing is the one most likely to fail.
- **Size.** The default widget showing both providers should sit comfortably in roughly 360×420 px at 100% scaling without scrolling. Treat this as a target to design toward, and tell us if the content genuinely will not fit.

## 3. The data you are designing for

A **provider** is one locally installed AI coding tool. It has a name, a version, a connection state, a mechanism tier, a last-updated time, and **zero or more quota windows**.

A **quota window** is one usage allowance the provider reports. It may carry an identifier, a display label, a usage percentage, a reset time, a window duration, and a rate-limit-reached flag.

Three properties of this data drive the entire design:

1. **The number of windows is discovered, never assumed.** It differs per provider, per account, per plan, and over time. One provider currently reports one window; the other reports three. Neither number is stable.
2. **Window names are discovered too.** Some resolve to a duration this app understands. Some do not — a live account was observed reporting a window named `nimbus_quill`, which appears in no public documentation.
3. **Everything except the usage percentage is nullable, and every one of those fields has been observed null in production.** A window can arrive with a name and a percentage and nothing else.

So: a design that only looks right when every field is populated is wrong. The sparse row is not an edge case being handled defensively — it is a state that occurs on a normal account, today.

## 4. Fixture snapshots

Design against these. They are drawn from a real verification run against live accounts on 2026-08-10. Where a figure is illustrative rather than observed, it is marked.

### Fixture A — Claude Code, healthy, three windows

| | |
|---|---|
| Provider | Claude Code |
| Version | 2.1.226 |
| Mechanism tier | **Unofficial** |
| Connection state | Connected |
| Last updated | 12 seconds ago |

| Window | Used | Resets in | Duration | Marker? |
|---|---|---|---|---|
| `five_hour` | 47% | 3h 12m | 5 hours, inferred from the name | yes |
| `seven_day` | 92% | 4h 55m | 7 days, inferred from the name | yes |
| `nimbus_quill` | 34% | *none supplied* | *none supplied* | **no** |

*Verified:* the three window names, the 47% and 92% figures, and the total absence of reset and duration data on `nimbus_quill`. *Illustrative:* the 34%, and the exact countdowns.

Note that `seven_day` resets **sooner** than `five_hour`. That was the real observed data. Do not sort, group, or lay out windows on an assumed duration ordering.

`nimbus_quill` is the most important row in this brief. It must look like a window the app is faithfully relaying, not like a row that failed to load.

### Fixture B — Codex, exhausted, one window

| | |
|---|---|
| Provider | Codex |
| Version | 0.144.6, plan `plus` |
| Mechanism tier | **Official** |
| Connection state | Connected |
| Last updated | 4 seconds ago |

| Window | Used | Resets in | Duration | Elapsed |
|---|---|---|---|---|
| identifier `codex`, no label supplied | **100%** | 5d 08h | 7 days, supplied by the provider | ~24% |

The provider also reports that the rate limit has been reached.

*Verified:* the single window, 100% used, the 7-day provider-supplied duration, the rate-limit-reached flag, and the roughly one-quarter-elapsed position.

This row is the clearest argument for the elapsed-time marker: the bar is completely full while only a quarter of the window has passed. A plain progress bar cannot convey that. Design it so the gap between fill and marker is the thing the eye catches.

Note also that this provider has **one** window while Fixture A has three. Both cards appear in the same widget at the same time.

### Fixture C — Claude Code, mechanism unavailable

Tier Unofficial. The sole mechanism this provider has stopped returning usable data. There is no fallback — no second mechanism exists to fall back to. Last successful update was 2 hours ago.

This is not a network blip that will heal itself, and it must not be dressed as one. Design it to say plainly that the app can no longer read this provider, and let the user reach diagnostics from it.

### Fixture D — Codex, not installed

The tool is not present on the machine. The card stays visible, because the user may be about to install it. Nothing is fabricated in its place.

### Fixture E — a provider reporting zero windows

Installed, authenticated, reachable, and returning no quota windows at all. Not an error. Not zero usage. The card must say that no windows were reported without implying either.

### Fixture F — a provider reporting six windows

`five_hour`, `seven_day`, `seven_day_opus`, `seven_day_sonnet`, `extra_usage`, `nimbus_quill`. **Hypothetical** — these names appear in documentation but were not present on the verified account. Included solely to prove the card scales past three windows without redesign, and to force a decision about where scrolling or truncation begins.

## 5. Required screens and states

Produce all of the following.

**Screens**

1. Default widget, both providers, expanded — Fixtures A and B together.
2. Compact widget — same data, minimal height. Show what you cut first and be explicit about that priority order.
3. Expanded provider card — one provider, all metadata and all windows.
4. Settings.
5. Diagnostics.
6. System tray icon and its context menu.

**States** — each of these must appear somewhere, clearly labelled:

1. **Complete quota row** — label, percentage, bar, elapsed marker, countdown. (Fixture A, `five_hour`)
2. **Partial quota row** — provider label and percentage only, no countdown, no marker. Must read as deliberate and finished. (Fixture A, `nimbus_quill`)
3. **Bar with no elapsed marker** — the marker is shown only when the window duration is verified, so the unmarked bar is the *baseline* form. Design it as the default, not as a degraded variant of the marked one.
4. **Marker at both extremes** — at 0% and at 100% of the window. At 100% it must not collide with or vanish into the bar's end cap, and it must stay distinguishable from the fill edge when marker and fill land on the same pixel.
5. **Exhausted window** — 100% used *with* the provider reporting rate-limit-reached. Visually distinct from a merely very full bar. Reset timing becomes the primary information in the row. (Fixture B)
6. **Usage far ahead of elapsed time** — fill at 100%, marker at 24%. (Fixture B)
7. **Mechanism tier badge** — Official and Unofficial, on every provider card and in diagnostics. An unofficial value must never be able to pass as official. Keep it quiet enough to live on a card permanently, and clear enough to actually register.
8. **Mechanism unavailable** — prominent, distinct from stale, communicating that no fallback exists. (Fixture C)
9. **Each connection state** — Not Installed, Discovering, Waiting, Connected, Stale, Unavailable, Unsupported, Error.
10. **Unknown label treatment** — `five_hour` renders as a readable label; `nimbus_quill` renders as the provider's literal token, typographically distinguished so it cannot be mistaken for something the app understands. The raw identifier stays available in a tooltip for *every* window, known or not.
11. **Asymmetric providers** — a one-window card beside a three-window card (Fixtures A and B), plus the zero-window and six-window cards (Fixtures E and F).
12. **Stale data** — last known values retained but visibly de-emphasised, with the age of the last successful update, a statement that values may no longer be current, and a refresh action. Stale values must be de-emphasised without becoming unreadable.

## 6. Visual direction

**Restrained native-Windows utility.** Compact, readable, calm. It should look like it belongs beside Task Manager and an editor, not beside a SaaS analytics page.

**Shell — Windows 11 Fluent.** The window frame, tray menu, settings, and diagnostics follow the Windows 11 design language: rounded window corners, a layered Mica-like background, Segoe UI Variable, the system accent color used sparingly, standard Fluent control metrics. .NET 10 WPF provides this natively, so following it costs nothing and buys instant familiarity.

**Interior — instrument, not dashboard.** The quota rows are the dense heart of the widget and do not follow Fluent's generous spacing. Specifically:

- Tabular (lining) figures for all percentages and countdowns, so numbers do not jitter horizontally as they tick.
- Columns, not ragged lines: label left, percentage right-aligned in its own column, countdown right-aligned in another. The eye should scan a column.
- Hairline separators between rows. No cards nested inside cards.
- The bar is thin — roughly 4–6px — spans the row, and has a square or barely-rounded cap. Fill represents **used** capacity.
- The elapsed marker is a 1–2px vertical hairline sitting on the bar. Secondary to the fill in visual weight, but never so faint it disappears in either theme.

**Restraint rules.** At most one accent hue. No gradient carries meaning. No shadow deeper than a single subtle elevation on the window itself. No iconography beyond state indicators and the provider marks.

**Both themes are first-class.** Design light and dark together, not dark-as-an-afterthought. Show them side by side.

## 7. What to deliver

**`widget-states.html`** — one self-contained HTML file.

- Every screen and state above, on a single page, each under a clear heading naming which fixture and which state it shows.
- Light and dark both present.
- No external requests of any kind: no CDN, no web fonts, no remote images. Inline everything; embed any asset as a data URI.
- No frameworks. Plain HTML and CSS. Script only if a theme toggle genuinely needs it.
- Annotate the tricky ones inline — especially the marker at 100%, the exhausted row, and the `nimbus_quill` partial row — with a sentence on what the treatment is doing and why.

**`tokens.md`** — the token layer, written for someone building a WPF `ResourceDictionary`.

- Every color, spacing step, corner radius, and type style, named, with light and dark values.
- A proposed WPF resource key for each.
- Measured contrast ratios for text, for state indicators, and for the elapsed marker against both bar fill and bar track.

**A short rationale** — no more than a page. The information hierarchy you chose and why; what compact mode sacrifices first and in what order; and any place where these requirements pulled against each other and you had to pick.

## 8. Prohibitions

- **No prediction.** Never state or imply when the user will run out, never project a burn rate, never suggest a safe pace. The elapsed marker invites a comparison; the UI must not draw the conclusion. No "at this rate…", no "you'll hit your limit in…".
- **No fabricated values.** If a fixture does not supply a field, the design shows its absence. Never fill a gap with a zero, a dash that reads as zero, an em-dash placeholder that looks like data, or a greyed-out plausible number.
- **No invented providers, plans, or quota windows** beyond the fixtures above. If you need another example, reuse a fixture.
- **Never render context-window fill as a quota row.** Providers report a context-window percentage that looks exactly like a quota but is not — it is how full the current conversation is, not how much of a subscription remains. It must not appear anywhere in this design.
- **No credentials in any mockup.** No access token, API key, bearer header, session identifier, or file path containing a username — including in the diagnostics screen, which is where this is most tempting. Diagnostics show state, timing, versions, and error categories, never secrets.
- **No dashboard clutter, decorative illustration, gamification, or achievement framing.** No sparklines, no trend arrows, no historical charts, no streaks, no emoji.
- **No color-only status.** Already stated above; it is the single easiest requirement to violate by accident.
