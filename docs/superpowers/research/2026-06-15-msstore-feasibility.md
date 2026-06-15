# Microsoft Store feasibility — 626 Mod Launcher

**Date:** 2026-06-15
**Method:** research-verify Workflow — repo grounding + current MS Store policy (web) + MSIX packaging path (web), with the two load-bearing claims adversarially verified against live Microsoft Learn policy.
**Verdict:** **Conditional yes.** A feature-reduced Store SKU is viable and clears most policy hooks; the anti-cheat-disable toggle is the load-bearing review risk. Staying GitHub-only is the zero-cost fallback.

## The honest bottom line

Yes, with a catch — and the catch is smaller than it first looks. The core mod manager (a full-trust WinUI 3 MSIX that writes to game folders) is a normally-accepted Store product class. The one real risk is `AntiCheat.cs` — the EAC-disable bootstrapper swap. **The cleanest move is to strip that one feature from a Store SKU and keep the full feature set on GitHub/Velopack.**

## The adversarial correction (important — don't overstate this)

My going-in assumption was "EAC-disable = hard Store dealbreaker." **Verified false as stated.** Checked against live policy (Store Policies v7.19 eff. 2025-10-14, Developer Code of Conduct, Unwanted/Malicious-Software criteria):

- **No Store policy clause names "anti-cheat," "cheating," or "mod manager."** None.
- The risk is **reviewer discretion**, inferred from three general clauses, each a genuine stretch:
  - **Code of Conduct §3** — "inducing users to ... violat[e] first or third party EULAs." A reversible, opt-in, warn-and-acknowledge toggle is arguably *enabling user choice*, not *inducing*.
  - **Store Policy 10.2** — "will not disable any platform safety or comfort features." In context that's *Windows/Xbox platform* features; EAC is third-party game DRM, not a Microsoft platform feature — a stretch.
  - **Malware "Tampering software"** criteria — threat-framed ("evade defense mechanisms"), names AV/EDR, not game anti-cheat.
- **No documented rejection precedent**, and **no positive-approval precedent** (Vortex / MO2 self-select off-Store; cause unverified).

So the defensible read is **"high-risk / plausibly-disqualifying at reviewer discretion,"** NOT "policy prohibits it." Shipping the full feature set to the Store is a coin-flip on one reviewer's read, with a burned listing if it loses. Stripping the feature removes the bet entirely.

## The prize (why bother)

