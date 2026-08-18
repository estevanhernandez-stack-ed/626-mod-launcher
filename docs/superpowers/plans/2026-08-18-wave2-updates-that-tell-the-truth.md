# Wave 2 — updates that tell the truth

**Date:** 2026-08-18 · **Items:** A10, A11, plus the MCP staleness fix Wave 1 exposed
**Why now:** Wave 1 made the agent's view honest. This makes the surface a *person* looks at honest,
and it shares a root cause with the A12 work that just landed.

## The theme

The updates view currently shows things that are not true, and one of them recommends going
backwards. All three items here are the launcher stating something with more confidence than it has.

---

## A10 — the version pair it cannot fill · `S`

**Observed** on a real install: about twenty consecutive rows reading `unknown → 1.2.1`, plus
`1.1 → 1` and `1.0.1 → 1.0.0` — two apparent **downgrades** — and `1 → 1.0`, a no-op. Header said
*115 updates*.

**Not a bug in the decision.** `Mod.UpdateAvailable` and `ModUpdateSummary` already refuse to claim an
update on an unknown installed version, and the comment above the rule records the 98-mod smoke that
put that clause there. The rule is `NexusUpdateAvailable ?? (version compare)`, and Nexus's per-user
flag **outranks** the compare — rightly, because Nexus knows which FILE was downloaded and we often
do not.

**The bug is that the row renders a comparison regardless of why it was listed.** A row pending on
Nexus's authority has no left-hand side, and `VersionText` prints `unknown → …` anyway. So the launcher
shows a comparison it cannot stand behind, in the direction most likely to be acted on.

**Fix.** `PendingUpdate` carries WHY it is pending, and the row says that instead of faking a pair:

- pending because versions differ, both known → `1.2.0 → 1.3.1`, unchanged
- pending on Nexus's flag → *"Nexus says a newer file is available"* — no invented pair
- versions that normalise equal (`1` and `1.0`) → say a newer file is available, never `1 → 1.0`

**Deliberately NOT filtering those rows out.** A version string that looks equal does not mean there
is no update: Nexus tracks files, and a different file can carry the same version text. Dropping the
row would hide a real update to make a display problem go away.

**Not attempting to order arbitrary version strings.** Mod versions are not reliably semver, and a
comparator that guesses would produce a *confidently wrong direction*, which is the fault being fixed.
Where we cannot say which is newer, we do not draw an arrow.

**Tests.** Flag-pending renders no pair; both-known renders the pair; equal-normalising pair renders
the newer-file line; nothing is dropped from the list in any case.

---

## A11 — one multi-option mod becomes several identical rows · `M`

"Faster Ships" appears four times, every row identical, every row `1 → 1.0`. Nexus tracks downloads
per **file**, and that mod publishes four: `FasterShips10`, `FasterShips10_B`, `aaUltraFastShips`,
`aaUltraFastShipsB`.

**Fix.** Group pending updates the way the launcher already groups mods. `PendingUpdate` carries
`NexusModId`, and all four share id 285 — so group on that when present, falling back to the variant
family Wave 1 already surfaces through `GameShape.VariantFamilies`. One row per mod, with its files
named underneath if they are worth showing.

**Same root cause as A12**, which is why it lands cheaply now: that wave established that a family is
one row and several keys, and taught `GameShape` to report both counts. This applies the same idea to
the updates list.

**The count follows the rows.** *115 updates* counting per-file entries is the same overstatement in
badge form.

**Tests.** Four keys of one Nexus mod id become one row; mods with no id fall back to family; distinct
mods never merge.

---

## MCP staleness — folded in from Wave 1 · `XS`

`.mcp.json` runs `dotnet run --no-build`, so the server serves whatever was last built. It sat on a
**nine-day-old binary** through an entire session and nothing surfaced it: no payload carries a
version, so the only way to notice is comparing file timestamps, which nobody does. It cost real
confusion today — stale reads were attributed to a feed gap when the binary also predated two other
fixes.

**Fix.** `get_server_info` reports the Core assembly version and its build timestamp, so an agent can
check its own freshness in one call instead of inferring it from behaviour.

**Also record**, without changing it yet: whether `--no-build` should stay. It buys a couple of
seconds at startup and costs a server that can silently disagree with the repo in front of you. That
is a workflow call for Este rather than a code fix.

**Test.** `get_server_info` reports a version and a build time, and the time is not default.

---

## Sequence

A10 first — it changes `PendingUpdate`, which A11 then groups. The MCP stamp is independent and can
land any time.

## Done when

- No row shows a pair the launcher cannot justify, and none implies a downgrade.
- One multi-option mod is one row, and the badge count matches the rows.
- `get_server_info` reports build freshness.
- Full suite green, CorePurity green, and verified through the MCP against the real install — Windrose
  has the Faster Ships family, so it is the case to check.
