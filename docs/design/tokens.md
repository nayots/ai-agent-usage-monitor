# Token layer — Quota Monitor

For a WPF `ResourceDictionary`. Two theme dictionaries (`Light.xaml`, `Dark.xaml`) share one set of keys; everything below is keyed identically in both. All colours are sRGB hex. Contrast ratios are WCAG 2.1 relative-luminance ratios, computed from these exact values.

---

## 1. Colour

### Surfaces

| Token | Light | Dark | WPF key | Use |
|---|---|---|---|---|
| Window base | `#F3F3F3` | `#202020` | `WidgetWindowBackgroundBrush` | Mica-backed window fill |
| Window stroke | `#D9D9D9` | `#3A3A3A` | `WidgetWindowStrokeBrush` | 1px window border, footer rule |
| Layer | `#FFFFFF` | `#2B2B2B` | `WidgetLayerBackgroundBrush` | Provider card, menu, settings group |
| Layer alt | `#F7F7F7` | `#323232` | `WidgetLayerAltBackgroundBrush` | Buttons, badges, inset banners |
| Card stroke | `#E5E5E5` | `#383838` | `WidgetCardStrokeBrush` | 1px card / control border |
| Divider | `#ECECEC` | `#333333` | `WidgetRowDividerBrush` | Hairline between quota rows |
| Selection | `#EAF2FB` | `#2F3B47` | `WidgetSelectionBackgroundBrush` | Hovered / focused menu item |
| Token chip fill | `#F2F2F2` | `#333333` | `WidgetTokenChipBackgroundBrush` | Unresolved window-name chip |
| Token chip stroke | `#DEDEDE` | `#3F3F3F` | `WidgetTokenChipStrokeBrush` | Unresolved window-name chip |

### Text

| Token | Light | Dark | WPF key | On layer, light | On layer, dark |
|---|---|---|---|---|---|
| Ink primary | `#1B1B1B` | `#FFFFFF` | `TextPrimaryBrush` | **17.2:1** | **14.2:1** |
| Ink secondary | `#5C5C5C` | `#C8C8C8` | `TextSecondaryBrush` | **6.7:1** | **8.5:1** |
| Ink tertiary | `#6E6E6E` | `#A6A6A6` | `TextTertiaryBrush` | **5.1:1** | **5.8:1** |

Ink tertiary is also the **stale** text colour. It was chosen as the lightest value that still clears 4.5:1 in both themes, so stale data is de-emphasised without dropping below body-text contrast.

### Accent and state

| Token | Light | Dark | WPF key | Contrast |
|---|---|---|---|---|
| Accent text | `#1F63AD` | `#4CC2FF` | `AccentTextBrush` | all accent-coloured text — see below |
| Accent fill | `#2B7CD3` | `#4CC2FF` | `AccentBrush` | toggle fill, selection accents |
| Bar fill (accent) | `#2B7CD3` | `#3A96DD` | `QuotaBarFillBrush` | vs track **3.4:1** light, **3.4:1** dark |
| Bar fill, exhausted | `#C0453F` | `#E0685E` | `QuotaBarExhaustedFillBrush` | vs track **4.0:1** light, **3.3:1** dark |
| Bar fill, stale | `#7A7A7A` | `#8A8A8A` | `QuotaBarStaleFillBrush` | vs track **3.4:1** light, **3.2:1** dark |
| Bar track | `#E4E4E4` | `#3D3D3D` | `QuotaBarTrackBrush` | container only |
| Elapsed marker | `#1B1B1B` | `#FFFFFF` | `ElapsedMarkerBrush` | see §2 |
| Marker gap | = Layer | = Layer | `ElapsedMarkerGapBrush` | 1px either side of the core |
| Hatch overlay | `#FFFFFF` @ 38% | `#000000` @ 30% | `QuotaBarHatchBrush` | exhausted tile brush |
| State OK | `#0F7B3C` | `#6CCB8E` | `StateOkBrush` | **5.4:1** / **7.1:1** |
| State warn | `#8A5A00` | `#E0B252` | `StateWarnBrush` | **5.9:1** / **7.2:1** |
| State bad | `#B32020` | `#F0796F` | `StateBadBrush` | **6.7:1** / **5.2:1** |

### Bar tone by usage — `ColourBarsByUsage` setting

When the setting is on, every bar in the app (quota rows and tray rules alike) takes its fill from the band its percentage falls in. Three steps, no interpolation, same thresholds in both themes. When it is off, all bars below 100% use `QuotaBarFillBrush` and the tone tokens are unused.

| Range | Light | Dark | WPF key | vs track | vs marker |
|---|---|---|---|---|---|
| 0–74% | `#2B7CD3` | `#3A96DD` | `QuotaBarFillBrush` | 3.4:1 / 3.4:1 | 4.0:1 / 3.2:1 |
| 75–99% | `#9A6600` | `#B47F1E` | `QuotaBarHighFillBrush` | 3.9:1 / 3.1:1 | 3.5:1 / 3.5:1 |
| 100% | `#C0453F` | `#E0685E` | `QuotaBarExhaustedFillBrush` | 4.0:1 / 3.3:1 | 3.4:1 / 3.3:1 |

