# Backlog

Living list, newest headline on top. Source of truth for "what's next," separate from the
dated design docs under `docs/superpowers/`.

## 1. Uninstall doesn't wipe the Nexus DPAPI token or app-data folders — next Store push

**Added:** 2026-08-03, from the privacy evidence sweep (`626labs-hub`
`.superpowers/sdd/privacy-findings-2026-08-03.md`, §3.5). Confirmed: no Velopack
`OnBeforeUninstallFastCallback` (or equivalent) deletes the DPAPI-encrypted Nexus token or
`%APPDATA%\ModManagerBuilder\` / `%LOCALAPPDATA%\ModManagerBuilder\` on uninstall, on either
distribution channel. `PRIVACY.md` already discloses this honestly ("Uninstalling doesn't
automatically wipe your stored Nexus token or app data"), so nothing is misrepresented today —
but the gap itself should close before the next Store push, same class of issue as Sanduhr's
(lower stakes here: the token only unlocks the user's own Nexus session, never a 626-Labs-side
credential). Options: add an uninstall hook that clears the token + folders, or an in-app
"disconnect + clear local data" action mirroring Sanduhr's sign-out-based fix.

**Secondary, same push:** `docs/store/privacy-statement-store-submission.txt` and
`docs/store/privacy-policy-update-for-nexus.md` predate two now-settled facts — the game-manifest
feed going live at v0.6.0, and Este's Cloudflare-dashboard confirmation that Workers Logs/Logpush
are off for `626-mod-metadata-proxy` (so the flat "we do not log or retain requests" claim, already
used in `PRIVACY.md` and on the site, can replace the store-submission text's more hedged framing).
Refresh both before the next submission goes out; `privacy-policy-update-for-nexus.md`'s "⚠ ONE
SENTENCE NEEDS YOUR CONFIRMATION" question is resolved — mark it so.

On ship: update `PRIVACY.md`, `docs/store/privacy-statement-store-submission.txt`, and
https://626labs.dev/privacy.html#privacy-mod-launcher together so none of the three drift from
each other.
