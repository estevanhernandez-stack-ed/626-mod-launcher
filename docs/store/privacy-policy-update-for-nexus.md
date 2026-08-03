# Privacy policy update — required before the Nexus Store submission

**Target:** `626Labs-LLC.github.io/privacy.html` (served as <https://626labs.dev/privacy.html>) — a different repo.
**Status:** proposed, NOT applied. This is a public legal page; it should be reviewed and published deliberately.

The reviewer letter points Microsoft at this URL, so anything inaccurate there is inaccurate *to a reviewer*.
Two changes are needed. **The second one is a pre-existing inaccuracy, not something Nexus introduced.**

---

## 1. Disclose the optional Nexus account (new)

The current policy is written around *plugins*. 626 Mod Launcher is an app, and it is **not named anywhere**
in the policy today. Its "Third-party integrations you configure" section already describes the exact
pattern — opt-in, your own credentials, data flowing directly to the third party — so this is an addition,
not a rewrite.

Insert as a new paragraph inside **Third-party integrations you configure**, after the existing paragraph:

```html
<p>
  <strong>626 Mod Launcher and Nexus Mods.</strong> 626 Mod Launcher can optionally connect to your own
  Nexus Mods account so it can show you a game's mods in the app, tell you which ones you already have or
  have endorsed, and let you endorse or track a mod. Signing in uses Nexus's standard OAuth authorization
  flow in your browser &mdash; we never see or store your Nexus password. The resulting access token is
  encrypted and kept on your own machine; it is never sent to 626 Labs. Signing in is entirely optional and
  the app's mod-management features work fully without it. Browsing and downloading on Nexus is governed by
  Nexus Mods' own privacy policy and terms. The app never downloads a mod for you &mdash; it opens the mod's
  page in your browser, and you download there.
</p>
```

## 2. Correct the "does not proxy" claim (pre-existing inaccuracy)

The section currently states, of third-party integrations:

> *"626 Labs does not receive, proxy, or store the credentials or the data exchanged."*

That is true of the Nexus integration, but **not** true of the launcher as a whole: 626 Mod Launcher
identifies a mod file already on your disk by calling a **626 Labs-operated CurseForge metadata proxy**
(`626-mod-metadata-proxy.626labs.workers.dev`). The proxy exists so the CurseForge API key stays
server-side instead of being embedded in the app. That is a good design, but it does mean a request passes
through our infrastructure, and the blanket "does not proxy" sentence contradicts it.

This predates the Nexus work and would be worth fixing regardless — but it matters more now, because the
reviewer letter cites this page. Suggested replacement for that sentence:

```html
<p>
  With one exception, 626 Labs does not receive, proxy, or store the credentials or the data exchanged.
  The exception is a metadata lookup: 626 Mod Launcher identifies a mod file you already have on disk by
  asking a 626 Labs-operated proxy, which forwards the lookup to CurseForge. The proxy exists so that a
  third-party API key stays on our server rather than being embedded in the app. It is a read-only lookup of
  public mod metadata (name, author, version). It carries no account information, and we do not log it or
  associate it with you.
</p>
```

**Verify before publishing** that the last sentence is true of the deployed Worker — if it logs requests,
say so instead. Do not publish a claim about our own infrastructure that has not been checked.

## 3. Optional but recommended

Add "626 Mod Launcher" to whatever named-products line the page carries, so the app is explicitly covered
rather than covered by implication. The 0.8.1 store listing already noted this as an optional
belt-and-suspenders item; with an account integration in play it is worth doing.

---

## Checklist

- [ ] Paragraph 1 added (Nexus account disclosure)
- [ ] Sentence 2 corrected (CurseForge metadata proxy) — **after** confirming the Worker's logging behavior
- [ ] 626 Mod Launcher named on the products line
- [ ] Page's "last updated" date bumped
- [ ] Published and live at <https://626labs.dev/privacy.html> **before** the submission is sent, since the
      reviewer letter links to it
