# 626 Mod Launcher — Privacy Statement

_Last updated: 3 August 2026_

626 Mod Launcher collects nothing: no analytics, no telemetry, no advertising, no usage tracking, and no 626 Labs account — we cannot see what games you own, what mods you use, or how you use the app. Everything it stores (settings, your added games, mod metadata, profiles, and any backups it makes) stays on your own computer and never leaves your machine unless you move it yourself. The app is fully usable offline. When connected, it can talk to three places: our CurseForge lookup proxy, a read-only mod-metadata lookup that keeps our API key off your machine and that we log and store nothing from; GitHub, for a digitally signed game-definitions feed and app updates; and Nexus Mods, only if you choose to sign in. Nexus sign-in uses Nexus's own OAuth flow in your browser — we never see or store your Nexus password, and the resulting access token is encrypted on your device with Windows data protection and never sent to 626 Labs; you can disconnect at any time in Settings. The app doesn't download mods itself — choosing a mod opens its page on the Nexus Mods website in your browser, and anything from there on is between you and Nexus. Mods flagged as adult or mature are excluded before they ever reach the app. Uninstalling doesn't automatically wipe your stored Nexus token or app data; delete `%APPDATA%\ModManagerBuilder\` and `%LOCALAPPDATA%\ModManagerBuilder\` to remove everything.

The canonical, most current version of this policy is published at **[626labs.dev/privacy.html#privacy-mod-launcher](https://626labs.dev/privacy.html#privacy-mod-launcher)**. If this file and the site version ever disagree, the site version governs.

## Contact

- Privacy questions: **estevan.hernandez@gmail.com**
- Source code: <https://github.com/estevanhernandez-stack-ed/626-mod-launcher>
