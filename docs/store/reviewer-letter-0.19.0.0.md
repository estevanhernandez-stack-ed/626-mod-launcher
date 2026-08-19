# Notes for certification — reviewer letter — v0.19.0.0 (one place for what's true)

> **Draft — finalise against the real package.** Identity, version, capability and seal must be read
> out of the built bundle before this is pasted, the way 0.18.1's letter was. The claims below are
> true of master `c644025`; confirm they are true of the thing you upload.
>
> **The honest headline:** the app looks different and behaves the same toward a user's files. One
> genuine safety fix (a control that moved files while looking like a view filter), one destructive
> action given a confirmation it never had, and a lot of rearranging.
>
> **The screenshot claim was corrected on 2026-08-19.** This letter first said the listing shots did
> not match the build — written without opening them. The `screenshots-0.18` set matches everywhere
> except the updates view (being retaken) and some renamed labels in two shots. The opening paragraph
> now says only what is true.

---

```text
Hello reviewer,

Thank you for your time on version 0.19.0.0.

WHAT A REVIEWER WILL SEE FIRST

The main toolbar and the Settings page are laid out differently from the
last approved version. Several section headings were renamed and a set of
scattered warnings was consolidated into a single row above the mod list.
That is what this release mostly is: a rearrangement of what the app
already did. Nothing about what the app does to a user's files changed
with the layout.

WHAT CHANGED SINCE THE LAST APPROVED VERSION

1. Warnings were consolidated. Anti-cheat risk, missing prerequisites,
   setup problems, game-update notices and third-party-tool conflicts used
   to appear in two different places in two different visual styles. They
   are now a single row of labelled chips above the mod list, ordered by
   how much the situation costs the user. The anti-cheat warning is first
   and cannot be dismissed.

2. A control that looked like a view filter was one. Three buttons marked
   All / MP / SP appeared to change what was listed, and instead turned
   mods on and off in bulk. They now do what their shape says: change what
   is shown, and touch no files. Turning a set of mods on or off is a
   separate, explicitly named action that states what it will change before
   it does it.

3. Bulk enable/disable now saves the user's current setup first, under a
   generated name, and tells them where it went.

4. Removing an installed prerequisite ("framework") now asks for
   confirmation and states exactly what it will remove and from where. It
   previously did not ask.

5. Settings was reorganised into Appearance, Accounts, Restore points and
   Reset, with About moved to a footer. The reset action remains last.

6. Keyboard shortcuts were added: Ctrl+comma (settings), Ctrl+O (add
   mods), Ctrl+P (profiles), Ctrl+1/2/3 (the show filter).

WHAT DID NOT CHANGE

- Capabilities. runFullTrust remains the only one declared.
- Network endpoints. No new services are contacted. The Nexus Mods
  integration is unchanged and is still compiled into this package rather
  than downloaded — no code is fetched or loaded at runtime.
- Data collection. None. The app has no telemetry and no account of its
  own. Everything it writes stays on the user's machine.
- File behaviour. Disabling a mod still moves files to a holding folder
  rather than deleting them; replacing still snapshots first; removing a
  prerequisite restores whatever it replaced when it was installed.

WHY runFullTrust IS STILL REQUIRED

Unchanged from previous submissions. The app manages mod files inside game
installation folders chosen by the user, which are outside any sandboxed
location, and it launches games through Steam.

VERIFICATION

The submitted package was checked with a build-time seal script that reads
the compiled binaries and asserts two things: that no runtime code-loading
mechanism is present, and that the Nexus integration is compiled in rather
than downloaded. Both are verified for this build.

Thank you again.
```

---

## Why this letter leads with the visual change

0.18.1's letter had to explain a colour difference and did so plainly. This one has a bigger version
of the same problem: the layout differs from the live listing screenshots, and a reviewer who spots an
unexplained difference reasonably wonders what else was not mentioned. Naming it in the first
paragraph costs nothing and removes that question.

## What is deliberately not in the letter

- Internal wave numbering and PR references. A reviewer does not need our sequencing.
- The framework-install-over-existing gap
  (`docs/2026-08-19-framework-install-over-existing.md`). It is pre-existing, unrelated to anything
  this submission changes, and affects only a user who already has a prerequisite installed in a
  different layout. It belongs in our backlog, not in certification notes — but if a reviewer asks
  about prerequisite installation, answer it honestly.
