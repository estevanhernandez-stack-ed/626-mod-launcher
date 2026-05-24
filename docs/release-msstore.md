# Microsoft Store release flow (Phase 3 — staged, not yet wired)

> **Status: staged for the right time.** This documents the intended release path so it's
> ready when Phase 2 (the WinUI shell) is done. Nothing here runs yet — packaging is Phase 3.

The launcher is framed for the Store as a **load-order utility for the user's own installed
game files** — it never redistributes mods or copyrighted content (honors operating law #1).

## Shape of the flow

1. **Packaging project** — add `ModManager.Package` (Windows Application Packaging, MSIX) or
   single-project MSIX on `ModManager.App` (`<EnableMsixTooling>true</EnableMsixTooling>`,
   `Package.appxmanifest`). Declare only the capabilities actually used (broad filesystem
   access for arbitrary game dirs → `runFullTrust` / `broadFileSystemAccess`, justified in
   the listing).
2. **Build artifact** — `msbuild /t:Build /p:Configuration=Release /p:Platform=x64 /p:GenerateAppxPackageOnBuild=true`
   (or `dotnet build` + the MSIX targets) → an `.msixupload` bundle (x64 + ARM64).
3. **Sign** — the **Store signs it**; for sideload/dev use a self-signed or 626 cert.
4. **Submit** — Partner Center submission (manual first, then `StoreBroker` PowerShell or the
   Store submission API in CI). Listing copy in the 626 voice; screenshots from the themed app.
5. **CI (later)** — a `release-msstore.yml` GitHub Actions workflow, **manual-dispatch only**,
   that builds the MSIX bundle and uploads the artifact. Submission stays human-gated.

## Gates before this runs

- Phase 2 shell complete + visually signed off.
- Code-signing identity decided (roadmap H2).
- Capabilities review (broad filesystem access is the sensitive one — the Store reviews it
  carefully for a mod manager; the "your own files" framing is the answer).
- Privacy: no telemetry without disclosure; the CurseForge key stays server-side (law #2).

## Not in scope yet

The packaging project, `Package.appxmanifest`, signing cert, and the CI workflow are **not
created** — this doc is the plan. Wire it at Phase 3.
