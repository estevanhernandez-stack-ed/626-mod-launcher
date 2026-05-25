# Nexus integration — design

- **Date:** 2026-05-25
- **Status:** Approved (shape confirmed with Este)
- **Roadmap:** Phase D6 in [docs/2026-05-25-backlog-roadmap.md](2026-05-25-backlog-roadmap.md).
- **Why:** The project's differentiator. Many games — Windrose included — host mods on **both
  CurseForge and Nexus**, and plenty (repacked, renamed, save/world mods, niche ones) are
  **Nexus-only**. The CurseForge MurmurHash fingerprint can't see those. Nexus as a 2nd metadata +
  file-ID source is how the launcher out-classes a CF-only catalog — the origin thesis (Vortex, the
  bad Nexus installer, is what this project set out to beat).

## Decisions (locked with Este)

| Question | Decision |
|---|---|
| Auth | **Nexus SSO flow** — browser authorize → key over the SSO websocket → stored per-user, never baked. |
| v1 scope | **Metadata + md5-at-intake.** Search/details + author/endorsements/donation; md5 exact-ID at drop. **No downloads.** |
| Key handling | The key is the **user's own**, obtained via SSO, stored in per-user local config (law #2). The app's SSO **slug is public** (not a secret). |
| Game keying | Nexus keys games by **domain name** (`windrose`), not a numeric id → new `nexusGameDomain`. |

## Prerequisite (external, like Partner Center)

Register the 626 app with Nexus Mods to obtain an **application slug** for SSO. The slug ships in the
client (public identifier); it is not a secret. Until registered, the SSO flow can't run — the rest
of the build can proceed and be tested against a fake handler.

## Architecture (pure-core / thin-shell)

### Auth — `NexusAuthService` (App)

