# Ban-risk safety — operating law + manifest flag (warn, gate, never auto-force)

**Date:** 2026-06-15
**Status:** Design — pending spec review
**Branch:** `feat/ban-risk-safety`
**Project:** `DP1YCsh7iAN1yAiR8sAd`
**Research:** grounded 2026-06-15 (internal code, 3 readers + synthesis). Rests on verified facts.

## Problem

Este's standing principle: **the launcher never forces mods onto a game where modding carries a ban risk** (online/kernel anti-cheat — nProtect GameGuard, online EAC, BattlEye). Today the launcher labels per-mod multiplayer risk (`MpCompat`) and offers a reversible, opt-in EAC toggle (`AntiCheat`), but there is **no game-level ban-risk concept** and nothing gates the enable path on a ban-risk title. This slice adds two things: the principle as a canonical operating law, and its first enforcement mechanism — a game-level ban-risk flag that forces a warning and gates enabling, while never refusing the user (reversibility stands).

## Grounded reality (verified)

- **The launcher never auto-enables mods.** Intake only *copies* files ([Scanner.cs:963-1013]); every enable is user-initiated — `ToggleAsync` ([MainViewModel.cs:707-732] → `Scanner.SetLoaderModEnabledAsync` [Scanner.cs:372-391]), `ToggleVariantAsync`, and the bulk paths (`SetAll`/`ApplyMode`/`LoadProfile`). So "block auto-enable" = **gate the explicit disabled→enabled transition** behind a ban-risk acknowledgment.
- **One nuance — placement is enabling for paks.** A pak physically in the load folder loads; on rescan `BuildModList` reports it `Enabled=true` ([Scanner.cs:261]). That's a *read* of disk state, not a launcher write — but it means a dropped-live pak produces an enabled row with **no toggle event**, so the gate can't catch it. The **persistent banner** covers that case; the gate covers deliberate enables.
- **The manifest is descriptive-only** ([GameManifest.cs:24-26]): "it never describes how to enable/disable a mod." A ban-risk flag is *descriptive* (this game carries ban risk) — it fits the law; the *compiled* code decides what to do about it.
- **Existing UX to ride:** per-mod `MpBadge` ([ModRowViewModel.cs:151-164]), the `MpWarning` game-level banner ([MainViewModel.cs:168-175] / [MainWindow.xaml:235-242]), the `ConfirmOwnedToggleAsync` confirm-with-"don't warn again"-checkbox dialog ([MainWindow.xaml.cs:176-200]), and `MpCompatStore` (the per-game override persistence pattern).

## Decisions (recommended — flagged forks below)

- **Field: a string-enum, not a bool.** `string? BanRisk` ∈ `null | "low" | "medium" | "high"` on `GameManifestEntry` *and* runtime `GameEntry`. Mirrors the existing optional-string fields (`NexusDomain`/`SaveDirHint`), parallels the `MpRisk` vocabulary the UX already speaks, and avoids a migration when "warn but don't gate" is wanted. Mapped at the boundary to a pure Core enum `GameBanRisk` (`None/Low/Medium/High`) so nothing switches on raw strings.
- **Resolved live, not persisted on `GameEntry`.** The gate + banner resolve the active game's risk by Steam app id from `EffectiveManifest` via a `BanRiskCatalog` facade (mirroring `NexusDomains.ByAppId`) — *not* a persisted `GameEntry` field. For a safety field this is strictly better: a feed update that flags a newly-known ban-risk game immediately protects players who already added it, with no registry migration and no add-path threading. (Refines the grounding synthesis, which defaulted to persisting on `GameEntry`.)
- **Merge = never-downgrade MAX, not plain coalesce.** This is the one deliberate deviation from the `remote ?? embedded` pattern every other field uses. A safety field must not be silently *lowerable* by an auto-mined remote feed — `remote` can **raise** risk (null/low → high) but can never drop a curated `high`. Mirrors the existing "never downgrade trust" doctrine on `Provenance.Status`. **Load-bearing for safety, not cosmetic.**
- **Gate = warn-and-acknowledge, never hard-block.** The gate informs and requires an explicit acknowledgment; it does **not** refuse. A hard block would contradict the launcher's "honest about what it does, your files, your call" posture and the reversibility law (disable is always available). *(See fork 1.)*
- **Which levels do what:** `high` → gate (prompt) **and** persistent banner; `medium` → banner only (warn, no prompt); `low` → optional banner. Keeps GameGuard/EAC/BattlEye titles behind the ack; lighter-risk games informational. *(See fork 2.)*
- **Ack = per-game-remembered.** A `BanRiskAckStore` twin of `MpCompatStore` (gameId → acked), with a "Don't warn me again for this game" checkbox. **The persistent banner stays visible after ack** — only the repeated prompt is suppressed, so the risk is never hidden. *(See fork 3.)*
- **One pure decision, consulted everywhere.** A pure `ShouldGateEnable(GameBanRisk, alreadyAcked)` Core function is the single source of truth that *every* enable path calls (per-row, variant, bulk), so the gate can't be bypassed by one path forgetting it.