The high band is a dedicated bar token, **not** `StateWarnBrush`. The text-weight warn colour is too light in dark theme (`#E0B252` leaves the white elapsed marker at 2.0:1) and too dark in light theme (`#8A5A00` leaves the marker at 2.9:1). `QuotaBarHighFillBrush` is tuned to the narrow band that clears 3:1 against the track *and* against the marker in both themes.

Bar *length* and the written percentage are the primary signals; tone reinforces them. This is the one deliberate departure from the single-accent-hue restraint rule, and it is user-controlled.

**`AccentTextBrush` measured against every surface the widget paints accent text on:**

| Surface | Light `#1F63AD` | Dark `#4CC2FF` |
|---|---|---|
| Layer (`#FFFFFF` / `#2B2B2B`) — "N more windows" | **6.1:1** | **7.1:1** |
| Layer alt (`#F7F7F7` / `#323232`) — stale-banner Refresh | **5.9:1** | **6.5:1** |
| Window base (`#F3F3F3` / `#202020`) — footer and expanded-card actions | **5.5:1** | **8.1:1** |

Light accent text is a separate, darker key from the fill accent because `#2B7CD3` only reaches 3.8:1 on the window base — below the 4.5:1 text floor at 10.5–11px. Darkening the *fill* instead was not an option: `QuotaBarFillBrush` is already the binding constraint for the elapsed marker. Dark theme needs no split; `#4CC2FF` clears 4.5:1 on all three surfaces.

The accent is a single hue in all three roles. `QuotaBarFillBrush` is the darkest sibling, used for area fill, because the marker has to survive on top of it. Substitute the Windows system accent here if the app follows it; the bar fill should then be the system accent's `AccentDark1` (light theme) / `AccentLight2` (dark theme) so the ratios below hold.

---

## 2. The elapsed marker — the contrast that nearly fails

A single flat colour **cannot** clear 3:1 against both a light track and a dark fill: the two requirements pull in opposite directions. The marker is therefore drawn as a **three-band element**, 4px wide overall:

```
[1px gap = layer colour][2px core = ElapsedMarkerBrush][1px gap = layer colour]
```

Height is 11px against a 5px bar, so 3px overshoots above and below and the marker is findable even where it sits on top of the fill.

| Pairing | Light | Dark |
|---|---|---|
| Marker core vs **bar track** | `#1B1B1B` on `#E4E4E4` — **13.8:1** | `#FFFFFF` on `#3D3D3D` — **10.9:1** |
| Marker core vs **bar fill (accent)** | `#1B1B1B` on `#2B7CD3` — **4.0:1** | `#FFFFFF` on `#3A96DD` — **3.2:1** |
| Marker core vs **exhausted fill** | `#1B1B1B` on `#C0453F` — **3.4:1** | `#FFFFFF` on `#E0685E` — **3.3:1** |
| Marker core vs **stale fill** | `#1B1B1B` on `#7A7A7A` — **4.0:1** | `#FFFFFF` on `#8A8A8A` — **3.5:1** |
| Marker core vs **layer** (overshoot) | **17.2:1** | **14.2:1** |
| Gap band vs fill | = fill-vs-layer, ≥ 3.4:1 | ≥ 3.2:1 |

Every pairing clears 3:1. The dark accent fill (`#3A96DD`) is the binding constraint at 3.2:1 — it is deliberately darker than `AccentBrush`, and lightening it any further breaks the marker.

**End clamping.** The 4px marker box is positioned `left = elapsed% − (elapsed% × 4px)`. At 0% the box sits flush inside the left cap, at 100% flush inside the right; it never overhangs the track and never merges with an end cap. The core's centre therefore shifts by at most 2px relative to a mathematically exact position — a deliberate trade at 4–6px bar height. In WPF: a `Grid` with `HorizontalAlignment=Left` and a `TranslateTransform` bound to that expression, or a 3-column `Grid` with a star-sized leader.

**High-contrast mode.** `ElapsedMarkerBrush` → `SystemColors.WindowTextBrush`, `ElapsedMarkerGapBrush` → `SystemColors.WindowBrush`, `QuotaBarFillBrush` → `SystemColors.HighlightBrush`, `QuotaBarTrackBrush` → `SystemColors.ControlBrush`. The gap bands are what keep the marker readable when fill and marker resolve to the same system colour.

---

## 3. Spacing

Base unit 2px. Card interiors are deliberately tighter than Fluent's default; Settings and Diagnostics use Fluent metrics.