The SSO handshake (network + browser → the user's key):
1. Generate a UUID; open a `System.Net.WebSockets.ClientWebSocket` to `wss://sso.nexusmods.com`
   (built-in, **no new dependency**).
2. Send the SSO request (`{ id: <uuid>, token: <prev or null>, protocol: 2 }`); launch the browser
   to `https://www.nexusmods.com/sso?id=<uuid>&application=<slug>`.
3. The user authorizes in the browser; the websocket delivers the **API key**.
4. Store it in **per-user local config** (the app's settings store — never the binary, never the
   registry games.json). Expose a "connected as <user>" status (via `/v1/users/validate.json`).

SSO is App-layer (network + browser + UI). Pure message shapes (the request/response JSON) can live
in Core if it keeps `NexusAuthService` thin, but the orchestration is App, build-verified.

### Client — `NexusClient` + `NexusRequests` (Core, pure, unit-tested)

Mirrors `CurseForgeClient` / `CurseForgeRequests`: an injected `HttpClient`, the `apikey` header
carrying the user's key, base `https://api.nexusmods.com`.

- `NexusRequests` — pure request shape (URL + headers) for: search-by-name, `GetMod(domain, modId)`
  (`/v1/games/<domain>/mods/<id>.json`), `GetFilesByMd5(domain, md5)`
  (`/v1/games/<domain>/mods/md5_search/<md5>.json`). Unit-tested (URL/header shape).
- `NexusClient` — calls + response parsing into the app's metadata shape; tested against a **fake
  `HttpMessageHandler`** (no live network), exactly as the CF client is.
- **md5 helper** (`Md5Hash` in Core) — `System.Security.Cryptography.MD5` over file bytes; golden-tested.

### Game keying — `nexusGameDomain`

Add `nexusGameDomain` (string) to `GameEntry`, `GameInput` + `BuildGameEntry`, the agentic-profile
contract (`GameProfileDraft` + `GameProfilePrompt`), and the Add Game wizard — so a game carries its
Nexus domain alongside `curseforgeGameId`.

### Identification at intake — md5-at-intake

In the intake metadata pass (today `Scanner.FingerprintIdentifyAsync` runs the CF MurmurHash
fingerprint): after the CF pass, for any still-unidentified added file, compute its md5 and call
`GetFilesByMd5(nexusGameDomain, md5)`. **Exact match (CF fingerprint OR Nexus md5) wins over
name-search.** Best-effort — a Nexus miss or no-connection falls through to name-search / CF; never
fails intake.

### Metadata merge

Nexus joins the existing curated-wins merge as a source. Its **author, endorsements, donation link**
populate the honor-the-builders display fields the mod row already shows for CF. Precedence: a
user-curated value wins; then an exact-match source (fingerprint/md5); then name-search; between CF
and Nexus, CF (established) is the default tiebreak — but whichever **identified** the mod supplies
its metadata.

### UI (App)

- **Settings:** a "Connect Nexus" button (runs SSO) + connection status. A "disconnect" clears the
  stored key.
- Nexus-sourced metadata renders in the mod row exactly like CF's — `TextBlock.Text` only
  (mod-supplied strings are attacker-controlled), donation/source links via the `SafeUrl` guard.

## Security (law #2)

- The API key is the **user's own**, obtained via SSO at runtime, stored in per-user local config —
  **never embedded** in the distributed exe. The SSO **slug is public**, fine to ship.
- All Nexus-supplied strings are rendered as text (no markup), links gated by `SafeUrl.IsHttpUrl`.
- Add a note to the threat model: a 2nd remote metadata source + a stored per-user credential.

## Error handling

- **Not connected** → Nexus features inert; CurseForge + the rest unaffected (graceful degrade).
- **SSO timeout/cancel/closed socket** → surfaced as a status message; no partial/stored key.
- **429 / rate-limit** (Nexus enforces per-key limits, returns headers) → back off + surface; don't
  hammer. Respect the daily/hourly limit headers.
- **md5 miss / 404** → fall through to name-search / CF; intake still succeeds.
- **Key rejected (401)** → prompt to reconnect; clear the stale key.

## Testing

**Core (test-first, no network):**
1. `NexusRequests` builds the right URL + `apikey` header for search, `GetMod`, `GetFilesByMd5`.
2. `NexusClient` parses a stubbed mod-details / md5-search response into the metadata shape
   (fake `HttpMessageHandler`).
3. `Md5Hash` golden — a known input → known md5.
4. `BuildGameEntry` carries `nexusGameDomain`; `GameProfileImport` accepts/validates it.

**App (build-verify + live smoke):** SSO connect (needs the registered slug + a real Nexus account),
md5-at-intake identifies a Nexus-only mod, donation/author renders + links gated.

## Scope / limits (v1)

- **No downloads/installing from Nexus** — that's a separate large feature (download API + premium
  gating + install flow).
- SSO needs the **registered app slug** (external prereq).
- Respect Nexus **rate limits** (per-key).
- Metadata + md5 exact-ID + honor-the-builders are the v1 payoff.

## File structure

- Create: `src/ModManager.Core/NexusRequests.cs`, `src/ModManager.Core/NexusClient.cs`, `src/ModManager.Core/Md5Hash.cs`.
- Modify: `src/ModManager.Core/GameEntry.cs` (`nexusGameDomain` on `GameEntry` + `GameInput`), `EnginePresets.cs` (`BuildGameEntry` maps it), `GameProfileImport.cs` + `GameProfilePrompt.cs` (profile carries it), `Scanner.cs` (md5-at-intake after the CF pass), `Metadata.cs` (Nexus as a merge source).
- Create: `src/ModManager.App/Services/NexusAuthService.cs` (SSO), `src/ModManager.App/Services/NexusService.cs` (client + stored key + domain).
- Modify: `src/ModManager.App` settings UI (Connect Nexus + status); `AddGameDialog` (`nexusGameDomain` field); DI host registration.
- Tests: `tests/ModManager.Tests/NexusClientTests.cs`, `NexusRequestsTests.cs`, `Md5HashTests.cs`.