## Forks for review (recommendations baked in)

1. **Gate behavior — warn-and-ack vs hard-block.** Recommend **warn-and-ack**: it honors "we don't *force* mods" by not pushing them frictionlessly *and* not refusing the user's choice; reversibility stands. Hard-block only fits a future "zero mod tolerance" tier the data doesn't support. **This is the one philosophy call I want your explicit nod on** — it shapes the whole UX.
2. **Which levels gate** — recommend `high` gates + banner, `medium` banner-only, `low` optional. (Alternative: only `high` surfaces anything.)
3. **Ack memory** — recommend per-game-remembered + persistent banner stays. (Alternative: prompt every time = nagware that trains users to ignore it.)

## Non-goals

- **Not curating which games are high-risk in this slice.** This ships the *mechanism*; flagging specific titles (Marvel Rivals, etc.) is a follow-up **data** task in the `626-game-manifest` feed repo + embedded snapshot — same mechanism-vs-data split as the deferred Marvel Rivals feed entry. The embedded snapshot ships with no new `high` flags; Core tests prove the mechanism on fixtures.
- **No change to the `AntiCheat` EAC toggle** — that's a reversible state toggle, a different (and complementary) thing. The ban-risk gate is a one-way acknowledgment, not a toggle; it reuses the `ConfirmOwnedToggleAsync` confirm pattern, **not** `AddAntiCheatToggle`.
- **No new engine, no Helldivers support.** This slice makes the operating law real; it does not add HD2 (still deferred new-engine work — and now, under fork 1, likely a declined enable path).
- **No hard refusal** of any enable (see fork 1).

## Architecture

Pure-Core decision + data + ack store; thin App dialog + banner + gate wiring. No new file-op behavior.

### 1. Core — the field, parser + catalog facade (data)

- `GameManifestEntry.BanRisk` (`string?`, after `Featured`) — [GameManifest.cs:38]. Serializes camelCase as `"banRisk"` via existing `ManifestJson.Options`. No runtime `GameEntry` field.
- New pure `GameBanRisk` enum (`None/Low/Medium/High`) + `Parse(string?)` + `Max(a, b)`, next to `MpCompat`: `null`/unknown/garbage → `None`, case-insensitive, mirroring `MpCompat.Infer`'s tolerance.
- New `BanRiskCatalog` facade — a twin of `NexusDomains` ([NexusDomains.cs]): `ByAppId(steamAppId) → GameBanRisk`, built from `EffectiveManifest.Current.Games` (SteamAppId → BanRisk where non-null), cached by `EffectiveManifest.Generation`. The active game's risk is resolved live from its `SteamAppId`, so a feed update applies without re-adding the game.

### 2. Core — merge (never-downgrade)

