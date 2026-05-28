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
- Registry / settings (`src/ModManager.Core/Registry.cs`)
- Tool registry (`src/ModManager.Core/Tools/ToolRegistry.cs`)
- `ModMeta` `installedUtc` + `sourceConfidence` (`src/ModManager.Core/Mod.cs`)

If you're adding a new on-disk shape and it isn't in this list, add it.
