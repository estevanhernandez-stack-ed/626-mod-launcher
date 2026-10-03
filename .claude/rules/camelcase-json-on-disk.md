# Rule: camelCase JSON on disk

## The convention

Every file the launcher writes to disk uses **camelCase JSON keys**. No exceptions, no per-shape overrides.

## Why

The launcher historically shared on-disk JSON with an Electron predecessor that produced camelCase. User installs in the field carry that shape. Snake_case or PascalCase keys silently break round-trips against existing user data — the file deserializes to default values, the user thinks their settings got wiped.

This is the one place "but the legacy" outranks "but C# convention."

## How

Configure `JsonSerializerOptions` once per write site:

```csharp
private static readonly JsonSerializerOptions JsonOpts = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true, // optional — pretty-prints for human-readable state files
};
```

Use those options on every `JsonSerializer.Serialize(...)` and `JsonSerializer.Deserialize<T>(...)` call. **Don't pass `null` or the default options** to either method — that's PascalCase by default in System.Text.Json and will break the convention silently.

`AtomicJson` in `src/ModManager.Core/AtomicJson.cs` is the canonical wrapper for the atomic-write side. If you're writing JSON state, use it (or follow its pattern).

## The test

Every new persisted shape ships with a round-trip test:

```csharp
[Fact]
public void FooConfig_RoundTripsAsCamelCase()
{
    var original = new FooConfig { SomeProperty = "value", OtherThing = 42 };

    var json = JsonSerializer.Serialize(original, JsonOpts);
    Assert.Contains("\"someProperty\"", json); // camelCase key on disk
    Assert.DoesNotContain("\"SomeProperty\"", json);

    var roundTripped = JsonSerializer.Deserialize<FooConfig>(json, JsonOpts);
    Assert.Equal(original.SomeProperty, roundTripped!.SomeProperty);
    Assert.Equal(original.OtherThing, roundTripped.OtherThing);
}
```

The string-contains assertion is what protects you — without it, the round-trip passes whether the keys are camelCase or PascalCase, because System.Text.Json deserializes case-insensitively by default.

## The reviewer

`catalog-entry-reviewer` flags new persisted shapes that don't set the policy. The `check-camelcase-json` hook (`.claude/hooks/check-camelcase-json.ps1`) does a best-effort PostToolUse grep on Edit/Write touches and warns when a likely-new serializer call lands without the policy set.

## Surfaces this rule already governs

- `DirectInjectConfigOverrides` (`src/ModManager.Core/Catalog/DirectInjectConfigOverrides.cs`)
- `FrameworkInstaller` / `FrameworkRegistry` install manifests (`src/ModManager.Core/Frameworks/`)
- Profile / loadout state (`src/ModManager.Core/GameProfile.cs`, `Profile.cs`)
- Theme files (`src/ModManager.Core/Themes.cs`)
- Registry / settings (`src/ModManager.Core/Registry.cs`; `GameEntry.manifestId`, the entry a game was added as, covered by `ManifestIdentityTests.ManifestId_round_trips_as_camelCase_and_is_omitted_when_null`; `GameEntry.loaderCheckedExeUtc`, the executable date the stale-loader chip was marked checked against, covered by `StaleLoadersTests.LoaderCheckedExeUtc_round_trips_as_camelCase`)
- Tool registry (`src/ModManager.Core/Tools/ToolRegistry.cs`)
- `ModMeta` `installedUtc` + `sourceConfidence` (`src/ModManager.Core/Mod.cs`)
- Restore-point manifest (`src/ModManager.Core/RestorePoints/RestorePointManifest.cs`), including the
  vanilla turn-off record on each game (`turnedOffByClear` / `turnOffSkipped` / `dataDir`, schema 2;
  round-trip in `RestorePointManifestTests.Turn_off_record_round_trips_as_camelCase`) and the held-copy
  record (`heldCopies: [{ name, files: [{ rel, bytes, sha256 }] }]`;
  `RestorePointManifestTests.Held_copy_record_round_trips_as_camelCase`), and the vanilla-remainder
  record (`vanillaRemainder: [{ rel, bytes, sha256 }]`, `leftInPlace: [{ path, reason }]`;
  `SafeClearRemainderTests.Remainder_record_round_trips_as_camelCase`)
- Game-definition manifest `modPathModOnly` (`src/ModManager.Core/Manifest/GameManifest.cs`, cached on disk by
  `RemoteManifestCache`; `ModPathModOnlyTests.Round_trips_as_camelCase`)
- `TakenOverState` taken-over.json (`src/ModManager.Core/VortexTakeover.cs`)
- `VanillaStash` vanilla-stash.json (`src/ModManager.Core/VanillaLaunch.cs`)
- `NexusOAuthConfig` nexus-oauth-cache.json (`src/ModManager.Core/Nexus/NexusOAuthConfig.cs` — `JsonOpts`; written/read via `src/ModManager.App/Services/NexusOAuthConfigSource.cs`)
- `AppSettingsService` app-settings.json (`src/ModManager.App/Services/AppSettingsService.cs` — camelCase keys merged one at a time by `AppSettingsFile.WriteKey` in `src/ModManager.Core/AppSettingsFile.cs`, locked, atomic temp+rename, shared with the agent's `apply_theme`; per-key tolerant loads; covered by `tests/ModManager.App.NexusValidate.Tests/AppSettings*Tests.cs` and `AppSettingsFileTests.WriteKey_merges_one_key_and_keeps_every_other_setting`)
- `ModNameIndex` nexus-name-index.json (`src/ModManager.Core/Discovery/ModNameIndex.cs` — per-game Nexus name cache; written via `AtomicJson` by `ModNameIndexSource`)

- `ModInstallManifest` per-install records (`src/ModManager.Core/ModInstallRegistry.cs` — written to
  `<dataDir>/installs/<installId>.json` via `AtomicJson`; what an intake actually placed, so a row can
  say which files are its own; optional `locationPath`, where the location was when 626 installed, covered by
  `SafeClearRound8Tests.The_location_path_round_trips_as_camelCase_and_an_old_record_loads_without_it`)

- `DataDirMoveJournal` pending-moves/<id>.json (`src/ModManager.Core/DataDirMoveJournal.cs` — the A6
  breadcrumb for a data-folder move, written via `AtomicJson`; covered by
  `DataDirMoveJournalTests.A_record_round_trips_as_camelCase`)

- Game manifest `loaders` list (`src/ModManager.Core/Manifest/GameManifest.cs` — `LoaderManifestEntry`,
  serialized with `ManifestJson.Options`; written by the miner to `games-manifest.json`, curated in
  `overrides/loaders/*.json`; covered by `ManifestLoadersTests.Loaders_round_trip_as_camelCase`)

- Game manifest `extraModTrees` (`src/ModManager.Core/Manifest/GameManifest.cs` — `GameManifestEntry.ExtraModTrees`,
  serialized with `ManifestJson.Options`; curated in `overrides/*.json`; covered by
  `ModTreesTests.ExtraModTrees_round_trips_as_camelCase`)

If you're adding a new on-disk shape and it isn't in this list, add it.
