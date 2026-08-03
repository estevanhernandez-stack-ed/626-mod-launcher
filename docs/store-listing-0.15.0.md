# Microsoft Store submission — 0.15.0.0 (the Nexus build)

> **This is the submission where the Store SKU stops being the sealed core.** It carries the Nexus Mods
> integration, compiled into the package. The listing has to say so — the current live description tells
> users you bring your own mods and never mentions browsing, and a reviewer comparing the listing to the
> app would find an undisclosed feature. That is a worse problem than the feature itself.
>
> **Package to upload:** `src/ModManager.App/AppPackages/ModManager.App_0.15.0.0_Store_Test/ModManager.App_0.15.0.0_x64_Store.msixbundle`
> (unsigned — the Store re-signs). Claims verified inside the bundle; see
> [`store/reviewer-letter-0.15.0.0-nexus.md`](store/reviewer-letter-0.15.0.0-nexus.md).
>
> Category (**Utilities & tools**) and screenshots are unchanged from prior submissions.
>
> **Privacy:** supplied directly with this submission as text
> ([`store/privacy-statement-store-submission.txt`](store/privacy-statement-store-submission.txt)), not as a
> link to the 626labs.dev page. Nothing in this submission should reference that URL.

---

## What's new in this version  *(max 1500 chars)*

You can now find mods without leaving the launcher. Open a game, hit Browse Nexus, and search that game's
mods from Nexus Mods right in the app — sorted by endorsements, filtered by category, with the art and
details there to read.

Because you are signed in to your own Nexus account, the launcher can tell you things a website tab cannot:
which mods you already have, which you have endorsed, and which of your installed mods have an update
waiting. You can endorse or track a mod from the app, so the authors get their credit without a detour.

Downloading still happens on Nexus, in your browser, on the author's page. The launcher does not fetch mods
for you — you download there and drop the file in, same as always.

Also new: a per-game update badge on your library and one Updates view listing every mod with a newer
version across all your games, so you stop hunting for what needs attention.

Signing in is optional. Every file-management feature works exactly as before without it.

---

## Description — changed sections only

The description's opening ("your files are yours"), **Who it's for**, and the reversibility bullets are
unchanged. Two sections change.

### Add to **What it does** (after the "Reads your installed games" bullet)

```text
- Browses Nexus Mods for the game you are managing, right in the app — search, sort by endorsements,
  filter by category, and read a mod's details and requirements before you decide.
- Tells you what you already have: with your own Nexus account connected, mods show as installed or
  endorsed, and mods with a newer version are flagged — per game, and in one cross-game Updates view.
- Lets you endorse or track a mod from the app, so authors get credit without a trip to the website.
```

### Replace **What it is not** entirely

The current wording ("You bring the mods you already have") is no longer the whole truth. Replacement:

```text
**What it is not**

It is not a store, and it does not download or distribute mods. You can search Nexus Mods from inside the
launcher, but getting a mod opens that mod's page on the Nexus website in your browser — the download
happens there, on the author's page, under Nexus's terms. Nothing is sold here, and no third-party mod
files or game content ship with this app. Mods flagged as adult are excluded from what the launcher shows.

You still bring the file. The launcher manages it on your machine.
```

**Why worded that way:** "It is not a store, and it does not download or distribute mods" is the honest
claim and also the exact policy defense in the reviewer letter — the public listing and the certification
note should say the same thing, because a reviewer will read both.

---

## Notes for certification

Use the paste-ready block in **[`store/reviewer-letter-0.15.0.0-nexus.md`](store/reviewer-letter-0.15.0.0-nexus.md)**.
It leads with the change from the approved 0.11.2.0 (that submission stated there was no in-app mod
browsing; this one reverses that) rather than letting a reviewer's diff discover it.

## Age rating

**Re-run, not carried over.** Online Content = Yes (the app displays third-party mod listings fetched at
runtime). Violence = Yes (mod screenshots for M-rated titles can depict combat and blood; excluding
adult-flagged content does not make the remainder violence-free). Expect a higher rating than the previous
submission — that is the correct outcome, not a failure.

## Before you upload

- [x] Privacy statement supplied with the submission (no 626labs.dev reference anywhere in the submission)
- [ ] Description updated with the two sections above
- [ ] "What's new" pasted
- [ ] Reviewer letter block pasted into Notes for certification
- [ ] Age rating questionnaire completed with the new answers
- [ ] Sign-in re-confirmed on this exact bundle
