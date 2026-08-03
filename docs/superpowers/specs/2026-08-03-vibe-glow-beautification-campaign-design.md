# vibe-glow — beautification campaign + plugin v0.1 design (2026-08-03)

Two deliverables, one shape:

1. **vibe-glow** — a new vibe-family plugin (its own repo, `C:\Users\estev\Projects\vibe-glow`, matching vibe-access / vibe-insights) that packages a repeatable multi-stage, multi-pass QOL / UI / beautification campaign for any app.
2. **The 626 Mod Launcher campaign** — the plugin's first dogfood run. The skill is written first as the operating manual; the launcher run hardens it.

Decisions locked during brainstorm (2026-08-03, all Este's calls):

- **Direction:** distinctive identity — lead with brand, bend Fluent where the identity wants it. Not native-camouflage.
- **Identity source:** invented in stage 0. No pre-existing look; the campaign's first artifact is the design language.
- **User theming survives.** The launcher's theme engine owns color (23-token Sanduhr contract + accent bloom, `src/ModManager.Core/Themes.cs`). Identity lives in structure — type, spacing, shape, iconography, motion, component layout — plus one new flagship default theme. Token-contract extensions are optional-with-fallback only (the `NormalizeTheme` pattern); existing user themes keep working, byte-for-byte.
- **Evidence:** screenshots + XAML, both. Rendered truth and structural truth reviewed together.
- **Ship shape:** quiet bones, loud reveal. Structural/QOL work ships incrementally (0.16, 0.17…); the identity flip lands as one flagship release.
- **Models:** Fable orchestrates; **Opus 5 agents do all reviewing** (per-agent model override on the Agent/Workflow layer). Skeptic/verify passes run at high effort.
- **Plugin weight:** minimal first cut — router + stage commands + `:status`. Friction/session loggers and `:evolve` deferred until the dogfood run earns them.

## 1. Campaign architecture

Four stages, launched separately, each gated on Este's approval before the next fires — stage 2 gates per wave rather than once. No stage auto-advances.

| Stage | Name | Produces | Gate |
| --- | --- | --- | --- |
| 0 | Identity | `docs/superpowers/specs/<date>-launcher-design-language.md` | Este picks a concept |
| 1 | Audit | Verified findings register, `docs/superpowers/research/<date>-ui-audit-findings.md` | Este approves the register |
| 2 | Waves | Shipped incremental releases (0.16, 0.17…) | Per-wave PR merge |
| 3 | Reveal | Flagship release + marketing artifacts | Este publishes |

The design language doc is the measuring stick: after stage 0, reviewers never argue taste — only conformance to the written language.

## 2. Stage 0 — Identity

1. **Baseline capture** — screenshot all reachable surfaces of the current app (evidence pipeline, §3) plus the XAML inventory (23 surfaces at time of writing).
2. **Concept boards** — 2–3 self-contained HTML artifacts, each simulating launcher chrome under a candidate identity: type stack, spacing scale, corner/shape language, iconography direction, motion notes, and one flagship theme. Each board wires in the app's real theme tokens and ships a **theme switcher** so every concept is stress-tested under hostile user themes (Obsidian, Matrix) before commitment. Figma stays available for later iteration on the winner; it is not on the critical path.
3. **Selection + write-down** — Este picks; the winner becomes the design language doc: named tokens (type ramp, spacing scale, radii, motion durations/easings), component rules (buttons, rows, dialogs, tags, empty states), copy rules (inherits the repo voice), and the invariants list.
4. **Invariants list** — per-app constraints every later pass enforces. For the launcher, entry #1: *color belongs to the theme engine; identity may not hardcode palette values into surfaces.* New tokens (e.g. radius, bloom presets) extend the contract as optional-with-fallback only.

## 3. Evidence pipeline

- **Capture helper** — a PowerShell script (PrintWindow-based per-window capture → PNG). Lives in the plugin as the WinUI adapter; the launcher run is its reference use.
- **Automated surfaces** — main window + navigable views: launched via the run skill, captured without a human.
- **Guided session** — modal dialogs need a driver: Este gets a click-through checklist, opens each dialog, hits a capture hotkey; ~10 minutes per full round. Partial rounds (changed surfaces only) are the common case after waves.
- **Multi-theme capture** — key surfaces captured under 3 themes: the flagship, Obsidian, and Matrix (stress test). Identity findings that only hold under one theme are consistency bugs by definition.
- **Storage** — PNGs land in `docs/ui-evidence/` which is **gitignored** (add the entry when the folder is created). The findings register references evidence by relative path; the repo never swallows screenshots.
- **Launcher-specific gotchas, baked into the helper's runbook:**
  - Smoke builds need `-p:Version=<current>` or the Nexus surfaces silently vanish (minBinaryVersion gate).
  - After XAML edits, clean `obj/`/`bin/` before rebuild or the app crashes at `Connect()` with InvalidCastException; check `app-errors.log` on silent launch failures.

## 4. Stage 1 — Audit

One workflow. Five app-wide passes, each an Opus 5 agent with a distinct lens:

| Pass | Hunts for |
| --- | --- |
| Visual conformance | Deviation from the design language doc |
| Consistency | Cross-surface drift: duplicated styles, hardcoded values in XAML, spacing/radius variance, one-off controls |
| QOL / flow | Clicks-to-do-common-things, empty states, error states, first-run friction, dead ends |
| Copy / voice | UI strings vs. the repo's written voice rules |
| Accessibility | Contrast **under user themes**, keyboard nav, focus order, hit targets |

- The `ui-ux-pro-max` skill loads as the evaluation rubric for the UI passes.
- **Skeptic verification:** every finding faces a separate Opus agent prompted to refute it. Only survivors enter the register. Findings that would break an invariant (e.g. the theme contract) are auto-refused at this step — the invariant outranks the finding.
- **Register format:** one committed markdown file; per finding: id, surface, lens, severity (1–5), visibility weight (1–5), evidence pointer(s), skeptic verdict, proposed fix direction. Ranked by severity × visibility.
- Sizing: ~10–14 agents total (five lenses + skeptics), inside the medium workflow guideline. Este green-lights the run explicitly (cost throttle).

## 5. Stage 2 — Waves

Findings become fix waves in payoff order:

1. **Style consolidation** — extract shared styles/resources, kill hardcoded values. Runs first because it makes every later fix cheaper and shrinks the consistency surface.
2. **QOL** — flow friction, empty/error states, first-run.
3. **Per-surface polish** — visual conformance fixes, surface by surface, visibility-ordered.

Per-wave discipline:

- Small PR off `master` (house branch rules; no long-lived campaign branch).
- Behavior changes (view-models, Core) get a failing xUnit test first, per house rules. Pure-XAML changes get an entry in `docs/smoke-tests/pending.md`.
- `/code-review ultra` (or the personal ultrareviewer agent) as the merge gate.
- **Re-review loop:** after merge, re-capture changed surfaces → targeted Opus re-review against the register → findings close or the wave loops. A surface is done when it measures clean, not when the diff lands. This loop is the campaign's "multi-pass."
- Ships incrementally on the GitHub channel. The Store SKU follows on its own cadence — the in-flight 0.15.0.0 submission is never at risk from this campaign.
- CorePurityTests hooks keep running; reversibility-auditor on call for anything file-op-adjacent (unlikely in UI work, mandatory if it appears).

## 6. Stage 3 — Reveal

- Flagship default theme (new builtin, id + name per the design language) + signature moments: accent bloom tuning, motion, hero touches on MainWindow/LibraryView.
- One flagship release: release-notes-drafter output, refreshed store listing copy, screenshots for the listing regenerated from the evidence pipeline.
- Store submission follows the runbook (`docs/store-runbook` lineage) — reveal on GitHub channel first, Store SKU after.

## 7. The vibe-glow plugin v0.1

- **Home:** its own repo, `C:\Users\estev\Projects\vibe-glow`, matching the family pattern.
- **Commands:** `/vibe-glow` (state-aware router, house pattern), `:identity`, `:audit`, `:wave`, `:reveal`, `:status` (read-only).
- **Per-repo state:** `.vibe-glow/state.json` (camelCase keys) — current stage, design-language doc path, findings-register path, wave ledger, invariants list, chosen evidence adapter. The design-language doc and findings register are committed repo artifacts; state.json is the pointer layer.
- **Evidence adapters:** the only stack-specific component. v0.1 ships two: `winui-powershell` (PrintWindow capture, this repo) and `web-playwright` (Playwright MCP screenshots — covers WeSeeYouAtTheMovies, PriceScout/Streamlit, dashboard surfaces). Adapter selection happens in `:identity` and is recorded in state.
- **Model policy:** orchestrator inherits the session model; review agents pin Opus; skeptics run high effort. Encoded in the skill text, overridable per run.
- **Plays-well-with:** `ui-ux-pro-max` (rubric), `frontend-design` (identity-stage guidance), `/code-review ultra` (merge gate), `vibe-iterate:ux-polish` (the one-off polish tool; vibe-glow is the campaign tool — a single small finding can be handed to ux-polish instead of a wave).
- **Deferred past v0.1:** friction-logger, session-logger, `:evolve`, additional adapters. They bolt on via the self-evolving framework once the dogfood run proves the shape.

## 8. Build order

1. Scaffold vibe-glow v0.1 (repo, router, five commands, two adapters, state schema).
2. Run the launcher campaign **through the plugin** — stage 0 first gate is the concept-board pick.
3. Friction from the dogfood run gets captured as notes in the vibe-glow repo and becomes its first hardening pass (pre-`:evolve`, by hand).

## 9. Risks and refusals

- **Capture automation gaps** — some dialog states may resist scripted capture; the guided session is the designed fallback, not a failure mode.
- **Finding vs. invariant** — the invariant wins, always. The skeptic step enforces it mechanically.
- **Cost** — stage 1 is the expensive run; every stage needs an explicit go. No stage auto-fires.
- **Store timing** — campaign releases ride the GitHub channel; Store submissions stay on their own runbook cadence.
- **Scope creep in waves** — a wave that wants to touch Core behavior beyond its findings gets split; findings-driven or it doesn't ship.

## Out of scope for v0.1

- `:evolve` machinery and loggers (deferred, §7).
- Adapters beyond winui-powershell and web-playwright.
- Figma round-trip tooling (available ad hoc on the winning concept; not part of the loop).
- Any change to the theme engine's required-token contract.