| Token | Value | WPF key | Use |
|---|---|---|---|
| Space 2XS | 2 | `SpaceXXSmall` | Glyph-to-label |
| Space XS | 4 | `SpaceXSmall` | Badge padding, icon gaps |
| Space S | 6 | `SpaceSmall` | Row internal, card gap in compact |
| Space M | 8 | `SpaceMedium` | Column gutter, card gap |
| Space L | 10 | `SpaceLarge` | Widget body padding |
| Space XL | 12 | `SpaceXLarge` | Card horizontal padding − 1 |
| Space 2XL | 16 | `SpaceXXLarge` | Settings group spacing |

| Metric | Value | WPF key |
|---|---|---|
| Widget width | 360 | `WidgetWidth` |
| Widget max height before list scrolls | 520 | `WidgetMaxHeight` |
| Title bar height | 32 (28 compact) | `WidgetTitleBarHeight` |
| Footer height | 26 (24 compact) | `WidgetFooterHeight` |
| Card padding | 8,11,9,11 (6,9,7,9 compact) | `ProviderCardPadding` |
| Quota row padding | 6 top / 5 bottom (4/4 compact) | `QuotaRowPadding` |
| Used column width | 42 | `QuotaUsedColumnWidth` |
| Resets column width | 62 | `QuotaResetColumnWidth` |
| Column gutter | 8 | `QuotaColumnGutter` |
| Bar height | 5 | `QuotaBarHeight` |
| Bar top margin | 5 | `QuotaBarMargin` |
| Marker width / height | 4 (2 core) / 11 | `ElapsedMarkerWidth`, `ElapsedMarkerHeight` |
| Settings row min height | 38 | `SettingsRowHeight` |
| Menu item height | 30 | `TrayMenuItemHeight` |

Measured heights at 100% scaling: default widget **410px**, compact widget **326px**, both at 360px wide.

## 4. Corner radius

| Token | Value | WPF key | Use |
|---|---|---|---|
| Radius window | 8 | `RadiusWindow` | Widget, menu, settings, diagnostics |
| Radius card | 6 | `RadiusCard` | Provider card, settings group |
| Radius control | 4 | `RadiusControl` | Buttons, badges, menu item highlight |
| Radius chip | 3 | `RadiusChip` | Unresolved-token chip |
| Radius bar | 1 | `RadiusBar` | Quota bar and fill |

## 5. Elevation

| Token | Value | WPF key |
|---|---|---|
| Window shadow | Y 8, blur 20, `#000` @ 12% light / 45% dark | `WidgetWindowShadow` |
| Flyout shadow | Y 10, blur 24, `#000` @ 16% light / 50% dark | `TrayMenuShadow` |

Nothing else casts a shadow. Cards are separated by a 1px stroke only.

## 6. Type

Family: **Segoe UI Variable Text** (`Segoe UI Variable Display` above 16px), fallback `Segoe UI`. Monospace: **Cascadia Mono**, fallback `Consolas`.

All numerals use `Typography.NumeralStyle="Lining"` and `Typography.NumeralAlignment="Tabular"`.

| Token | Size / weight | WPF key | Use |
|---|---|---|---|
| Caption micro | 9.5 / 600, +5% tracking, upper | `CaptionMicroTextStyle` | Column captions, tier badge |
| Caption | 11 / 400 | `CaptionTextStyle` | Version, timestamps, secondary lines |
| Body small | 11.5 / 400 | `BodySmallTextStyle` | Notice bodies, limit-reached line |
| Body | 12.5 / 400 | `BodyTextStyle` | Window label, percentage, menu items |
| Body strong | 12.5 / 600 | `BodyStrongTextStyle` | Exhausted percentage and countdown |
| Numeric | 12 / 400 tabular | `NumericTextStyle` | Countdown column |
| Subtitle | 13 / 600 | `SubtitleTextStyle` | Provider name |
| Token | 11.5 / 400 mono | `TokenTextStyle` | Unresolved window name |

Smallest type is 9.5px, used only for all-caps captions with 5% tracking; at 125% scaling that is ~12px and at 200% ~19px. Nothing below 11px carries a value the user must read.

## 7. Shape and glyph tokens (state, never colour alone)

| State | Glyph | WPF key |
|---|---|---|
| Connected | filled disc 8px | `StateGlyphConnected` |
| Discovering | dashed ring 9px | `StateGlyphDiscovering` |
| Waiting | hollow ring 8px | `StateGlyphWaiting` |
| Stale | diamond 7px | `StateGlyphStale` |
| Unavailable | triangle 10×9 | `StateGlyphUnavailable` |
| Error | cross 9px | `StateGlyphError` |
| Unsupported | bar 9×2 | `StateGlyphUnsupported` |
| Not installed | hollow ring 8px + word | `StateGlyphNotInstalled` |
| Official tier | solid 1px border + filled dot | `TierBadgeOfficialStyle` |
| Unofficial tier | dashed 1px border + open triangle | `TierBadgeUnofficialStyle` |

Every glyph is paired with its word in every location. Removing all colour leaves seven distinguishable silhouettes plus text.
