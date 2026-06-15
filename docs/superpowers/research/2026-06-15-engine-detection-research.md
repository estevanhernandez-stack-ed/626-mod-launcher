# Research — engine detection deepening (probe depth, UE layouts, Helldivers 2 mechanism)

**Date:** 2026-06-15
**Method:** research-verify Workflow — 5 code readers (Explore) + 3 external tracks each paired with an adversarial verifier + a synthesis pass. All four external verdicts returned `refuted=false`. Load-bearing facts corroborated two ways (repo code + web sources).
**Feeds:** [`2026-06-15-engine-detection-probe-deepening-design.md`](../specs/2026-06-15-engine-detection-probe-deepening-design.md)

## Code reality (high confidence — read directly + agent-confirmed)

- **`EngineScan.Probe`** ([EngineScan.cs:25-26](../../../src/ModManager.App/Services/EngineScan.cs#L25-L26)) — App-IO. `HasContentPaks = Exists(root/Content/Paks) || subs.Any(s => Exists(s/Content/Paks))`. `subs` is one `Directory.GetDirectories(root)` call. Root + exactly one level.
- **`EngineDetect.GuessEngine`** ([EngineDetect.cs](../../../src/ModManager.Core/EngineDetect.cs)) — pure Core, zero `using`s. Priority: bepinex > melonloader > ue-pak (`HasContentPaks`) > bethesda > smapi > source > bare-Unity→bepinex > null. 9 tests pin it; `CorePurityTests` bans UI deps. **No change needed for Tier 2** — fix the probe and `ue-pak` lights up.
- **`EnginePresets.DetectUePakModLocation`** ([EnginePresets.cs:111-130](../../../src/ModManager.Core/EnginePresets.cs#L111-L130)) — pure Core, one-level walk, passes a *leaf* `project` name. `BuildGameEntry` honors explicit `input.ModPath` absolutely ([:66-74](../../../src/ModManager.Core/EnginePresets.cs#L66-L74)) — this is why a Tier-1 feed entry needs zero app code.
- **`ModLocator.UnrealProjects`/`Detect`** ([ModLocator.cs:41,56-68](../../../src/ModManager.App/Services/ModLocator.cs#L41-L68)) — App-IO, **separate** install-target picker. Finds top-level subfolders containing a `Content` dir; auto-seeds only when `projects.Count == 1` (the "don't guess" discipline).
- **`ModLocations.UePakModLocation`** ([ModLocations.cs:73-76](../../../src/ModManager.Core/ModLocations.cs#L73-L76)) — single shared primitive; `Path.Combine(project, "Content","Paks",…)`, so a **multi-segment** `project` flows through unchanged. `Candidates` ([:28-36](../../../src/ModManager.Core/ModLocations.cs#L28-L36)) likewise.
- **`PakClassifier.IsBaseGamePak`** — pure, name+size. Regex requires `-WindowsNoEditor` (UE4). **UE5 ships plain `pakchunk0-Windows.pak`** — the regex misses it (gap for the scoring signal; Marvel Rivals is UE5).
- **`games-manifest.json`** — Palworld `modPath: "Pal/Content/Paks/~mods"`, Hogwarts `"Phoenix/Content/Paks/~mods"` confirm the nested-`modPath` pattern. `KnownEngines` maps `appId→engine` for `known-engines`-tagged entries with both `steamAppId` + `engine`. Marvel Rivals + Helldivers 2 are **absent**.

**The trap:** three detection sites (gate / seeder / picker) all at one level. Deepen only the gate → Marvel Rivals detects but mods route to the wrong (root) folder. Must move all three in lockstep; `project` becomes a relative path.

## UE on-disk layouts (high confidence — code + web; some path strings search-surfaced)

- **Marvel Rivals (UE5, double wrapper):** `<GameRoot>/MarvelGame/Marvel/Content/Paks/~mods` — `Content` is two wrappers deep. `subs=[MarvelGame]`, `MarvelGame/Content/Paks` doesn't exist → `HasContentPaks=false` → no auto-detect. **Probe-depth bug confirmed by code, not just sources.**
- **Single-wrapper is the norm (Paks one wrapper deep):** Ready or Not (`ReadyOrNot`), Palworld (`Pal`), Hogwarts Legacy (`Phoenix`), Black Myth Wukong (`b1`). Wrapper = arbitrary UE project short-name → **not string-guessable, must detect structurally.**
- **No-wrapper exists:** S.T.A.L.K.E.R. 2 (`Stalker2/Content/Paks/~mods`, install root *is* the project).
- **No shipped 3-wrapper game found** — deepest verified is Marvel Rivals' two. Capping the probe at 2 wrappers covers the whole verifiable corpus (absence-of-evidence on 3+, flagged; revisit if one surfaces).
- **`~mods` is a direct child of `Paks`** (user/manager-created, leading `~` mounts it late). UE4SS `LogicMods` is a **separate** sibling — the launcher target is `~mods`, not `LogicMods`.
- **Multiple `Content/Paks` per install is common:** `Engine/Content/Paks` beside `<Project>/Content/Paks` (Fortnite has `Engine/` + `FortniteGame/`). The game mod path is the project one, **never** `Engine`.

## False-positive guard (high confidence)

- **#1 false positive: `Engine/`** — every shipped UE game ships it as a same-depth sibling of the project, with its own `Content`. *Prefer-shallowest alone cannot beat it* (same depth) — the denylist is what kills it deterministically.
- **AC dirs** (`EasyAntiCheat`, `BattlEye`) live under `Binaries`, not `Content/Paks` — but stay on the skip list; intake into AC dirs is a hard no (`AntiCheat.cs` treats them as a reversible-toggle surface, not a mod dir).
- **Sub-game / `*Server` / SDK build folders** can ship a valid-looking `Content/Paks` → a multi-match, not a no-match. The `projects.Count==1` "don't guess" discipline must survive deepening.
- **Recommended guard (ranked):** skip-denylist (`Engine`/`Binaries`/AC/redist/`*Server`) → prefer-shallowest → score by exe/shipping-pak sibling (`Binaries\Win64\*-Shipping.exe` and/or `PakClassifier` shipping-name match) → `.uproject` tie-breaker bonus only → multi-match = no auto-pick. Backstopped on the write side by the validate-then-extract forbidden-paths gate.
- **Known gap:** the shipping-pak scoring signal needs the UE5 `-Windows.pak` regex variant (above) or it's unreliable for UE5 titles.

## Helldivers 2 — Tier 3 scoping (high confidence on mechanism; flagged items for go/no-go)

- **Engine:** Bitsquid / Autodesk Stingray — proprietary, discontinued, no SDK/official mod support. **New-engine work**, shares nothing with the UE probe.
- **Mods are binary repacked Stingray archives** (not loose files): a triad sharing a base name — `NAME.patch_N` + optional `.gpu_resources` + optional `.stream`. Binary layout confirmed via Zekfad hexpat.
- **All current mods patch one base archive `9ba626afa44a3aa3`** → canonical shape `9ba626afa44a3aa3.patch_N(.gpu_resources/.stream)`. Fingerprint can key on hash prefix + `.patch_N`, **but the hash/tables are a moving target** that can shift on Arrowhead updates (re-verify live).
- **No loader.** Files drop into `<install>/data/` co-located with the base archive — no mods folder, no VFS. Enable = copy in ("deploy"); disable = remove ("purge"). **Load order = ascending `N`** (higher wins); two mods at `patch_0` collide — the real enable/order op is **renaming the triad to a unique patch number** (Vortex #16582).
- **Reversibility conflict:** "purge" = delete, which the launcher forbids in toggle paths → disable must *move* the triad to a holding folder and re-enable must move it back + re-assert the patch number, **all three files atomically** (partial-triad likely crashes the loader — needs a test).
- **Product gate:** nProtect GameGuard; EULA prohibits file modification; mesh/model mods flagged as auto-ban risk → any HD2 slice needs an explicit risk warning and likely cosmetic/client-side scope. **Go/no-go owed before any code.**
- **Interop:** HD2MM / Arsenal / Vortex all write `/data/` with their own numbering → need a ToolOwnership-style guard like the existing Vortex/MO2 handling.

**Deferral rationale:** new-engine + a non-trivial reversible enable/disable primitive + a moving-target fingerprint + an unresolved anti-cheat product call. Its own slice once verified live and the GameGuard scope decision is made.

## Open items carried into build

- Verify Marvel Rivals store id before any Tier-1 feed entry (Epic-primary; Steam `2767030` not code-grounded — consider `epicAppName`).
- Confirm whether `Engine/Content/Paks` (the `/Paks` subfolder specifically) is always present, and whether UE5 engine paks dodge `PakClassifier` and could surface as fake mod rows — check against a real install during build.
- Add the UE5 `-Windows.pak` regex variant before relying on shipping-pak-name as a project-Content/Paks confirmation signal.

## Sources

UE layouts: Epic directory-structure + packaging docs; unofficial-modding-guide.com; per-game modding wikis/guides (Marvel Rivals via Nexus articles/351 + gamerant — search-surfaced, direct fetch 403; Palworld, Hogwarts, Black Myth, Stalker 2); pwmodding.wiki (UE4SS LogicMods). Helldivers 2: PC Gamer + Wikipedia/Bitsquid (engine); HD2-Modding-Wiki (terms/overview/manifest); Zekfad/helldivers hexpat (binary format); Vortex issue #16582 (load order/rename); Nexus mods 109/4664/site-845; v4n00/h2mm-cli.
