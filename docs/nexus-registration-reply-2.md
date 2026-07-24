# Nexus registration — reply 2 (providing the OAuth details + review build)

> Paste-ready reply to the Nexus Mods Support thread, in answer to their "App name / Callback URL / scopes + a build with OAuth ready and API keys removed" request.
> **Send after** launcher v0.11.0 is cut (so the review build link resolves), OR point them at the PR to review source. **Confirm before sending:** the exact OAuth authorize/token endpoint URLs + the identity endpoint against your OAuth guide (our code reads them from config, marked `// CONFIRM`).

---

Hi, thanks for the detailed direction — this was exactly what we needed.

We've done the work you asked for. The API key path is **removed completely**, OAuth is the only auth, and — importantly — we fixed the security issue you flagged: the plugin can no longer reach the credential by any path. The host now owns the token end to end and makes authorized requests on the plugin's behalf; the plugin builds an unauthenticated request and never sees a token. (That was the right call — thank you for pointing it out.)

**The three things you need:**

- **App name:** 626 Mod Launcher (publisher: 626Labs LLC)
- **Callback URL:** `http://127.0.0.1:41999/callback` — a loopback redirect for a desktop public client (PKCE, RFC 8252). One question: do you allow the loopback-any-port form (RFC 8252 §7.3), i.e. `http://127.0.0.1:{any}/callback`? We prefer the fixed port above and fall back to an ephemeral loopback port only if 41999 is busy — happy to register whichever shape you require.
- **Scopes (as capabilities — please map to your scope names):**
  - *identity* — read the signed-in user (currently `GET /v1/users/validate.json`)
  - *endorsements: read* — `GET /v1/user/endorsements.json`
  - *endorsements: write* — `POST /v1/games/{domain}/mods/{id}/{endorse|abstain}.json`
  - *mod metadata: read* — md5 identify, mod-by-id, and updated lists (`/v1/games/{domain}/mods/…`)
  - (mod name-search runs on the v2 GraphQL endpoint unauthenticated — no scope needed)

**The build:** the OAuth framework is in and the keys are out as of v0.11.0 — the current GitHub release is the build to evaluate against: <RELEASE URL — fill in after cutting v0.11.0, e.g. https://github.com/estevanhernandez-stack-ed/626-mod-launcher/releases/tag/v0.11.0>. Source is public if you'd rather read it: the credential-handling is in `src/ModManager.App/Services/PluginHost.cs` (`SendAuthorizedAsync`), `NexusService.cs` (the DPAPI token store), and `NexusOAuthService.cs` (the loopback PKCE flow); the host clones the plugin's request before attaching the bearer, so a plugin can't read the token off its own request. The client_id is config-driven and empty until you register us — once you issue it, we deliver it to installs through our signed feed, so sign-in lights up without another app release.

**Headers on every request** (unchanged, per your ToS): `Application-Name: 626-mod-launcher` + `Application-Version`.

If anything else needs adjusting to register the app, tell me and I'll turn it around quickly. Thanks again for the guidance — the integration is better for it.

Estevan Hernandez
626Labs LLC
