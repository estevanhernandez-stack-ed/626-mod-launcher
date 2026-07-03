# Supported-games surface Implementation Plan

> **For agentic workers:** Executed INLINE (superpowers:executing-plans) by decision — single small feature in the 626-game-manifest repo. Spec: `docs/superpowers/specs/2026-07-03-supported-games-surface-design.md`.

**Goal:** CI-generated `SUPPORTED-GAMES.md` + `supported-games.json` + `badge.json` in the feed repo, committed on the same rail as the signed manifest; README badge + link.

**Architecture:** One stdlib-Python generator (`tools/generate-public.py`) with an embedded `--self-test`; two CI steps (self-test gate, then generate); the publish step's `git add` grows to include the three files.

## Global constraints
- Python 3 stdlib only; deterministic output (sorted by name, stable field order); timestamp only from `--generated-utc`/`SOURCE_DATE` (no clock reads in the generator body).
- JSON camelCase; optional fields **omitted**, never null. Badge color hex **without** `#` (shields endpoint convention — cosmetic deviation from the spec's `#17d4fa`).
- Tier derivation mirrors the launcher facades: engine → engine-curated; else nexusDomain → nexus-only; anything else skipped defensively (the published manifest should contain none).
- Same-commit rule: CI generates from the *fresh* miner output and commits with the manifest — drift structurally impossible.

## Task 1: `tools/generate-public.py` (feed repo) — full code carried in the implementation commit
- `--self-test`: 3-game embedded fixture (engine-curated+featured, nexus-only, steam-less engine-curated) asserting counts, tier split, badge message, URL derivation, omitted-fields rule, markdown row presence/absence. Exit non-zero on failure.
- Main path: read manifest → project → write the three files to `--out-dir`.
- Gate: self-test passes locally AND a real run against the live 143-game `games-manifest.json` produces sane output (counts match the known 103/40 split; spot-check Elden Ring row + DS2 row).

## Task 2: CI + README wiring (feed repo)
- `build-manifest.yml`: after Mine+curate+sign — `python3 tools/generate-public.py --self-test` then `python3 tools/generate-public.py launcher/tools/ManifestMiner/out/games-manifest.json --out-dir "$GITHUB_WORKSPACE" --generated-utc "$(date -u +%Y-%m-%dT%H:%M:%SZ)"`; publish step adds the three files to `git add`.
- `README.md`: shields endpoint badge + "See the full [supported games list](SUPPORTED-GAMES.md)" line.
- Gate: PR → merge → the triggered CI run commits all three files; badge renders; SUPPORTED-GAMES.md renders correctly on GitHub.

## Verification checklist
- [ ] self-test green locally + in CI
- [ ] real-manifest run: counts 143/103/40, Elden Ring engine-curated + featured 3, DS2 decima row present
- [ ] CI run after merge commits manifest + 3 public files in one commit
- [ ] raw URLs fetch (supported-games.json + badge.json); badge renders via shields endpoint
- [ ] launcher untouched (no changes in this repo beyond docs)
