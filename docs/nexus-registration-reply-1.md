# Nexus registration — reply 1 (to their Jul 9 response)

> Paste-ready reply to the Nexus Mods Support thread.

---

Hi, thanks for getting back to me.

**AUP: confirmed.** I've read the API Acceptable Use Policy and the app complies: every request carries `Application-Name` and `Application-Version` headers, the user's personal API key stays on their machine (supplied per-request from a local credential store — never proxied or stored server-side), background polling is debounced to once per 24 hours, and the app never downloads, caches, or redistributes any mod files or content through the API.

**On GraphQL:** we already use it — the v2 GraphQL endpoint handles our name-search (unauthenticated), and it's great for that. But the heart of our integration is **user-scoped**, which is why GraphQL alone doesn't cover us:

- **Endorsements** — reading the user's endorsement state and endorse/abstain from inside the app. This is the feature we care most about: we surface endorsing as a one-click action so users send appreciation back to mod authors.
- **MD5 identify** (`md5_search`) — matching the user's *own* downloaded archives to their Nexus mod pages, to attach title/author/source metadata to installed mods.
- **Update checks** (`updated.json` + mod-by-id) — flagging installed mods with newer versions, debounced.

Today those run on the v1 API with the user's personal key, per the AUP.

**OAuth2 is exactly what we'd like.** Our one UX pain point is asking users to manually paste an API key — a proper OAuth flow (I'd expect a desktop **public client with PKCE** and a loopback redirect, but happy to implement to your spec) would replace that with a one-click "Connect Nexus account." We'd keep personal-key entry as a fallback for users who prefer it.

**What would you need from us to register the app for OAuth?** Happy to provide whatever's required. For reference:

- **Name:** 626 Mod Launcher (publisher: 626Labs LLC)
- **Type:** free, native Windows desktop app; the Nexus integration ships only in the GitHub edition (the Microsoft Store edition has no Nexus surface)
- **GitHub:** https://github.com/estevanhernandez-stack-ed/626-mod-launcher
- **Current release:** https://github.com/estevanhernandez-stack-ed/626-mod-launcher/releases/latest
- **Store listing (for identity):** https://apps.microsoft.com/detail/9N53V6RRJK95

And whenever you're ready, the name/short description/logo for the API Access page listing from my first email still stand.

Thanks again,

Estevan Hernandez
626Labs LLC
