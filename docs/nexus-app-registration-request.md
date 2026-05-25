# Nexus Mods — SSO application registration request

> **Status:** draft to send. Fill in `[your Nexus handle]`, polish the tone, then **email it** (see *How to register* below). Registering under a **personal account**.
>
> **Why:** the SSO **application slug is staff-issued**. There is **no signup link or form** — per the API Acceptable Use Policy you **email `support@nexusmods.com`** with a *testing build that works with a personal API key*, plus the app's name / short description / logo; they then issue the slug.

---

## The request (paste-ready)

**To:** support@nexusmods.com
**Subject:** SSO application slug request — 626 Mod Launcher (desktop mod manager)

Hi — I'm the developer of **626 Mod Launcher**, a Windows desktop mod manager, and I'd like to register it for API / SSO access. A testing build that authenticates with a personal API key is attached / linked below.

**What it does with the API (v1):**

- **SSO authentication** — each user authorizes via the standard SSO flow (`wss://sso.nexusmods.com`, protocol 2) so they supply their *own* per-user API key. No keys are bundled or shared.
- **Metadata lookup** — mod details by id (`/v1/games/{domain}/mods/{id}.json`) to show real names, authors, and **donation / attribution** links (honoring mod authors is a core principle of the app).
- **md5 file identification** — `md5_search` at mod-install time to identify Nexus-hosted files that other fingerprints miss.
- **No downloads** in v1 — metadata + identification only.

**Compliance:** I've read the API Acceptable Use Policy and will respect the per-key rate limits and attribution requirements.

**Requested:** application approval + an application slug (preferred: `626-mod-launcher`) and connection token for SSO.

Registering under my personal account (**[your Nexus handle]**).

**App details for the listing:**

- **Name:** 626 Mod Launcher
- **Short description:** [one line — e.g. "A Windows mod manager for any moddable game; surfaces mod metadata and identifies installed files by md5."]
- **Logo:** [attach a PNG]

Happy to provide anything else you need. Thanks!

---

## How to register (no signup link — it's email)

Per the [API Acceptable Use Policy](https://help.nexusmods.com/article/114-api-acceptable-use-policy):

1. **Email `support@nexusmods.com`** with a **testing build that works with a personal API key** (so build + verify Track A first — see below).
2. Include the app's **name, short description, and logo**.
3. They review and **issue your slug + connection token**; approved apps then appear on your [API Access page](https://www.nexusmods.com/users/myaccount?tab=api).

> The forums / Discord are fine for *questions*, but the registration itself goes through **support@nexusmods.com**.

---

## Track A — the personal-key testing build (now a prerequisite, not just a shortcut)

Nexus wants a *testing build that works with a personal API key* before they issue the slug — so this is **step 1**, not optional. The `NexusClient` takes an injected `apikey`, so a **personal key** exercises the whole Core today:

1. Nexus → **account settings → API access** (`nexusmods.com/users/myaccount?tab=api`).
2. Generate / copy the **Personal API Key**.
3. `new NexusClient(http, new NexusOptions { ApiKey = "<key>" })` → `GetByMd5Async("windrose", <md5>)` against a real Windrose mod file.

Proves `GetMod` / `GetByMd5` / `Md5Hash` on live data before the SSO wrapper exists.

## Our SSO impl already matches their protocol

When the slug arrives, the deferred D6 SSO slice drops straight in:

- connect `wss://sso.nexusmods.com`
- send `{ "id": "<uuid>", "token": null, "protocol": 2 }`
- open browser `https://www.nexusmods.com/sso?id=<uuid>&application=<slug>`
- key returns as `{ "success": true, "data": { "api_key": "..." } }`

---

**Sources:** [SSO integration demo](https://github.com/Nexus-Mods/sso-integration-demo) · [API Acceptable Use Policy](https://help.nexusmods.com/article/114-api-acceptable-use-policy) · [API access / slug forum thread](https://forums.nexusmods.com/topic/13105527-nexusmods-api-access-application-slug/)
