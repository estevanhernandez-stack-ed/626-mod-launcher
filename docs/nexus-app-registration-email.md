# Nexus Mods — third-party application registration email

> Paste-ready draft per the [API Acceptable Use Policy](https://help.nexusmods.com/article/114-api-acceptable-use-policy) registration flow.
> **To:** support@nexusmods.com · **Attach:** the app logo (`src/ModManager.App/Assets/` icon, or the Store logo art)

---

**Subject:** Third-party application registration — 626 Mod Launcher

Hi Nexus Mods team,

I'd like to register a public-facing application that uses the Nexus Mods API, per your API Acceptable Use Policy.

**What it is**

626 Mod Launcher is a free, native Windows mod manager (.NET / WinUI 3) built and published by 626Labs LLC. It manages mods for the user's own installed games — reversible enable/disable, load order, save snapshots, config editing — and never bundles, hosts, or redistributes any mod content. Users obtain their mods themselves; the app organizes files already on their disk.

- GitHub (source + releases): https://github.com/estevanhernandez-stack-ed/626-mod-launcher
- Current release (testing/production build): https://github.com/estevanhernandez-stack-ed/626-mod-launcher/releases/tag/v0.10.0 (installer + portable zip)
- Microsoft Store listing: https://apps.microsoft.com/detail/9N53V6RRJK95 (note: the Store edition ships **without** the Nexus integration; everything below applies to the GitHub edition only)

**How it uses the Nexus API**

All API access happens client-side, from the user's own machine, using the user's own personal API key — supplied by the user at runtime, stored only in the app's on-machine credential store, sent per-request, and never proxied through or stored on any server of ours. Every request carries `Application-Name` and `Application-Version` headers identifying the app.

Endpoints used, and when:

- `/v1/games/{domain}/mods/md5_search` — identify a mod archive the user just dropped in, or a user-initiated backfill of their existing archives. User-triggered only.
- `/v1/games/{domain}/mods/{id}` — refresh title/author/endorsement/download metadata for the user's installed mods. User-triggered, plus a debounced refresh.
- `/v1/user/endorsements` and endorse/abstain — reflect and set the user's own endorsement state from inside the app (we surface endorsement as a first-class action to send appreciation back to mod authors).
- `/v1/games/{domain}/mods/updated` — a **debounced (24-hour)** background check so the app can flag installed mods with newer versions.
- `/v2/graphql` mods name-search — a user-triggered, review-first flow that proposes matches for mods the user already has installed loose on disk (a handful of queries per run, each with a client-side timeout; nothing runs in a loop).

**What it does not do:** no file downloads through the API (the app never fetches mod files), no scraping, no bulk crawling, no caching or redistribution of Nexus content beyond the metadata shown to the key's owner, and no circumvention of Premium features.

**For the API Access page listing**

- **Name:** 626 Mod Launcher
- **Short description:** A native Windows mod manager for your own installed games — reversible by default, honest about what it does, and built to send appreciation back to mod authors. Nexus integration identifies your installed mods, tracks updates, and makes endorsing one click.
- **Logo:** attached.

**One additional request:** we'd love an application slug for the SSO integration, so users can connect their Nexus account with a one-click authorization instead of manually pasting an API key. Happy to implement to your current SSO spec.

If anything in the app needs adjusting to comply with the policy, tell me and I'll make the change — the integration was built with your acceptable-use rules in mind from the start.

Thanks — and thanks for the platform. Half of what this app does exists to make Nexus mod authors' work easier to respect.

Estevan Hernandez
626Labs LLC
estevan.hernandez@gmail.com
