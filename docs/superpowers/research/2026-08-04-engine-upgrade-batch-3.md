# Engine-upgrade batch 3 — the full 40-game sweep

> Mining session 2026-08-04. Five parallel research agents over the entire nexus-only upgrade
> queue, cross-verified against documented loaders per the feed's facts-only rule. Data PR:
> `626-game-manifest` `feat/engine-upgrade-batch-3`. This file is the session record; the PR body
> carries the review summary.

## Verdicts (40 games)

### Upgrades — clean (5)

| game | engine | modPath | notes |
|---|---|---|---|
| red-dead-redemption-2 | custom | `lml` | Lenny's Mod Loader; **banRisk corrected low → high** (Red Dead Online bans) |
| the-witcher-2-assassins-of-kings | custom | `CookedPC` | native file-drop; REDkit editor content is a separate Documents pipeline |
| total-war-three-kingdoms | custom | `data` | .pack drops; activation via launcher mod manager or user.script |
| two-point-hospital | custom | `Mods` | UMM-created game-root folder; every Nexus mod documents it |
| grounded | ue-pak | `Maine/Content/Paks` | direct pak drop; community does NOT use `~mods`; UE4SS BP mods → `LogicMods` beneath it |

### Upgrades — curator calls, flagged in the PR (2)

| game | engine | modPath | the call |
|---|---|---|---|
| nioh-2-the-complete-edition | custom | `mods` | facts solid but sourcing medium (Nexus 403'd direct verification; two secondary sources agree). Spot-check before merge. |
| dragon-s-dogma-dark-arisen | custom | `nativePC` | MT Framework has NO loose-file loading — mods are repacked `.arc` files that REPLACE stock archives. Publishing means treating modPath as an overwrite surface (snapshot-first mandatory, which the launcher does). If that reads wrong, drop the file and settle nexus-only. |

### Settled UNCLEAR — stay nexus-only, reasons recorded (2)

- **tyranny** — three competing documented mechanisms (loose `Data\data` overwrites, Unity Mod
  Manager, a barely-used BepInEx pack); any single modPath misclassifies most of the catalog.
- **grand-theft-auto-v** — three game-root mechanisms (.asi root, SHVDN `scripts`, OpenIV-managed
  `mods` .rpf layer), none a drop-in folder. NEW override adds it **nexus-only with banRisk high**
  — the safety flag is the valuable datum (GTA Online is the canonical ban case).

### Confirmed NEXUS_ONLY_CORRECT — external mod homes (31)

All verified high-confidence against documented sources. These are the **managed-mod-folder
beneficiary list** (see `2026-08-04-managed-mod-folder-seed.md`) — settled, not curation debt:

- **Paradox Documents `mod` convention:** crusader-kings-ii, crusader-kings-iii,
  europa-universalis-iv, hearts-of-iron-iv, stellaris, victoria-3
- **Documents/My Games:** sid-meier-s-civilization-v (`MODS`), sid-meier-s-civilization-vi
  (`Mods`), farming-simulator-22, farming-simulator-25, terraria (tModLoader app)
- **Documents (publisher tree):** the-sims-4 (EA-documented), divinity-original-sin-ii,
  dragon-age-origins, dragon-age-ii (BioWare override trees), neverwinter-nights-enhanced-edition
- **AppData/LocalLow:** baldur-s-gate-3, cities-skylines, factorio, oxygen-not-included,
  two-point-campus (mod.io), warhammer-40-000-rogue-trader (Owlcat UMM), astroneer
  (`%LocalAppData%\Astro\Saved\Paks` — real pak ecosystem, prime future beneficiary)
- **User profile:** project-zomboid (`~\Zomboid\mods`), age-of-empires-ii-definitive-edition
  (per-Steam-ID Games tree)
- **Manager-applied (no folder contract):** dragon-age-inquisition (Frosty),
  dragon-s-dogma-ii (Fluffy + REFramework), mass-effect-legendary-edition (ME3Tweaks M3 across
  three sub-game roots), outer-wilds (OWML's own Mods dir)
- **No modding ecosystem to express:** wo-long-fallen-dynasty (ReShade/CE only; the Nioh-2-style
  enabler died at the DX12 jump), grand-theft-auto-iv (everything sits AT the game root beside the
  exe — no subfolder for a modPath to name; root-drop support would need schema/feature work)

## Queue math

40 in → 7 upgrade candidates (5 clean + 2 flagged) + 2 settled-unclear + 31 settled-external.
Post-merge the honest engine-upgrade queue is EMPTY — future queue entries are new games, and the
31 external homes wait on the managed-mod-folder feature, not on curation.

## The ammunition table — external-mod games: where mods live, where to get them

Este's rule: a true gamer gives their gamer more ammunition. Per confirmed-external game — the
documented mod home and the tool/site that feeds it. (Facts + links only. Candidate content for a
future in-app hint — see the managed-mod-folder seed.)

| game | mods live at | get mods / loader |
|---|---|---|
| crusader-kings-ii / -iii, europa-universalis-iv, hearts-of-iron-iv, stellaris, victoria-3 | `Documents\Paradox Interactive\<game>\mod` | Paradox Mods (mods.paradoxplaza.com) + Steam Workshop; Nexus per-game |
| sid-meier-s-civilization-v | `Documents\My Games\Sid Meier's Civilization 5\MODS` | Steam Workshop; Nexus civilisationv |
| sid-meier-s-civilization-vi | `Documents\My Games\Sid Meier's Civilization VI\Mods` | Steam Workshop; Nexus civilisationvi |
| farming-simulator-22 / -25 | `Documents\My Games\FarmingSimulator20xx\mods` | GIANTS ModHub (mods.giants-software.com) |
| terraria | `Documents\My Games\Terraria\tModLoader\Mods` | tModLoader — free Steam app 1281930 (github.com/tModLoader) |
| the-sims-4 | `Documents\Electronic Arts\The Sims 4\Mods` | EA-documented (help.ea.com); Nexus thesims4 |
| divinity-original-sin-ii | `Documents\Larian Studios\...DE\Mods` | in-game mod menu + Steam Workshop |
| dragon-age-origins | `Documents\BioWare\Dragon Age\packages\core\override` | Nexus dragonage; DAUpdater/DAModder for .dazip |
| dragon-age-ii | `Documents\BioWare\Dragon Age 2\packages\core\override` | Nexus dragonage2; DAModder |
| neverwinter-nights-enhanced-edition | `Documents\Neverwinter Nights\{override,hak,modules}` | Neverwinter Vault (neverwintervault.org); Steam Workshop |
| baldur-s-gate-3 | `%LocalAppData%\Larian Studios\Baldur's Gate 3\Mods` | in-game mod manager (patch 7+); BG3 Mod Manager (github.com/LaughingLeader/BG3ModManager); Nexus baldursgate3 |
| cities-skylines | `%LocalAppData%\Colossal Order\Cities_Skylines\Addons\Mods` | Steam Workshop |
| factorio | `%APPDATA%\Factorio\mods` | in-game portal / mods.factorio.com |
| oxygen-not-included | `Documents\Klei\OxygenNotIncluded\mods\Local` | Steam Workshop; Klei forums for manual |
| two-point-campus | `LocalLow\Two Point Studios\Two Point Campus\Mods` | mod.io via the in-game Mods tab |
| warhammer-40-000-rogue-trader | `LocalLow\Owlcat Games\...\UnityModManager` | UMM built into the game; Nexus warhammer40kroguetrader |
| astroneer | `%LocalAppData%\Astro\Saved\Paks` | AstroModLoader (github.com/AstroTechies/AstroModLoader); Nexus astroneer |
| project-zomboid | `%UserProfile%\Zomboid\mods` | Steam Workshop |
| age-of-empires-ii-definitive-edition | `Users\<you>\Games\Age of Empires 2 DE\<steamId>\mods` | in-game browser / mods.ageofempires.com |
| dragon-age-inquisition | Frosty-managed | Frosty Mod Manager (frostytoolsuite.com); Nexus dragonageinquisition |
| dragon-s-dogma-ii | Fluffy-managed + REFramework autorun | Fluffy Mod Manager (Nexus site mod 818); REFramework (Nexus dragonsdogma2 mod 8) |
| mass-effect-legendary-edition | `Game/MEx/BioGame/DLC/DLC_MOD_*` (M3-managed) | ME3Tweaks Mod Manager (me3tweaks.com); Nexus masseffectlegendaryedition |
| outer-wilds | OWML's own `Mods` dir | Outer Wilds Mod Manager (outerwildsmods.com) |
| wo-long-fallen-dynasty | game root (ReShade only) | Nexus wolongfallendynasty (ReShade presets / CE tables) |
| grand-theft-auto-iv | game root beside the exe | Ultimate ASI Loader (github.com/ThirteenAG); Nexus gta4 |
| grand-theft-auto-v | root / `scripts` / OpenIV `mods` layer | ScriptHookV (dev-c.com), OpenIV (openiv.com) — **single-player only; GTA Online bans** |
| tyranny | fragmented (see UNCLEAR) | Nexus tyranny; UMM for Bag of Tricks |
