# Road to zero — post-campaign register sweep

Este's directive (2026-08-04, post-reveal): **get the register to 0 open.**
Scope: the 8 open audit rows + all 32 proposed additions (now F-049–F-080).
Discipline unchanged: branch → tests/smoke → fresh review → merge → register
update. Register rows are the single source of truth for progress — re-derive,
never carry forward.

## Ledger mapping (proposed addition → F-id)

Wave 1: #1→F-049 (accent keys: ProgressRing/NumberBox/InfoBar), #2→F-050
(retired tag_vortex hexes). Wave 2: #3→F-051 (dup of F-047 — close),
#4→F-052 (x:Null ink hole + rule), #5→F-053 (done as DesignLawTests — close).
Wave 3: #6→F-054 (.cs opacity arm), #7→F-055 (glob guard), #8→F-056 (Style=
exemption), #9→F-057 (uncaptured surfaces), #10→F-058 (pin-game protocol).
Wave 4: #11→F-059 (filter empty state), #12→F-060 (filter FileTag), #13→F-061
(filter game-switch), #14→F-062 (bare ex.Message), #15→F-063 (CLR type names).
Wave 5: #16→F-064 (live-region first write), #17→F-065 (per-item names),
#18→F-066 (resource idiom), #19→F-067 (glyph-string detector). Wave 6:
#20→F-068 (GameLabel display name), #21→F-069 (rail chip affordance),
#22→F-070 (capture protocol doc). Wave 7: #23→F-071 (dead-string detector),
#24→F-072 (VSM danger rule), #25→F-073 (opacity on non-TextBlock carriers),
#26→F-074 (glyph allowlist), #27→F-075 (SDK-pin guard), #28→F-076
(warn-on-apply contrast). Wave 8: #29→F-077 (toggle/nav bloom), #30→F-078
(Loadout segment), #31→F-079 (code-built dialog shells). Reveal: #32→F-080
(theme persistence).

## Batches (each = one PR, ordered user-felt-first)

- **B0 bookkeeping** — register rows F-049–F-080 written; F-051/F-053 closed
  as dup/done. No code.
- **B1 quick wins (copy)** — F-043 INI casing, F-044 AI naming, F-062, F-063,
  F-068.
- **B2 theme persistence** — F-080. New persisted shape → camelCase JSON rule:
  JsonOpts + round-trip test + rule-file listing.
- **B3 filter trio** — F-059, F-060, F-061.
- **B4 danger/hover** — F-037 hover VSM (scoped ButtonBackgroundPointerOver in
  the dialog style), F-072 rule note, F-078 Loadout ThemeBg ink + outline
  treatment per fill discipline.
- **B5 theme keys + contrast** — F-049, F-050, F-076, F-052 fix half.
- **B6 glow completion** — F-077 (toggle/nav bloom — per-control composition),
  F-079 (code-built dialog shells via DialogTheming string-title auto-wrap).
- **B7 a11y** — F-064 (smoke entry), F-065 per-item names, F-025 keyboard
  accelerators (Ctrl+F/Ctrl+R/Esc + Space-toggles-row).
- **B8 lint armor** — F-054, F-055, F-056, F-067, F-071, F-073, F-074, F-075,
  F-052 rule half, F-066 idiom sweep.
- **B9 add-game reorder** — F-026 (detected list + filter first, AI/batch
  demoted).
- **B10 motion** — F-023 (duration resources, opacity-only dialog open,
  UISettings.AnimationsEnabled respect).
- **B11 type-size sweep** — F-010 (route body literals onto ramp resources,
  per-surface capture before/after).
- **B12 spacing snap** — F-022 (shared spacing resources, one sweep, capture
  before/after — riskiest, LAST).
- **B13 process closures** — F-057/F-058/F-070 (capture-protocol docs),
  F-069 (decide: accept tooltip+Settings route or add affordance).

## Status

Track per-batch completion in the register rows themselves. This file is the
map, not the scoreboard.
