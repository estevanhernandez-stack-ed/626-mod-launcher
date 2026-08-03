# Privacy policy update for the site — SUPERSEDED for the 0.15.0.0 submission

> **Not needed for the Store submission.** The 0.15.0.0 submission supplies its privacy statement directly
> as text ([`privacy-statement-store-submission.txt`](privacy-statement-store-submission.txt)) and does not
> reference `626labs.dev/privacy.html` at all, so nothing here gates the submission.
>
> **Kept because change 2 is still a genuine accuracy bug on the live site**, unrelated to Nexus: the page
> says 626 Labs "does not receive, proxy, or store" third-party data, while the launcher identifies mod
> files through a 626 Labs-operated CurseForge proxy. Worth fixing on its own schedule. The submitted
> statement already words this correctly.

# (original) Privacy policy update — proposed site changes

**Target file:** `626Labs-LLC.github.io/privacy.html` (the root one, ~26 KB — served as
<https://626labs.dev/privacy.html>; there is also a smaller `legal/privacy.html`, confirm which is live).
**Status:** proposed text, NOT applied. This is a public legal page and should be published deliberately.

The reviewer letter points Microsoft at this URL, so anything inaccurate there is inaccurate *to a reviewer*.
Two changes. **The second is a pre-existing inaccuracy that Nexus did not introduce.**

---

## Change 1 — disclose the optional Nexus account

**Where:** in the existing **"Third-party integrations you configure"** section, as a new paragraph after
the current one. That section already describes this exact pattern (opt-in, your own credentials, data
flowing straight to the third party), so this is an addition, not a rewrite.

**Paste-ready:**

```html
<p>
  <strong>626 Mod Launcher and Nexus Mods.</strong> 626 Mod Launcher can optionally connect to your own
  Nexus Mods account so it can show you a game's mods inside the app, indicate which ones you already have
  or have endorsed, and let you endorse or track a mod. Signing in uses Nexus's standard OAuth
  authorization flow in your browser &mdash; we never see or store your Nexus password. The resulting
  access token is encrypted and stored on your own machine; it is never sent to 626 Labs. Signing in is
  optional, and the app's mod-management features work fully without it. The app never downloads a mod for
  you: choosing a mod opens its page on Nexus in your browser, and anything you download there is governed
  by Nexus Mods' own privacy policy and terms.
</p>
```

Every claim above is verifiable in the source: the token is DPAPI-encrypted on-machine
(`NexusService`), the flow is loopback PKCE in the system browser (`NexusOAuthService`), and the only
"get" action is a `Process.Start` on the mod's Nexus URL.

---

## Change 2 — correct the blanket "does not proxy" sentence

**The problem.** That section currently says of third-party integrations:

> *"626 Labs does not receive, proxy, or store the credentials or the data exchanged."*

True for Nexus. **Not** true for the app overall: 626 Mod Launcher identifies a mod file already on your
disk by calling a **626 Labs-operated proxy** (`626-mod-metadata-proxy.626labs.workers.dev`) that forwards
the lookup to CurseForge. The proxy exists so the CurseForge API key stays server-side instead of being
embedded in the app — good design, but it does mean a request passes through our infrastructure, and the
sentence as written contradicts it.

**⚠ ONE SENTENCE NEEDS YOUR CONFIRMATION.** I could not verify the Worker's logging behavior — its source
is not in this repo. **Do not publish a retention claim that hasn't been checked.** Pick a variant:

**Variant A — use ONLY if you confirm the Worker keeps no request logs:**

```html
<p>
  With one exception, 626 Labs does not receive, proxy, or store the credentials or the data exchanged.
  The exception is a metadata lookup: 626 Mod Launcher identifies a mod file you already have on disk by
  asking a 626 Labs-operated proxy, which forwards the request to CurseForge. The proxy exists so that a
  third-party API key stays on our server instead of being embedded in the app. It is a read-only lookup of
  public mod information (name, author, version), it carries no account details, and we do not retain logs
  of these requests.
</p>
```

**Variant B — the safe default, accurate without any retention claim (recommended if unsure):**

```html
<p>
  With one exception, 626 Labs does not receive, proxy, or store the credentials or the data exchanged.
  The exception is a metadata lookup: 626 Mod Launcher identifies a mod file you already have on disk by
  asking a 626 Labs-operated proxy, which forwards the request to CurseForge. The proxy exists so that a
  third-party API key stays on our server instead of being embedded in the app. It is a read-only lookup of
  public mod information (name, author, version) and carries no account details. Like any web request, it
  reaches our hosting provider with the usual connection information; we do not use it to build a profile
  of you or link it to an identity.
</p>
```

Variant B is honest without requiring you to audit the Worker first. If you later confirm no logs, swap in A.

---

## Change 3 — name the app (recommended)

The policy is written around *plugins* and does not mention 626 Mod Launcher anywhere. Add it to whatever
named-products line the page carries so the app is explicitly covered rather than covered by implication.
The 0.8.1 store listing flagged this as optional; with an account integration in play, it is worth doing.

---

## Checklist

- [ ] Confirm which file is actually served at `626labs.dev/privacy.html` (root vs `legal/`)
- [ ] Change 1 added (Nexus account disclosure)
- [ ] Change 2 applied — **choose Variant A only after checking the Worker; otherwise use B**
- [ ] Change 3: 626 Mod Launcher named on the products line
- [ ] "Last updated" date bumped
- [ ] Published and live **before** the submission is sent — the reviewer letter links to it