`EffectiveManifest.MergeEntry` ([EffectiveManifest.cs:83]) gets `BanRisk` via a **MAX** rule, not `??`: `BanRisk = Higher(parse(remote.BanRisk), parse(embedded.BanRisk))` re-serialized to the canonical string. Remote can raise, never lower. A small pure helper (`GameBanRisk.Max`) + a test mirroring the `Provenance.Status` never-downgrade test.

### 3. Core — the gate decision + ack store

- Pure `BanRiskRules.ShouldGateEnable(GameBanRisk level, bool alreadyAcked)` → `true` only for `High && !acked`. Single source of truth for every enable path.
- `BanRiskAckStore` — a twin of `MpCompatStore` (gameId → acked, `AtomicJson`, tolerant load, camelCase). Lives in Core; the App calls it.
- `ManifestValidator` unchanged — `BanRisk` is non-path, so it neither rejects nor needs validation (a bad `ModPath` still fails independently).

### 4. App — the gate wiring + dialog

- A `ContentDialog` modeled on `ConfirmOwnedToggleAsync` ([MainWindow.xaml.cs:176-200]): ban-specific copy (names the game, "account-level / can get you banned online", "disable is always reversible"), a "Don't warn me again for this game" checkbox, returns the user's choice.
- Every enable path resolves the active game's level via `BanRiskCatalog.ByAppId(_ctx.Game.SteamAppId)`, checks `BanRiskAckStore`, and consults `BanRiskRules.ShouldGateEnable(level, acked)` before enabling: `ToggleAsync` ([MainViewModel.cs:718]), `ToggleVariantAsync` ([line 751]), and the bulk paths (`SetAll`/`ApplyMode`/`LoadProfile`). If it returns true, await the dialog; on accept persist the ack (if checkbox) via `BanRiskAckStore` and proceed; on cancel **revert the visual** exactly like the existing catch-block revert (`row.Enabled = !row.Enabled`, [MainViewModel.cs:728]) and enable nothing.

### 5. App — the persistent banner

A `BanRiskWarning` banner parallel to `MpWarning` ([MainViewModel.cs:168-175] + [MainWindow.xaml:235-242]), computed off `BanRiskCatalog.ByAppId(_ctx.Game.SteamAppId)`: `ThemeDanger` for `high` (and `medium` if chosen), **ban-specific copy distinct from the co-op-desync string** ("This game uses anti-cheat — enabling mods for online play can get your account banned."), visible even after the prompt is acked. Covers the dropped-live-pak blind spot the gate can't see.

### 6. Docs — the operating law

Add to the README *Operating laws* block (the canonical four → five) and reference it from this repo's `CLAUDE.md` *What NOT to do*: **"Never auto-force mods onto a ban-risk game. Detection is fine; the enable path on an anti-cheat/ban-risk title must warn and require an explicit acknowledgment, and the launcher never enables without it."**

### 7. Tool — miner parity

`tools/ManifestMiner/OverrideEntry.cs` gets `string? BanRisk` so hand-curated ban-risk in `overrides/` survives a mine (else the safety field that matters most gets silently dropped). Tool-only, never shipped.

## Data shape

`BanRisk` is a new optional string on one persisted shape (`GameManifestEntry`) + a new persisted ack file (`BanRiskAckStore`, gameId → acked). Runtime risk is resolved live via `BanRiskCatalog` — **no `GameEntry` field, no registry migration.** Both persisted shapes are camelCase-on-disk via the existing options / `AtomicJson`. The manifest round-trip test asserts `Contains("\"banRisk\"")` **and** `DoesNotContain("\"BanRisk\"")` (the string-contains is the only guard against a PascalCase regression, since STJ reads case-insensitively). No migration: absent `banRisk` = `null` = `None`.

## Edge cases

