# 626 Mod Launcher — design language (vibe-glow stage 0)

> Chosen at the stage-0 gate, 2026-08-03: **Hangar leads, Afterglow's glow
> rule grafts in.** Concept boards: Hangar / Afterglow / Field Manual
> (artifacts, session 2026-08-03). Every rule below is measured by the
> stage-1 audit; deviations become findings.

## Identity statement

The launcher is flight-line equipment with a live current running through
it. The skeleton is industrial-utilitarian — squared corners, 1px borders,
condensed stencil chrome, rows dense enough to hold a 27-mod loadout on one
screen, motion near zero. Onto that skeleton, one grafted law from the
arcade: **light means live.** Anything energized — an enabled toggle, the
primary action, a ban-risk warning — emits the theme's accent bloom;
static chrome never glows. Color belongs entirely to the theme engine; the
identity must read in silhouette under any palette a user throws at it.

## Type ramp

| Role | Face | Size / weight | Case |
| --- | --- | --- | --- |
| Hero | Bahnschrift SemiBold Condensed | 34 | UPPERCASE, +.06em |
| View title | Bahnschrift Condensed | 21 | UPPERCASE, +.06em |
| Row title | Segoe UI Semibold | 14 | Sentence case |
| Body | Segoe UI | 13 | Sentence case |
| Meta | Segoe UI | 11.5 | Sentence case |
| Tags / data | Cascadia Mono | 10 | UPPERCASE |

Bahnschrift is a Windows system variable font (weight + width axes);
fall back to Segoe UI Semibold if the width axis is unavailable. Stencil
treatment (condensed caps + tracking) is chrome-only — mod names,
descriptions, and user content stay sentence-case sans.

## Spacing scale

4px base. Steps: 4 / 8 / 12 / 20 / 32.

- Row padding 7×12 — density is a feature; a full loadout fits one screen.
- Section gaps 20. Dialog padding 20. Nothing breathes wider than 32.

## Shape language

- **Corner radius 0 everywhere.** Cards, buttons, tags, toggles, dialogs.
- Borders 1px solid `border`. Active or modal surfaces carry a 3px accent
  top rail.
- Boxes over rules; outline over fill. Fill is reserved for the primary
  action and for state (toggle on).

## Iconography

1.5px stroke, squared terminals, 16px grid. Outline glyphs only; a fill
appears when the thing is on. Icons accompany labels in chrome; icon-only
is allowed solely in mod-row action clusters where the pattern repeats
every row (endorse / readme / uninstall).

## Motion

- Dialogs: 80ms opacity snap-in. No scale, no slide.
- Toggle knob: 80ms linear.
- Glow transitions (the graft): 160ms ease-out on bloom appearance.
- Everything else: instant. `prefers-reduced-motion` drops the 160ms glow
  ease to instant as well.

## The glow rule (grafted from Afterglow)

Bloom = `accent_bloom` theme token, exactly as the theme defines it
(obsidian 4/0.35 whispers, matrix 4/0.60 radiates, a zero-alpha theme
reads flat and that is legitimate). Surfaces that MAY bloom:

1. Enabled toggle (border + knob)
2. Primary action button (Play modded)
3. Active nav / selected item rail
4. Danger alerts (ban-risk tag, destructive confirm) — in `danger` color

Nothing else. A surface that glows must be interactive or alerting;
static chrome never emits light.

## Flagship theme proposal — "Forge"

Gunmetal + amber, bloom on. Ships through the theme engine as a builtin
token set at the reveal; never as hardcoded surface values.

| Token | Value | Token | Value |
| --- | --- | --- | --- |
| bg | `#14161a` | accent | `#ffb454` |
| glass | `#1d2126` | pace_marker | `#ff5c33` |
| glass_on_mica | `#191c21` | sparkline | `#ffb454` |
| title_bg | `#101216` | success | `#7ec46f` |
| border | `#33383f` | warning | `#ffb454` |
| text | `#e6e8ea` | danger | `#ff5c33` |
| text_secondary | `#aeb4bc` | info | `#6fb3c4` |
| text_dim | `#767e88` | bar_bg | `#1d2126` |
| text_muted | `#4e555e` | footer_bg | `#0f1114` |
| tag_secondary | `#ffb454` | tag_client_only | `#6fb3c4` |
| tag_vortex | `#ff5c33` | tag_folder | `#7ec46f` |
| accent_bloom | blur 6 / alpha 0.45 | | |

## Component rules

- **Rows:** toggle left, semibold name, dim description ellipsized, mono
  tags right-aligned. Hover = `glass` background shift, 80ms.
- **Tags:** outline mono chips, 0 radius, colored by their token
  (`tag_*`, `danger` for ban-risk). Ban-risk may bloom (rule 4).
- **Toggles:** consume `accent` and `accent_bloom` — never a hardcoded
  control brush. (Today's blue-under-every-theme toggles are the standing
  counter-example; the audit will file them.)
- **Buttons:** primary = the only filled surface on screen, accent fill +
  bloom. Secondary outline. Destructive outline in `danger`; filled
  danger only inside a confirm dialog.
- **Dialogs:** 0 radius, 3px accent rail, stencil stamp eyebrow (mono
  caps, e.g. `RESET // RESTORE POINT FIRST`), body copy sentence case,
  actions right-aligned. Reversibility note present wherever a write
  happens.
- **Empty states:** an order, not an apology. Stencil headline + one plain
  instruction ("No tools yet. Drop a zip to install, or hit + on the
  tools rail.").

## Copy rules

Inherits the repo voice (README operating voice): builder-to-builder,
second person, sentence case, periods at the end of microcopy, no emoji,
no corporate speak. Stencil stamps are the one sanctioned uppercase
surface. Errors say what happened and what to do next; reversibility
notes state the guarantee plainly ("Saving creates a snapshot first.").

## Invariants (verbatim from campaign state)

1. Color belongs to the theme engine (23-token contract, Themes.cs) —
   surfaces must not hardcode palette values. Findings requiring
   token-contract breaks are refused; extensions must be
   optional-with-fallback per NormalizeTheme.
2. Reversibility and pure-core laws hold — no UI fix may move file-op
   logic into the App layer or delete user data. CorePurityTests stays
   green.