MSIX-via-Store buys, verified-from-docs, for **$0–99 one-time**:
- **Free Microsoft re-signing** + **zero SmartScreen warnings** on install — this is the whole economic case (today's unsigned Velopack installer trips SmartScreen; RELEASE.md accepts that as the v1 trade).
- **Silent Store-managed updates** (staged rollouts + differential downloads) — replaces the Velopack updater for that SKU. `UpdateChecker` already no-ops when not a Velopack install, so it stands down cleanly.

**Do NOT** submit the existing Velopack EXE via the Store's MSI/EXE path — Microsoft does **not** re-sign that, so you'd still buy a cert + grind SmartScreen + get no Store updates. That path defeats the point. Go MSIX.

## Blockers (ranked)

| Sev | Issue | Clear it by |
|---|---|---|
| **Dealbreaker (for the *full* SKU only)** | `AntiCheat.cs` EAC-disable (bootstrapper-exe swap) — plainly an anti-cheat bypass to a reviewer reading the source. High-risk at discretion, not a black-letter ban. | **Strip/disable it in the Store build via a build-flavor gate** (recommended). Or gate off-by-default (weaker — bypass code still in the binary). Or stay GitHub-only. |
| **Major** | In-app Nexus/CurseForge browsing → third-party-storefront + UGC obligations (Store Policy 10.1.6, 11.12, 11.13): permitted-by-their-ToS, content guidelines, report mechanism, takedown-on-request. (Clients are metadata-only — no binary redistribution — which helps 11.2.) | Confirm Nexus + CurseForge API ToS permit a redistributed client to browse/download (UNVERIFIED — needs a ToS read), **or** ship the Store SKU with the in-app browser disabled (manage-your-own-files only) — pairs naturally with the EAC-stripped SKU and sheds 11.12/11.13. |
| **Major** | Ban-risk gate is designed (2026-06-15) but **not yet implemented**, and feed flagging is pending. Until live, the enable path on online-AC titles has no formal warn-gate — exactly what a reviewer probes. | Land the ban-risk gate + flag titles in the feed **before** any submission. (If the EAC toggle is stripped anyway, this surface shrinks — the two reinforce.) |
| **Minor** | Privacy policy mandatory (10.5.1 — the Nexus key is PII); accurate metadata (10.1.1); dependency disclosure (10.2.4); categorize as **Utility, not Game** (dodges 10.2.5 / 10.13 gaming rules). | Routine submission line-items; budget them. |
| **Minor** | `runFullTrust` is a restricted capability (routine, but triggers the restricted-cap submission gate). | Declare `runFullTrust` **only**. **Correction to `release-msstore.md` (lines ~14-15, 28-29): do NOT declare `broadFileSystemAccess`** — a full-trust mediumIL desktop MSIX writes to user-writable folders via ordinary Win32 I/O subject to NTFS; `broadFileSystemAccess` is a UWP/sandbox concept that, if declared, is a *scrutinized* capability requiring you to justify why file pickers are insufficient. Declaring it invites review friction the app doesn't need. |

## Packaging path (if pursued)

~**1–3 weeks calendar**, mostly project-config + first-submission iteration, not deep code. **$0–99 hard cost**, $0 ongoing.

0. **Decide the SKU shape first** (gates everything): Store SKU = EAC toggle off + optionally in-app browser off, via a build-flavor gate; full set stays on Velopack/GitHub.
1. **Partner Center** — individual registration free as of Sept 2025; company $99 one-time. *(UNVERIFIED: whether the free individual tier can grant `runFullTrust`, or if the $99 company account is required for that capability tier — confirm before assuming $0.)*
2. **Convert WinUI 3 unpackaged → packaged MSIX** (single-project MSIX or a `ModManager.Package` project); manifest with `runFullTrust` only. Audit own-exe-location reads, single-file extraction, the `VelopackApp.Build().Run()` entry, the app-local VC++/resources copy targets. ~3–7 days; `CorePurityTests` already isolate the platform edges.
3. **Self-contained vs framework-dependent** MSIX — decide before packing.
4. **Signing: $0** — Microsoft re-signs post-cert; zero SmartScreen.
5. **Updates** — Store-managed; build-flavor-gate `UpdateChecker` out of the Store flavor.
6. **Submission line-items** — privacy policy, metadata, dependency disclosure, Utility category, `runFullTrust` description.
7. **First cert round-trip** — budget calendar time; expect a question about writing into other publishers' game dirs (answer: "load-order utility for the user's own files, never bundles third-party binaries — see NOTICE").
8. *(optional)* manual-dispatch `release-msstore.yml` building the `.msixbundle`.

## Decisions for Este

1. **Dual-channel vs GitHub-only.** The Store buys free signing + no SmartScreen + silent updates for the non-power-user crowd; the price is a deliberately lesser Store SKU (no EAC toggle, maybe no browser) + ongoing two-SKU maintenance. Worth it for legitimacy/reach, or is GitHub the right home?
2. **EAC toggle handling** if you go Store: strip it (clean, recommended) / gate off-by-default (weaker) / bet the full SKU clears review (coin-flip, no precedent either way).
3. **Maintenance appetite** — one codebase, a thin build-flavor gate (EAC surface, browser, UpdateChecker), but every release ships two artifacts and the Store one round-trips cert. One-time legitimacy play, or a channel you'll tend indefinitely?
4. **Sequencing** — build the ban-risk gate + feed flagging first *regardless* (it's the "honest about what it does" story a reviewer wants, and it's good for the GitHub build too).
5. **Pre-submission ToS read** on Nexus + CurseForge (10.1.6) — cheap, do it before any packaging work; if either says no, the Store SKU ships browser-disabled.

## Confidence

**Verified-from-policy (high):** Store accepts full-trust desktop MSIX; MSIX-via-Store = free re-sign + no SmartScreen + Store updates; full-trust MSIX needs only `runFullTrust` (not `broadFileSystemAccess`); privacy policy mandatory for PII; UGC/storefront obligations attach to in-app browsing; Partner Center free individual / $99 company. **Verified-from-repo (high):** unpackaged + Velopack; `AntiCheat.cs` reversible EAC bootstrapper swap; ban-risk gate designed-not-built; `release-msstore.md` staged with the `broadFileSystemAccess` error. **Inferred / case-by-case (the load-bearing uncertainty — do not overstate):** the EAC-toggle review risk is reviewer discretion from three general clauses, no clause names anti-cheat, no rejection or approval precedent. **Unverified open items:** Nexus/CurseForge API ToS for a redistributed client; whether the free individual account grants `runFullTrust`; whether any code reads its own EXE path in a way MSIX breaks (not audited). Policy citations as-of Store Policies v7.19 (eff. 2025-10-14).
