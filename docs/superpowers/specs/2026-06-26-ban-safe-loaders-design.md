# Ban-safe loaders + loaders-in-the-bar — design

**Date:** 2026-06-26
**Status:** Spec (brainstormed in-conversation; grounded against the live catalog machinery). Awaiting review → writing-plans. Launcher feature → ships in a release (both flavors).

## The problem (two halves of one feature)

1. **`banRisk: high` is a dead-end warning.** The manifest flags anti-cheat/online games (Elden Ring, Monster Hunter Wilds), and the gate makes the user acknowledge before enabling — but it only *deters*. Elden Ring mods cleanly through the right loader (Mod Engine 2, Seamless Co-op, or offline) *without* tripping EAC. A warning that doesn't point at the safe path isn't "decent to the modders."
2. **Bespoke loaders aren't reachable from the hub.** Games with `engine: custom` (Cyberpunk REDmod, Witcher 3, RimWorld, Bannerlord, Monster Hunter Wilds/REFramework) — and the FromSoft loaders (Mod Engine 2, Seamless Co-op) — do the actual mod loading themselves. 626 manages the files + saves, but the user has to leave the app to launch the loader.

Both resolve with **one mechanism**: surface the loader as a clickable button in the bar, and teach the catalog which loaders are anti-cheat-safe.

## The model

626 stays the **hub** — it manages mod files (reversibly) and saves, like always. It does **not** try to replace bespoke loaders. Instead it **detects + surfaces** them and, for ban-risk games, **points the warning at the safe one**.

### Component A — Loaders in the bar (reuse the tools-row pattern)

The tools row already surfaces `ToolCatalog` entries (`KnownTool`) as clickable buttons — the WSE Save Editor (Windrose) is the precedent: detect the runnable by `ExpectedRunnableHints`, show `DisplayName`, click → `ToolLauncher` runs it, `EditsSaves` snapshots saves first. Extend this to **loaders**:

- Add loader entries to the catalog (a `KnownTool` with a `LaunchesGame: true` intent, or a parallel `KnownLoader` mirroring the shape — decided in the plan). Each carries `ExpectedRunnableHints` (the loader EXE, e.g. `modengine2_launcher.exe`, `launch_elden_ring_seamlesscoop.exe`, REFramework's runner), `DisplayName` ("Launch via Mod Engine 2"), `Engine`/`SteamAppId` scoping, `GetUrl`, and `Author`.
- Detect the loader the same way tools are detected (installed runnable present in the game folder) → surface a launch button in the bar. Click → launch the loader (snapshot-first where it touches saves).
- **Never bundle the loader.** Catalog metadata + detection + `GetUrl` only — the user installs the loader; 626 finds it and makes it one click (honors the NOTICE "never bundled" law). When absent, show the "Get it here" chip like tools do.

Result: 626 manages mods + saves; the surfaced loader does the loading; the user reaches it from one place.

### Component B — Ban-safe self-identification

Add a `BanSafe` (anti-cheat-safe) attribute to the catalog records — `KnownTool`/the loader record, `KnownFramework`, and `KnownDirectInjectMod`. A tool/loader declares whether its modding path avoids the game's anti-cheat:

- Mod Engine 2 — loads mods without modifying the base install → `BanSafe: true`.
- Seamless Co-op — runs its own multiplayer, bypasses EAC → `BanSafe: true`.
- The offline EAC toggle (FULL only) — sidesteps EAC → the safe path on FULL.
- A raw direct-inject DLL that loads EAC-side online → not ban-safe.

This is data on the catalog entry, not new logic — the entries self-identify.

### Component C — The ban-risk gate points at safety

When `BanRiskRules.ShouldGateEnable` fires (the game is `banRisk` medium/high) and the user hits the warning (`ConfirmBanRiskEnableAsync`), the dialog now **also surfaces the available ban-safe loaders** for that game — "This game uses anti-cheat. The safe way to mod it: [Mod Engine 2] [Seamless Co-op] [Play offline]." Pulled from the catalog's `BanSafe` entries scoped to the game (detected or `GetUrl`-linked if not installed). The warning becomes guidance; the acknowledgment stays (we still never force).

## Flavor handling

Works in **both** STORE and FULL — the surfaced loaders are catalog tools (not the EAC-disable toggle, which is FULL-only and stripped from STORE). **This matters most on STORE:** with the offline toggle gone there, the ban-safe loaders (Mod Engine 2, Seamless Co-op) are the *primary* safe path the gate must surface. No `#if FULL` on the loader-surfacing or the ban-safe guidance; the offline-toggle option simply doesn't appear on STORE.

## Non-goals

- **626 does not natively load bespoke-loader games.** It surfaces + launches the real loader; the loader loads. (`engine: custom` + `modPath` stays the model — 626 manages the files.)
- **No bundling loader binaries.** Catalog metadata + detection + `GetUrl` only (the NOTICE law).
- **No silent anti-cheat disabling.** The ban-risk gate + acknowledgment stay; this adds guidance, never removes the warning or auto-acts.
- **No new manifest field required.** Game-level `banRisk` stays in the manifest; the safe-path knowledge lives in the tool/loader catalog (tools self-identify), so growing it needs no feed change.

## Reuses (existing machinery)

`ToolCatalog` / `KnownTool` / `ToolLauncher` / the tools-row VM (the button surface) · `KnownFramework` (Mod Engine 2, UE4SS) · `KnownDirectInjectMod` (Seamless Co-op) · `BanRiskCatalog` / `GameBanRisk` / `BanRiskRules.ShouldGateEnable` · `ConfirmBanRiskEnableAsync` (the warning UX) · the FromSoft framework intake (validate-then-extract). The EAC `LaunchOption` (FULL) is the offline-path precedent.

## Success criteria

- A bespoke/ban-safe loader installed in a game folder shows as a clickable launch button in the bar (both flavors); click launches it; saves snapshot first where applicable.
- Catalog entries carry `BanSafe`; Mod Engine 2 + Seamless Co-op are marked safe.
- The ban-risk gate dialog lists the game's ban-safe loaders (installed → launch; absent → "Get it here"), warning intact.
- STORE: the safe-loader guidance appears even though the offline toggle is absent.
- No bundled binaries; reversibility + the warn-and-ack law untouched.
