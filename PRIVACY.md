# 626 Mod Launcher — Privacy Statement

_Last updated: 3 August 2026_

626 Mod Launcher is a mod manager for PC games you already own and have installed on your own computer. It
organizes and toggles mod files in your game folders. It is designed to work locally, and it collects no
personal information about you.

## What we collect

**Nothing.** 626 Labs does not collect, receive, or store personal information from this app. There is no
analytics, no telemetry, no advertising, no usage tracking, and no 626 Labs account. We cannot see what
games you own, what mods you use, or how you use the app. Nothing is sold or shared, because nothing is
gathered.

## What stays on your machine

Your settings, your list of added games, your mod metadata, your profiles, and any backups or snapshots the
app makes are written to your own computer — in your user application data folder and in the game folders
you point the app at. They never leave your machine unless you deliberately move them.

## Network connections the app makes

The app is usable with no network connection at all. When connected, it may contact:

1. **Mod information lookup.** To identify a mod file already on your disk (its name, author, and version),
   the app asks a 626 Labs-operated proxy, which forwards the request to CurseForge. The proxy exists so
   that a third-party API key stays on our server instead of being embedded in the app. It is a read-only
   lookup of public mod information, it carries no account details, and it does not forward your request
   headers upstream. We do not log these requests and we store nothing from them. As with any web request,
   it reaches our hosting provider in order to be served; we do not use it to build a profile of you or
   link it to an identity.

2. **Game definitions.** The app periodically downloads a digitally signed list of supported games from our
   public repository so it can recognize newly supported titles without an app update. This is a plain file
   download and sends no information about you.

3. **Nexus Mods** — only if you choose to sign in. See below.

## Optional Nexus Mods account

The app can optionally connect to your own Nexus Mods account. This is entirely optional; every
mod-management feature works without it.

If you choose to sign in:

- Sign-in happens in your web browser using Nexus's standard OAuth authorization flow. **We never see,
  receive, or store your Nexus password.**
- The resulting access token is encrypted and stored on your own computer using Windows data protection.
  **It is never sent to 626 Labs.**
- While signed in, the app can show you a game's mods, indicate which ones you have already downloaded or
  endorsed, and let you endorse or track a mod. Endorsing and tracking happen only when you click them, and
  are applied to your own Nexus account.
- Your activity on Nexus Mods is governed by [Nexus Mods' own privacy policy](https://help.nexusmods.com/)
  and terms.
- You can disconnect at any time in Settings, which removes the stored token from your computer.

## Downloading mods

The app does not download mods for you. When you choose a mod, it opens that mod's page on the Nexus Mods
website in your default browser, and you download it there. Anything that happens on that website is
between you and Nexus Mods.

## Adult content

Mods flagged as adult or mature are excluded before they ever reach the app: every request the app makes
for mod listings asks the service to omit adult content. The app therefore shows no adult listings and
contains no age gate.

## Children

This app is not directed at children, and we do not knowingly collect information from anyone, of any age.

## Changes

If this statement changes, the updated version will be published at this address and accompany the next
release of the app.

## Contact

- Privacy questions: **estevan.hernandez@gmail.com**
- Source code: <https://github.com/estevanhernandez-stack-ed/626-mod-launcher>