- **Dropped-live pak on a `high` game** → enabled row, no toggle event → gate doesn't fire, **banner does** (the designed coverage).
- **Bulk enable / profile apply on a `high` un-acked game** → consults the same `ShouldGateEnable` → gated once (not per-mod nag).
- **Remote feed sends `banRisk: null` over a curated `high`** → MAX merge keeps `high` (no silent un-gate).
- **Acked game** → no prompt on future enables, banner still shows.
- **`medium`/`low` game** → banner per fork 2, never gated.
- **Cancel at the dialog** → visual reverts, nothing enabled, game folder untouched.

## Testing

**Core (xUnit):**
- Round-trip: `GameManifestEntry.BanRisk="high"` serializes `"banRisk"`, not `"BanRisk"`, deserializes back.
- Catalog: `BanRiskCatalog.ByAppId` resolves a flagged game's app id to its level and an unflagged/unknown app id to `None` (mirrors `NexusDomains` tests).
- Merge never-downgrade: remote `null` over embedded `high` → `high`; remote `high` over `null`/`low` → `high`; remote `low` over `high` → **`high`** (never downgrades).
- Parser: `GameBanRisk` maps `high`/`medium`/`low`/`null`/garbage → `High/Medium/Low/None/None`, case-insensitive.
- Decision: `ShouldGateEnable` → true for `High`+not-acked; false for `High`+acked and for `Medium/Low/None` regardless of ack.
- Ack store: `BanRiskAckStore` round-trips gameId→acked through `AtomicJson`; missing/corrupt → empty (tolerant), mirroring `MpCompatStore` tests.
- Validator: an entry with `BanRisk` set + a bad `ModPath` still fails for the `ModPath` (BanRisk neither rescues nor blocks).
- `CorePurityTests` stays green (new decision/store are pure Core).

**App:** not unit-testable (WinUI VM) — build-verified + live smoke (the gate + banner are pure-Core-decided, so the logic is tested; the App is thin wiring).

## Smoke (append to `docs/smoke-tests/pending.md`)

- [ ] Flag a local test game `banRisk: "high"` (manual registry edit or a temporary embedded entry). Enabling a mod prompts the ban-risk acknowledgment naming the game; cancel → nothing enables, row reverts; accept with "don't warn again" → enables, and the next enable does not re-prompt.
- [ ] The persistent ban-risk banner shows on that game and stays visible after the ack; its copy is ban-specific, not the co-op-desync wording.
- [ ] A `medium` game shows the banner but never prompts; a `null`/None game shows neither.
- [ ] Bulk "enable all" / applying a profile on the un-acked `high` game also prompts once (no per-row bypass).

## Surfaces touched

| Path | Change |
|---|---|
| `src/ModManager.Core/Manifest/GameManifest.cs` | `BanRisk` on `GameManifestEntry` |
| `src/ModManager.Core/BanRiskCatalog.cs` | **NEW** — live `ByAppId` facade resolving ban-risk from `EffectiveManifest` (twin of `NexusDomains`) |
| `src/ModManager.Core/Manifest/EffectiveManifest.cs` | `BanRisk` in `MergeEntry` via never-downgrade MAX |
| `src/ModManager.Core/GameBanRisk.cs` | **NEW** — pure enum + parser + `Max` + `ShouldGateEnable` (the single gate decision) |
| `src/ModManager.Core/BanRiskAckStore.cs` | **NEW** — per-game ack persistence (twin of `MpCompatStore`) |
| `src/ModManager.App/ViewModels/MainViewModel.cs` | gate wiring on `ToggleAsync`/`ToggleVariantAsync`/bulk; `BanRiskWarning` banner VM |
| `src/ModManager.App/MainWindow.xaml(.cs)` | ban-risk confirm dialog (ConfirmOwnedToggle-style) + banner binding |
| `README.md` + repo `CLAUDE.md` | the operating law (five rules) |
| `tools/ManifestMiner/OverrideEntry.cs` | `BanRisk` for curated overrides |
| `tests/ModManager.Tests/` | round-trip, never-downgrade merge, parser, gate decision, ack store, validator |
| `docs/smoke-tests/pending.md` | the smoke entries above |
