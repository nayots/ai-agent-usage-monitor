# Design output — AI Usage Widget

Result of running `design-prompt.md` through Claude Design. Pulled from the project
**"Widget states and specifications"** (`867552cc-206c-46ea-8ddf-c774b6d4cd6e`) on 2026-08-11
via the `claude_design` MCP. That project remains the editable source of truth; everything
here is a copy.

## What each file is

| File | Role |
|---|---|
| `design-prompt.md` | The brief. Input, not output. Satisfies PRD §7. |
| **`widget-states.html`** | **The deliverable.** Static, self-contained render of every screen and state. Open it directly — no server, no network. |
| `tokens.md` | Token layer written for a WPF `ResourceDictionary`, with measured contrast ratios. |
| `rationale.md` | Information hierarchy, compact-mode cut order, and where requirements conflicted. |
| `widget-states.dc.html` | Editable source of the page. Needs the runtime below. |
| `ProviderCard` / `QuotaRow` / `StateChip` / `TrayGlyph` `.dc.html` | Editable component sources, composed by `widget-states.dc.html`. |
| `support.js` | Claude Design's component runtime. Vendored, generated, **do not edit**. |

`widget-states.html` is what an engineer reads while writing XAML. The `.dc.html` sources
exist so the design can be changed and re-rendered; they are not needed to consume it.

## Why there are two forms of the same page

The brief (§7) required *one self-contained HTML file, no external requests of any kind*.
Claude Design's actual output does not meet that on its own:

- `widget-states.dc.html` composes 32 `<dc-import>` elements that are fetched at runtime,
  so it cannot be opened from `file://` — the fetches fail CORS and the page stays blank.
- `support.js` pulls React 18.3.1 and ReactDOM 18.3.1 from `unpkg.com` at load time, so
  the page needs internet access as well as a web server.

`widget-states.html` is the fix: the component page rendered once and captured as static
DOM. Verified to contain zero `<script>` tags, zero `src`/`href` references, zero CSS
`url()`, and zero `@font-face`, and to issue **zero network requests** when loaded. It
matches the live render exactly — 1369 elements, 7813px tall, 20065 characters of text.

## Regenerating `widget-states.html`

Only needed after editing a `.dc.html` source. The components must be served over HTTP —
`file://` will not work.

```powershell
# from docs/design/
npx --yes http-server -p 8731 --cors
```

Open `http://127.0.0.1:8731/widget-states.dc.html`, let it finish rendering, then in the
console:

```js
const doc = document.documentElement.cloneNode(true);
doc.querySelectorAll("script").forEach(s => s.remove());
doc.querySelectorAll("x-dc,dc-import,sc-if,sc-for").forEach(e => e.replaceWith(...e.childNodes));
copy("<!DOCTYPE html>\n" + doc.outerHTML);   // then paste over widget-states.html
```

Keep the provenance comment at the top of the file when you replace it.

## Open questions this design raises

These are product decisions, not design defects — recorded here so they are not lost:

- **`ColourBarsByUsage` is a new setting** the brief did not ask for (bars take one of three
  fixed tones by usage band). It is not in `docs/PRD.md`, and it deliberately breaks the
  brief's "at most one accent hue" rule, which is why the design ships it behind a toggle.
  PRD §7 needs to either adopt or reject it.
- **The 360×420 size target was missed by design**, at 410px for the default widget only
  because the column captions and duration provenance were moved to the expanded card.
  `rationale.md` says restoring them needs a 445px budget.
