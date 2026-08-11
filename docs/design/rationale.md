# Rationale

## Hierarchy

The question is "how much have I got left, and when does it come back", asked in two seconds. So the row is the unit of design and everything above it is addressing: **provider → window → number**. Within the row the order of visual weight is percentage, then bar, then countdown, then label. The label is the least urgent element because by the time you have found the row you already know which window it is.

Three fixed columns — label, used, resets in — let the eye drop down a column instead of reading each row as a sentence. Tabular figures mean a ticking countdown never nudges its neighbours, which is the difference between a thing you can glance at and a thing you have to focus on. Hairlines separate rows; nothing is nested inside anything.

Everything above the rows is addressing, and it is ranked by how likely it is to change the meaning of the numbers below: connection state first (if it is not Connected, the numbers are suspect), then mechanism tier (permanently true, so permanently visible but quiet), then version and timestamp (rarely load-bearing).

The elapsed marker exists because 100% used at 24% elapsed and 100% used at 96% elapsed are different situations, and a plain bar renders them identically. It is drawn as a hairline, secondary to the fill, and nothing in the interface interprets the gap between them. No projection, no rate, no advice.

## What compact sacrifices, in order

1. **Per-card timestamp** — one stays in the footer. Freshness is a property of the poll, not of a card.
2. **Version and plan** — you do not check your version several times a day.
3. **Provider monogram** — the name is already there.
4. **The *Connected* status chip** — silence means connected. Every other state comes back immediately, including Stale.
5. **Padding** — 2px off rows and cards.
6. **Rows beyond three per card** — collapse to "N more windows — expand". This is the first cut that removes data, which is why it is last.

Never cut: window label, percentage, bar, elapsed marker, countdown, tier badge, limit-reached indicator, any non-connected state.

Column captions and duration provenance were already cut from the *default* widget, before compact begins — see below.

## Where the requirements pulled against each other

**The marker's contrast is unsatisfiable as a single colour.** 3:1 against a light track and 3:1 against a dark fill are contradictory demands; a colour dark enough for one is invisible on the other. The marker is therefore a 4px assembly — 2px core with a 1px surface-coloured gap on each side — so what guarantees separation is the gap, not the core's colour. Every pairing then clears 3:1, with the dark-theme accent fill binding at 3.2:1. The knock-on cost: the bar fill in dark theme is a darker blue than the accent used for links, so the widget carries two values of one hue. Measured ratios are in `tokens.md` §2.

**The 420px budget cost the column captions.** Both fixtures fully expanded came to 454px with captions and duration provenance on every row. Rather than shrink type below 11px or cut a window, I moved both to the expanded provider card: the default widget is 410px, and "Used / Resets in" is legible from the `%` sign and the shape of a countdown. If the budget can stretch to 445px, the captions should come back — they are the first thing I would restore.

**Legibility of stale versus prohibition on fabricated data.** Stale values must be de-emphasised but still readable, and the obvious move — heavy greying — reads as "disabled" and edges towards looking like placeholder data. Stale therefore uses tertiary ink, the lightest value that still clears 4.5:1 in both themes (5.1:1 light, 5.8:1 dark), and switches the bar fill from accent to neutral grey. The de-emphasis is carried by the neutral fill and the banner, not by making the type faint.

**"Unofficial" has to be quiet and unmissable at once.** A coloured warning badge on a card that sits on screen all day becomes furniture within a week. Colour is spent instead on the states that are transient and actionable. Tier is carried by border style and word: solid border with a filled dot for Official, dashed border with an open triangle for Unofficial. Dashed-versus-solid survives greyscale, high contrast, and peripheral vision, and an unofficial badge cannot be mistaken for an official one at any size.

**Scrolling.** A card never truncates: every window the provider reported is drawn. The widget grows to a 520px ceiling and then the provider list scrolls — never an individual card, because a scrollbar inside a card implies the card is a container of records rather than a report. Collapsing to "N more" happens only in compact mode, at three rows.

**Colouring bars by usage cost the single-accent rule.** Bars now take their fill from the band their percentage falls in — accent below 75%, a dedicated high tone from 75 to 99, exhausted red at 100 — across quota rows and tray rules alike. Three fixed bands, not interpolation, so nothing implies a rate. It stays inside the no-colour-only rule because bar length and the written percentage were already carrying the value and tone only reinforces them; in the tray, where there is no written figure, the state overlay carries what colour would otherwise have to. What it does not stay inside is "at most one accent hue", so it ships behind a **Colour bars by usage** setting: off, every bar below 100% returns to the single accent. The high band is a dedicated bar token rather than `StateWarnBrush` — the text-weight warn colour leaves the white elapsed marker at 2.0:1 in dark theme, and the band that clears 3:1 against both the track and the marker in both themes is narrow enough to need its own value.

**`nimbus_quill`.** The partial row keeps the full three-column grid and leaves the countdown cell genuinely empty. Every candidate placeholder — a dash, an em-dash, "—", a greyed zero — reads as a value at a glance, and the whole point of the row is that no value arrived. The monospace chip marks the name as the provider's literal token rather than a phrase the app authored, and it is quieter than a resolved label, because an unknown window is ordinary rather than an error. The absent marker follows from the absent duration and is the same absent marker any unverified window gets, which is why the unmarked bar is designed as the baseline.
