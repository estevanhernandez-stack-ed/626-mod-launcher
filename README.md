# 626 Mod Launcher (.NET 10 / WinUI 3)

Native Windows mod manager — enables, disables, and organizes mods for your own installed
games, reversibly and atomically. Disabling *moves* a mod to a holding folder; nothing is
ever deleted. This is the native rewrite of the original Electron 626 Labs Mod Launcher,
which serves as the executable spec.

## Status

| Phase | Scope | State |
|---|---|---|
| 1 | `ModManager.Core` + xUnit contract | **done** |
| 2 | WinUI 3 shell (Views/ViewModels, DI host) | **done — full feature surface** |
| 3 | Portable distribution → MSIX + Microsoft Store | **portable build shipping; MSIX/Store next** |

Test suite: **271 green** (`dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`).
The ported xUnit suite is the acceptance contract — a core isn't "ported" until its test passes.

## What it does

- **Games** — register games (popular-game quick-pick or manual), header switcher, Steam launch.
- **Mods** — reversible enable/disable, All On/Off, **MP/SP** loadouts, named **Profiles**.
- **Intake** — drag in a mod (folder or zip); **smart intake** fingerprints it at drop to identify it.
- **Metadata** — fetch real names/descriptions/author credit over the CurseForge proxy; mod rows
  surface `by <author> · downloads · source` (honor the builders).
- **Load order** — reorder mods and apply (engine-specific), reversibly.
- **Saves** — pro save manager: snapshot, 3-way clone, per-type restore, prune, auto-backup on launch.
- **Themes** — 7 built-ins incl. the on-brand **626 Labs** theme, plus an AI theme generator.
- **Engine-aware** — Bethesda, SMAPI, BepInEx, UE-pak, ModEngine2/FromSoft direct-inject, and more.

## Build, test, run

```pwsh
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj   # run the contract (use the explicit project)
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64   # build the app
```

Requires the **.NET 10 SDK**. (A bare `dotnet test` / `dotnet build` at the root hangs building
the WinUI app — always target the explicit project.)

## Portable distribution

```pwsh
dotnet publish src/ModManager.App/ModManager.App.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true
```

Produces a self-contained, **zero-prereq** `win-x64` folder — bundles the .NET runtime, the
Windows App SDK, the `resources.pri` XAML index, and the VC++ runtime app-local, so a friend can
unzip and run with nothing pre-installed. Two MSBuild targets in `ModManager.App.csproj` handle
the PRI + VC++ bundling that `dotnet publish` otherwise drops. Zip the publish folder for hand-off;
see [`GETTING-STARTED.md`](GETTING-STARTED.md) for the install note (incl. the SmartScreen step,
since the build is unsigned for now). Road to the first drop + the Store track:
[`docs/road-to-dist.md`](docs/road-to-dist.md).

## Layout

| Path | What it is |
|---|---|
| `src/ModManager.Core/` | The engine — no UI references. Scanner, Fingerprint, CurseForge client, save manager, load order, themes, intake, metadata, game profiles. |
| `src/ModManager.App/` | WinUI 3 shell — Views/ViewModels (CommunityToolkit.Mvvm), DI host, dialogs, services. |
| `tests/ModManager.Tests/` | xUnit acceptance contract. |
| `docs/` | Rewrite spec, build plan, feature map, save-manager design, road-to-dist, Store release flow. |

## Operating laws (carried from the Electron app)

1. **Honor the builders** — surface attribution, source, donation links, downloads.
2. **Never embed the API key** — proxy or per-user, supplied at runtime.
3. **File ops stay reversible and atomic** — temp-write + rename; disable rolls back on failure.
4. **Core stays UI-free and test-first** — guarded by `CorePurityTests`.

## Spec repo

The original Electron app (working prototype + cross-referenced spec) lives separately at
`mod-manager-builder`. This rewrite mirrors its cores and design docs; see `docs/spec.md`.
