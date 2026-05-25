# MP-safety flags — design

- **Date:** 2026-05-25
- **Status:** Approved (shape confirmed with Este)
- **Roadmap:** Phase C5 in [docs/2026-05-25-backlog-roadmap.md](2026-05-25-backlog-roadmap.md).
- **Why:** A live Windrose co-op session showed that data/gameplay mods desync multiplayer while
  cosmetic/client mods are fine — and the app gave no signal which was which. This adds a first-class
  per-mod **MP-compatibility** signal so the user can see, tag, and be warned before a risky mod
  rides into a co-op session.

## Decisions (locked with Este)

| Question | Decision |
|---|---|
| Model | A **separate per-mod MP-compat flag** (not piggybacked on the MP/SP loadout). |
| State | **Effective = user override ?? inferred.** Inferred from the mod's class (live); override persisted. |
| Strength | **Badge + warn**, non-blocking (consistent with B4's confirm-not-gate). |

## State model

- **Inferred** (computed live from the mod's class, never stored):
  - `Safe` — cosmetic / client-side classes (graphics, display, upscaler, co-op).
  - `Risky` — shared-data / gameplay classes (gameplay and the like).
  - `Unknown` — unrecognized class → no claim, no badge.
- **User override** (persisted): `Safe` · `Risky` · `SpOnly` (strongest "never co-op-safe") · `Auto`
  (cleared → fall back to inferred).
- **Effective** = override (if set and not `Auto`) else inferred. Drives the badge + warnings.

## Architecture (pure-core / thin-shell)

### Core — `MpCompat` (pure, unit-tested)

```csharp
public enum MpRisk { Unknown, Safe, Risky, SpOnly }

public static class MpCompat
{
    /// <summary>Infer MP risk from a mod's class/kind. Unrecognized -> Unknown (no claim).</summary>
    public static MpRisk Infer(string? modClass) => (modClass ?? "").ToLowerInvariant() switch
    {
        "graphics" or "display" or "upscaler" or "co-op" => MpRisk.Safe,
        "gameplay" => MpRisk.Risky,
        _ => MpRisk.Unknown,
    };

    /// <summary>The state to show: a real user override wins; else the inferred value.</summary>
    public static MpRisk Effective(MpRisk inferred, MpRisk? userOverride)
        => userOverride is { } o and not MpRisk.Unknown ? o : inferred;
}
```

> The exact class/kind vocabulary comes from the existing `Mod.Class` / DirectInject kinds — verify
> the strings at build and extend the `Infer` map to cover them (the switch above is the
> representative mapping; `tweak`/`dll`/etc. can be slotted Safe/Risky/Unknown as their behavior
> warrants). Keep unrecognized → `Unknown` so the feature never cries wolf.

### Core — `MpCompatStore` (overrides persistence)

Read/write the user overrides map (`modKey -> MpRisk`) at `<dataDir>\mp-compat.json` via
`AtomicJson.WriteJsonAtomic` (atomic per operating law #3). `modKey` is the existing `Scanner.ModKey`.
Missing/corrupt file → empty map (all inferred). A bad/unknown stored value → ignored (fall back to
inferred). Unit-tested round-trip + tolerance.

### App (thin shell, build-verified + smoke-tested)

- **Badge** on the mod row showing `Effective` (MP-SAFE / MP-RISKY / SP-ONLY; nothing for Unknown),
  styled with the theme accents (safe = accent, risky = warning, sp-only = danger).
- **Set-override control** on the row (a small menu: Mark MP-safe / MP-risky / SP-only / Auto) →
  writes via `MpCompatStore`, refreshes the badge.
- **Summary warning** when ≥1 *enabled* mod has `Effective` of `Risky` or `SpOnly` — a status/banner
  line: "N enabled mods may not be co-op-safe." Non-blocking.

## Composes / stays independent (per the model choice)

- Independent of the MP/SP loadout — this is a safety signal, not a loadout mover.
- **Later enhancements (out of scope v1):** an `SpOnly` tag could auto-exclude (or warn within) the
  MP loadout; CF/Nexus metadata + the readme viewer (B3) could feed inference beyond class.

## Error handling

- Missing/corrupt `mp-compat.json` → no overrides, everything inferred. Never blocks mod use.
- Unknown class → `Unknown` (no badge, no warning) — silent, not a false alarm.

## Scope / limits (v1)

- Class-based inference only (no readme/metadata parsing yet — that's the B3/D6 feed, later).
- Badge + warn; no hard block; the warning is a summary, not a per-launch gate.
- Override is per-mod (by `modKey`), per-game (lives in the game's data dir).

## Testing (test-first, pure Core)

`tests/ModManager.Tests/MpCompatTests.cs`:
1. `Infer`: `gameplay` → Risky; `graphics`/`display`/`upscaler`/`co-op` → Safe; an unrecognized
   class → Unknown.
2. `Effective`: a real override wins over the inferred value; `Auto`/`Unknown` override falls back to
   inferred.
3. `MpCompatStore`: write an override + read it back; missing file → empty; corrupt/bad value →
   ignored (inferred).

App badge + set-override + summary warning are build-verified + a live smoke test (a gameplay mod
shows MP-RISKY; tag it SP-only → badge + the "may not be co-op-safe" warning when enabled).

## File structure

- Create: `src/ModManager.Core/MpCompat.cs` — `MpRisk`, `Infer`, `Effective`.
- Create: `src/ModManager.Core/MpCompatStore.cs` — overrides JSON (read/write, tolerant).
- Modify: `src/ModManager.App/ViewModels/ModRowViewModel.cs` — expose `Effective` + the override setter.
- Modify: `src/ModManager.App/MainWindow.xaml` + `.xaml.cs` — badge + set-override control + the summary warning surface.
- Tests: `tests/ModManager.Tests/MpCompatTests.cs`.
