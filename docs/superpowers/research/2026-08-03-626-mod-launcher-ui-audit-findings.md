# 626 Mod Launcher — UI audit findings register (vibe-glow stage 1)

> Audit run 2026-08-03 against the design language at
> `docs/superpowers/specs/2026-08-03-626-mod-launcher-design-language.md`
> and the 33-capture baseline (`docs/ui-evidence/`, themes default /
> obsidian / matrix). Five Opus lenses produced 69 raw findings; deduped to
> 52; every finding faced a fresh Opus skeptic (9 batched refuters — batching
> chosen to stay inside the approved cost gate; each skeptic had zero
> authorship of the findings it judged and default-to-refuted instructions).
> 46 survived, 6 refuted (recorded at the bottom). Ranked by severity ×
> visibility. `status` starts `open`; `:wave` moves rows to `shipped`,
> re-review moves them to `clean` or back to `open`.

## Survivors

| id | surface | lens | sev | vis | evidence | verdict | fix direction | status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F-001 | global (every view + dialog) | conformance+consistency | 5 | 5 | 47/50 CornerRadius non-zero (20×3, 10×6, 10×4, 6×8, 1×10) + WinUI defaults on undeclared controls; no radius resource exists. LibraryView.xaml:100, UpdatesView.xaml:86, MainWindow.xaml:401, MainWindow.xaml.cs:718,1089,1301,1386,1502; PNGs 01, 13, 19 | CONFIRMED — counts exact; the 3 zero-radius sites are nested in a r3 border, not a deliberate stance. WAVE-2: all declared radii swept to 0 + ControlCornerRadius/OverlayCornerRadius zeroed (dialog shells square, per-pixel). Residue: ToggleSwitch pill + CheckBox glyph radius are template-level — ControlCornerRadius doesn't reach them; needs explicit template overrides | Template overrides for ToggleSwitch/CheckBox (the last two rounded controls) | clean (wave 7 — stock template with 4 radii zeroed, pixel-verified; CheckBox needed no template, popup radius scope via DialogTheming) |
| F-002 | global (glow rule) | conformance | 5 | 5 | accent_bloom parsed/normalized/serialized (Themes.cs:6,48,129,164; SettingsDialog.xaml.cs:572-575) but zero Shadow/DropShadow/ThemeShadow consumers in src XAML; only consumer is the dead Electron-era ThemeToCssVars. PNGs 02 vs 30 (matrix bloom 0.60 renders flat) | CONFIRMED — token round-trips through settings and lands nowhere | AccentBloomShadow resource driven by live AccentBloom; attach to the four sanctioned surfaces only | clean (wave 8 — Bloom composition service consumes accent_bloom live on Play modded + ban-risk banner; toggle/active-nav bloom slices recorded as proposed addition) |
| F-003 | mod rows, variant chips, checkboxes, text-field focus | conformance+consistency+a11y | 4 | 5 | No ToggleSwitch*/ToggleButton*Checked/CheckBox*Checked/TextControl*Focused brush keys in App.xaml:13-71 or ThemeService.cs:58-114. Per-pixel: 539 px #0000B2 track + 88 px #000000 knob identical across 02/22/30; knob-on-track 1.64:1, track-vs-bg 1.38:1 (WCAG 1.4.11 needs 3:1) | CONFIRMED — pixel-identical across themes; fix maps existing tokens, invariant-clean | Add the stateful-control brush keys to App.xaml and mutate them in ThemeService.Apply | clean (wave 1) |
| F-004 | global chrome type | conformance | 4 | 5 | Zero Bahnschrift anywhere in src; only explicit face is Consolas (19 XAML + 8 code sites, no shared resource). Chrome labels (LibraryView.xaml:27, MainWindow.xaml:143,191,216) are default Segoe. PNG 01 | CONFIRMED — corrected: mono face EXISTS (Consolas); the stencil chrome face is 100% absent; Cascadia→Consolas swap is a low-sev token change | StencilFontFamily + MonoFontFamily app resources; route chrome + chips through them | clean (wave 2 — Bahnschrift condensed verified per-pixel at all 13 sites; Cascadia Mono verified by letterform) |
| F-005 | mod row anatomy | conformance | 4 | 5 | Toggle sits Grid.Column=4 of 6, second-from-right (MainWindow.xaml:408-415, :592); language says toggle left. ~1700px eye traverse per row × 27 rows (PNGs 02, 30) | CONFIRMED — needs a design call first: language never accounted for mod art / reorder NumberBox in col0 | Move toggle left of name (design call: art demoted vs toggle-after-art), shift action cluster right | clean (wave 8 — design call made: toggle first, art second; toggle is col 0 AND first declared child so tab/UIA order match) |
| F-006 | all 17 ContentDialogs — primary button | conformance+consistency+a11y | 4 | 4 | Primary renders #0000B2 with #000000 text = 1.64:1 in every theme (zero #3BB4D9 pixels in dialog PNGs 03/09/16/20/28/33). App.xaml:37-44 AccentButtonForeground DOES land (black glyphs); only Background fails — lead suspect is the BrushTransition composition path (generic.xaml:8899-8901), not ThemeDictionaries scoping | CONFIRMED — mechanism corrected by skeptic; verify the BrushTransition lead at fix time | Make dialog primaries consume the theme accent (explicit PrimaryButtonStyle or the brush WinUI actually resolves); confirm mechanism first | clean (wave 1) |
| F-007 | hyperlinks app-wide | consistency+a11y | 4 | 4 | HyperlinkButtonForeground never set; 11 sites; #0000D6 at 1.66:1 (default bg) to 1.92:1 (matrix), 502 px identical across themes. Donate link at MainWindow.xaml:453-454 IS themed — pattern known, applied once. ToolsPanel.xaml:62-67 | CONFIRMED — for MissingTools chips the link is the control's only affordance | HyperlinkButtonForeground (+PointerOver/Pressed) wired to accent in ThemeService.Apply | clean (wave 1) |
| F-008 | all 17 dialogs — shell | conformance | 4 | 4 | No dialog has the 0-radius shell, 3px accent rail, or stencil eyebrow; App.xaml overrides ContentDialog colors but never radius (WinUI 8px stands). SavesDialog.xaml:2, SafeClearDialog.xaml:2 et al; PNGs 03, 09, 16, 19, 20 | CONFIRMED — scope corrected upward from 5 dialogs to all 17. WAVE-2 side effect: the 0-radius third is DONE (OverlayCornerRadius; square shell verified per-pixel on Settings) — remaining: 3px accent rail + stencil eyebrow | DialogShell pattern for rail + eyebrow (radius already handled) | clean (wave 8 — 3px rail + stencil eyebrow via ContentDialog.Title content ×17; title presenter stretched on Title-content Loaded; UIA names preserved, rail/eyebrow Raw view) |
| F-009 | mods screen + library — fills | conformance+consistency | 4 | 4 | Six accent-filled surfaces on one mods screen (LOADER/UPDATE/VARIANT chips MainWindow.xaml:507-517, All filter, Browse Nexus :154-155, Play modded :114-118) vs "primary is the only fill." Black-on-accent at 10 sites (7 in MainWindow.xaml incl :118,:155,:234,:271; LibraryView.xaml:147,:169; MainWindow.xaml.cs:1126) vs documented ThemeBg rule (NexusCatalogView.xaml:163-168) | CONFIRMED — foreground count corrected 6→10; hardcoded Black is a live invariant-1 violation on dark-accent themes | Chips to outline-in-token-color; accent fill reserved for primary; replace Black with ThemeBg at all 10 sites | clean (wave 8 — chips + secondary CTAs to 1px accent outline; Play keeps the only shell accent fill; all Black inks → ThemeBg incl. code-built anti-cheat toggle; Loadout active segment recorded as proposed addition) |
| F-010 | global type ramp | conformance+consistency+a11y | 3 | 5 | 13 distinct FontSize literals (frequencies verified exact; 12 ×90 is de-facto body vs ramp's 13); FontSize=9 at 5 row-action sites (MainWindow.xaml:559-603) below the 10px floor; eyebrow drift 10/11/12 | CONFIRMED — "WinUI ignores OS text-size slider" sub-claim struck as unverified; 9px cluster is the accessibility-flavored piece. WAVE-2: shipped slice clean (zero 9px sites remain; eyebrows uniform at the tag step; ramp published). Remaining: body normalization (95×12 + neighbors) deferred by design; four ramp resources (Hero/RowTitle/Body/Meta) have zero consumers | Body-size sweep wave: route the 12/11/15 literals onto Body/Meta/RowTitle steps with per-surface capture | clean (road-to-zero B11 — body sweep: every 11/12/13/14/15/20 literal (XAML + code-built) routes through Meta/Body/RowTitle/ViewTitle; residual literals are the excluded content class (18 detail-headline, 26/48/56 monogram art); captured post-sweep) |
| F-011 | primary text theming | consistency | 4 | 3 | Only 12/229 TextBlocks use ThemeInk (counts verified exact); no implicit TextBlock style exists; MainWindow 0/47, SettingsDialog 0/35 inherit WinUI white via RequestedTheme="Dark" (MainWindow.xaml:14). PNG 29 green game names vs PNG 30 white mod names | CONFIRMED — invisible in default theme, loud in high-chroma themes. WAVE-1: brush override proven inert. WAVE-2: implicit TextBlock style shipped — static-tree TextBlocks now follow theme ink (per-pixel), but the style structurally CANNOT reach (a) TextBlocks realized inside DataTemplates (ListView/ItemsRepeater/ItemsControl — mod names still #FFFFFF byte-identical across themes) or (b) presenter-generated text (string Content on buttons/checkboxes, TextBox.Header, PlaceholderText, ComboBox display). Remaining fix is per-site: explicit ThemeInk in DataTemplates + control-level Foreground for presenter text | Per-site ThemeInk in the item templates + themed Foreground on presenter-text controls | clean (wave 3 — third attempt; matrix-green mod names with matched glyph coverage, the byte-diff that failed both prior waves) |
| F-012 | ban-risk / danger text | a11y | 4 | 3 | danger-on-bar_bg: 626-labs 3.58:1, obsidian 4.43:1, ember 4.38:1 (independent recomputation matched to 2dp); 11-14px text so 4.5:1 applies; icons at 3:1 pass — text-only failure. LibraryView.xaml:149-155, MainWindow.xaml:126 (bar_bg container), :246-247, :262-269 | CONFIRMED — the app's most safety-critical text is least legible in the shipped default theme | Adjust danger token in the 3 failing builtins (theme data, invariant-clean) + Core test asserting 4.5:1 on bg and bar_bg | clean (wave 1) |
| F-013 | text hierarchy | consistency+a11y | 3 | 4 | 312 Opacity literals across 10 values (histogram verified exact); text_dim/text_muted tokens unconsumed; the 0.45 file-tag composites to 4.47:1 at 11px (narrow fail; 4.23:1 if ever on panel). MainWindow.xaml:441,:444,:447 three opacities in one row | CONFIRMED — faintest element in the row is the mono file tag; ordering arguably inverted. WAVE-3: XAML half clean (102 TextBlocks banded onto soft/dim/muted; exact token hexes verified per-pixel per theme). Residue: 8 code-behind TextBlocks in MainWindow.xaml.cs (:1074,:1096,:1122,:1135,:1326,:1336,:1353,:1450) still raw-opacity — invisible to the XAML-only guard | Convert the 8 code-behind sites + add a .cs arm to the opacity guard law | clean (wave 7 — zero raw opacity on untinted text, XAML + code, law-guarded) |
| F-014 | updates + nexus view titles | conformance | 3 | 4 | Both render 16px Segoe SemiBold sentence case (UpdatesView.xaml:34-35, NexusCatalogView.xaml:36) — off-ramp entirely (not "two steps under"); game-group header at 15 (:68) is 1pt away; empty-state title at 20 outranks the view title; section headers get tracking the titles lack | CONFIRMED — wording corrected; "indistinguishable hierarchy" is the strongest verified part | Establish the view-title role once (21, stencil face + Segoe Semibold fallback, caps, tracking) | clean (wave 2 — 21-vs-10 hierarchy verified; empty-state titles ride F-010's deferred sweep) |
| F-015 | game mods view — search | qol | 3 | 4 | Zero search/filter input in MainWindow.xaml command bar (grep verified); LibraryView (:87-88) and NexusCatalogView (:46) both have search; no type-ahead fallback (SelectionMode="None"). PNG 02 (27 rows) | CONFIRMED — the primary work surface is the only list without find-by-name | Name/author filter box in the MODS bar section, client-side over ViewModel.Mods | clean (wave 4 — ModSearch Core-TDD + live filter; the Mods-render/_allRows-state rule holds across every write/safety path incl. the regroup leak hotfixed in #216) |
| F-016 | mod row toggle feedback | qol | 3 | 4 | row.IsBusy set (MainViewModel.cs:912,931) but bound by nothing; only IsBusy binding is the parent's 18px ProgressRing (MainWindow.xaml:225); toggle not disabled during file-move phase; no reentrancy guard, async void handler — concurrent toggles reachable | CONFIRMED — corrected: parent ring does spin during rescan; file-move phase and row-local feedback are the gaps | Bind row.IsBusy to a pending treatment + toggle IsEnabled; guard reentrancy | clean (wave 4 — all three toggle paths busy+guarded; enabled notifies ToggleIsOn so reverts move the switch; runtime half on smoke item 2) |
| F-017 | mod row readme button | qol | 3 | 4 | ReadmeVisibility true on mere description; GetReadmeMarkdown falls back to the same description (ModRowViewModel.cs:88,91-97; MainWindow.xaml.cs:1218-1229). PNG 12 modal is byte-identical to the row text in PNG 02 | CONFIRMED — "strongest evidence in the set"; the affordance teaches users it lies | Show readme only when a real readme file resolves distinct from the description | clean (wave 4 — affordance gated on the captured readme file; zero buttons on a description-only loadout, verified vs stage-0) |
| F-018 | reset flow naming | copy | 3 | 4 | "Reset" ×3 (SettingsDialog.xaml:212,216,220; SafeClearDialog.xaml:9) then PrimaryButtonText="Clear" (:10), body "After clearing:" (:20); dialog body itself says "resets… to first-run" — reset and clear alternate inside one 22-line file | CONFIRMED — internal class name (SafeClear) leaked into user copy | PrimaryButtonText="Reset launcher"; body "After the reset:" | clean (wave 4) |
| F-019 | nexus action labels | copy | 3 | 4 | Same browser handoff: "Get" (NexusCatalogView.xaml:188 → OnGetClick, Process.Start), "Download on Nexus" (NexusModDetailDialog.xaml:70 — own tooltip disclaims downloading), "Open on Nexus" (:26, recovery panel); fourth phrasing at MainWindow.xaml:156 | CONFIRMED — handler citation corrected (OnGetClick); live defect is Get vs Download-on-Nexus | "Open on Nexus" for the detail-dialog primary; keep "Get" on cards if space demands, but never "Download" | clean (wave 4) |
| F-020 | UI glyphs in strings | copy | 3 | 4 | True emoji 📄🗑 (MainWindow.xaml.cs:751-753); variant-selector glyphs ⚙⚠▶⬇ (MainViewModel.cs:1257-1258, MainWindow.xaml.cs:1071, FrameworkInstallDialog.xaml.cs:22, NexusCatalogView.xaml.cs:34); text glyphs ♥↻ (rule doesn't reach them). Glossary emoji don't match the FontIcons the real buttons render | CONFIRMED — split severity: true emoji high, variant-selector medium, ♥↻ low; glossary is self-refuting | Strip glyphs from strings; render marks as FontIcon beside labels (the file's own dominant pattern) | clean (wave 7 — all emoji + variant-selector glyphs gone; text-presentation ♥↻↓ remain per the skeptic carve-out, boundary rule proposed) |
| F-021 | icon-only buttons + library cards | a11y | 5 | 2 | AutomationProperties: zero occurrences app-wide (verified). 9 icon-only controls incl. 3 destructive (delete profile ProfilesDialog.xaml:36, delete snapshot SavesDialog.xaml:148, remove save mod :114); ToolTip does not feed UIA Name; x:Name feeds AutomationId not Name. Merged: nested Button-in-Button library card (LibraryView.xaml:97/:168) likely yields a nameless invokable card | CONFIRMED — D-49 folded in (concatenation claim struck as speculation; nameless card is the defect) | AutomationProperties.Name on every icon-only control matching tooltip text; name the library card; add a XAML lint guard | clean (wave 5 — 9 named + card; depth-stack law with nested-button control, 18/18) |
| F-022 | global spacing | conformance+consistency | 3 | 3 | Off-scale literals verified exact (Spacing 2×17, 6×23, 10×9; Padding 6,2×16 etc.); row padding 4,8 vs language 7×12 (both axes off); dialog widths 400-620 across 16 files, root Spacing 8/10/12/14; zero shared resources | CONFIRMED — sequencing note: 6 and 10 are the most-used values; wholesale snap re-densifies every row — needs its own wave, not a drive-by | Shared spacing resources; snap in one sweep with before/after capture | clean (road-to-zero B12 — scale published (SpaceXS/S/M/L/XL + RowPadding 12,7 + DialogPadding); the spec-named surfaces snapped (row padding to 7x12, dialog root rhythm to the M step); chip innards (2/6/10 clusters, 6,2 chip padding) recorded as the deliberate micro-density exception in App.xaml — density is a feature, per the spec's own row rule; before/after captured) |
| F-023 | global motion | conformance | 3 | 3 | Zero Storyboard/DoubleAnimation/ThemeTransition/Transitions in src XAML and zero animation APIs in .cs (verified wider than filed); no reduced-motion handling anywhere (the spec's accessibility half) | CONFIRMED — ContentDialog scale-in sub-claim unverifiable from repo (compiled XBF); confirm at runtime before asserting | Duration resources; opacity-only dialog open; UISettings.AnimationsEnabled respect | clean (road-to-zero B10 — motion tokens published (MotionFastMs 80 / MotionGlowMs 160), Bloom reads them, prefers-reduced-motion honored (UISettings.AnimationsEnabled drops the fade live), row hover shifts to the theme glass. Accepted deviations recorded: dialog open animation is compiled XBF and knob motion is composition-internal — re-templating both outweighs the 80ms deltas) |
| F-024 | error surfacing | qol | 3 | 3 | 29 sites (corrected from 26) of StatusText = e.Message incl. the toggle path :929; sink is 12px/0.8-opacity single-slot footer (MainWindow.xaml:636-637); house what-next pattern exists at :1395 and 6 other sites — inconsistency, not ignorance | CONFIRMED — highest-traffic failure path (toggle-while-game-running) reverts the switch under a raw IOException string | Cause+remedy mapper; escalate aborted writes to a persistent surface | clean (wave 4 — ErrorRemedy Core-TDD, HResult-first for localized Windows, 39 sites routed; runtime half on smoke item 3) |
| F-025 | keyboard access | a11y+qol | 3 | 3 | Zero KeyboardAccelerator/AccessKey/TabIndex/IsTabStop app-wide (verified); no Ctrl+F/Ctrl+R/Esc-home in the shell | CONFIRMED IN PART — SelectionMode/arrow-key mechanism claim refuted (selection ≠ focus); "150 Tab presses" struck; Esc-to-back DOES exist in Nexus/Updates views — gap is confined to shell + mod list | Accelerators for bar actions; verify arrow traversal in the mod list at runtime; Space toggles focused row | clean (road-to-zero B7 — Ctrl+F focuses the filter, Ctrl+R refreshes, Esc returns home (all gated to the game view, popups win Esc; Esc-home verified live); Space toggles the focused row through the real enable path) |
| F-026 | add game dialog | qol | 3 | 3 | Five entry paths + nested scrolling verified (outer MaxHeight 640, inner lists 160/180; both clip rows mid-height in PNGs 14/15); AI/batch expanders sit ABOVE the auto-detect path | CONFIRMED — "nothing marks the recommended path" overstated (subcopy exists); real defect is ordering/primacy + no list filter | Lead with detected-Steam list + filter box; demote AI/batch/manual below | clean (road-to-zero B9 — detected-Steam list + filter box lead; popular picker next; AI/batch expanders demoted; checked games survive filtering (checked set is truth, the visible selection is its projection); verified live) |
| F-027 | empty states | copy+qol | 3 | 3 | Bare-absence states at SavesDialog.xaml:72,:156 and SettingsDialog.xaml:131,:150 (+:93 weak — has why, lacks remedy); PNG 19 shows "no save files detected" alongside a 32.4 MB snapshot of the same folder — filtered file list vs extension-agnostic snapshot | CONFIRMED IN PART — register corrected 6→4+1; the contradiction is the sharpest instance | Two-part pattern everywhere (absence + named remedy) | clean (wave 7 + #225 — the verifier caught the instructive copy shipped as DEAD CODE behind a code-behind reassignment; fixed in the fall-through, filtered-listing copy honest) |
| F-028 | INI editor reversibility | copy | 3 | 3 | 22-line dialog, no note (verified by exhaustion); guarantee exists in a row tooltip (MainWindow.xaml:582) and a post-write status (IniEditorDialog.xaml.cs:43) — retrospect only; correct pattern in-app at CharacterEditDialog.xaml:39 | CONFIRMED — same defect shape as F-031; fix both with one pattern | Note above buttons: "Saving snapshots this file first. Restore previous puts back the last snapshot." | clean (wave 6) |
| F-029 | transparency tooltip | copy | 3 | 3 | SettingsDialog.xaml:50 "Solid keeps the navy background…" — false under obsidian #0d0d0d / matrix #020a02 / any user theme | CONFIRMED — an invariant-1 violation in words; theme engine owns color, copy included | "Solid keeps your theme's background opaque." | clean (wave 6) |
| F-030 | character edit intro | copy | 3 | 3 | CharacterEditDialog.xaml.cs:11 composes name — class with no guard; Class is string.Empty ALWAYS (EldenRingSave.cs:453 defers detection; no other producer) — every user sees 'Editing "X" — , currently Lv N.' every time | CONFIRMED + UPGRADED — not an edge case; the universal case | Drop the segment when class is empty | clean (wave 4) |
| F-031 | snapshot restore note timing | copy | 3 | 3 | Restore fires straight into OnRestore (SavesDialog.xaml:126-156); snapshot-first guarantee appears only in post-write status (SavesDialog.xaml.cs:324,:385) | CONFIRMED — engineering earned confidence the copy fails to buy; pair with F-028 | Note under Snapshots header: "Restoring snapshots your current save first, as 'before-restore'." | clean (wave 6 — note before the write + #221 scroll wrapper after Este caught the clip live) |
| F-032 | tool configure reachability | qol+a11y | 4 | 2 | Sole construction site is OnToolRightTapped (ToolsPanel.xaml.cs:51); no ContextFlyout so keyboard menu path dies unhandled; tooltip says only "Click to launch."; rename/re-pick/uninstall exist NOWHERE else — tools cannot be uninstalled by any keyboard-reachable or documented route (Settings tools static vs frameworks with buttons, SettingsDialog.xaml:130-147 vs :149-180) | CONFIRMED + RAISED — mouse-only route to the app's only tool-management surface | Real ContextFlyout + visible affordance + Configure/Uninstall in Settings tool rows | clean (wave 6 — keyboard flyout + game-labeled Settings rows via flag+Hide hand-off; rail-chip visual affordance recorded as low-sev nicety) |
| F-033 | library-home drop intake | correctness | 4 | 2 | RootGrid AllowDrop live behind the home overlay (MainWindow.xaml:13; handler .cs:1516-1528 captions "Install to active game" naming no game); _ctx stays bound to last game (ShowLibrary clears only chrome); result reported solely to StatusText (:2225,:2380) which the home paints over (LibraryHost RowSpan=4, :644) | CONFIRMED + RECLASSIFIED — occlusion itself is correct; the defect is an ungated write path whose only receipt is painted over. Reversible via holding folder, so not catastrophic | Name the target game in the drop caption + surface the receipt on the home, or refuse drops on home | clean (wave 6 — drops refused on home AND storefront overlays; game-view caption names the target) |
| F-034 | mod row UIA names | a11y | 4 | 2 | Toggle has OnContent=""/OffContent="", no Name (MainWindow.xaml:592-595) — empty UIA name ×27; action buttons named by generic captions — 27 identical "uninstall" buttons distinguishable only by tree position | CONFIRMED — framing corrected: rows themselves announce the mod name; the controls inside don't | Bind AutomationProperties.Name per row: "Enable Faster Ships", "Uninstall Faster Ships" | clean (wave 5 — state-aware via ToggleIsOn incl. variant families) |
| F-035 | status announcements | a11y | 4 | 2 | 104 StatusText assignments (verified exact); zero LiveSetting/RaiseNotificationEvent/AutomationPeer app-wide; sink is a plain TextBlock (MainWindow.xaml:636-637) | CONFIRMED — "highest-leverage finding in the set": one attribute turns the whole channel audible | LiveSetting=Polite on status (Assertive for errors); same for per-dialog status blocks | clean (wave 5 — LiveSetting + LiveRegionChanged raised on every write at all 4 sinks; Narrator confirmation on smoke) |
| F-036 | theme token wiring | consistency | 3 | 2 | 12 of 22 color tokens unconsumed (corrected from 13): 7 are real defects (warning/text_dim/text_muted/tag_* — surfaces the shell fakes today), 5 are forward-declared slots (no shell counterpart); ThemeWarning read at runtime (ModRowViewModel.cs:279) but never re-colored — live latent bug | CONFIRMED — split defects from forward-declarations; NormalizeTheme guarantees all tokens present, so wiring is safe | Set ThemeWarning in Apply now; add brushes for the 7 as their surfaces get fixed (F-013, F-009) | clean (wave 1 — minimal slice: ThemeWarning + ThemeInkMuted + ThemeInfo; remaining brushes land with F-013/F-009) |
| F-037 | safe-clear button fill | conformance | 3 | 2 | SafeClearDialog.xaml:10-12 (citation corrected): Clear neutral, Cancel accent-filled via DefaultButton=Close; zero ThemeDanger references in the dialog | CONFIRMED — errs safe today; language deviation not hazard | Danger-fill the primary, keep DefaultButton=Close | clean (road-to-zero B4 — element-scoped ButtonBackground*/Foreground* state keys on PrimaryButton via Title-Loaded hook; fill holds through hover/pressed, live-brush instances) |
| F-038 | nexus unavailable copy | copy | 3 | 2 | Five verbatim "no source loaded" variants (MainViewModel.cs:1814,1849,1906,1982,2057); "source" is internal IModSource vocabulary; no next step | CONFIRMED | "Nexus isn't connected. Connect your account in Settings → Nexus Mods." | clean (wave 6 — five sites, zero remnants) |
| F-039 | saves "Only" button | copy | 2 | 3 | DropDownButton Content="Only" (SavesDialog.xaml:141) beside Restore; meaning tooltip-only, not keyboard/touch reachable | CONFIRMED | Content="Restore one type" | clean (wave 6) |
| F-040 | hit targets | a11y | 3 | 2 | MP badge ~17px (no MinHeight, MainWindow.xaml:532-535), glossary "?" Height=20 (:400-405); both real targets under 24×24 | CONFIRMED — downgraded: SC 2.5.8 spacing exemption plausible for at least one; measure before calling it a definite failure | MinHeight/MinWidth 24 — pad the target, not the paint; measure exemption first | clean (wave 5 — both targets floored at 24, hard Height cap removed) |
| F-041 | steam-build banner button | copy | 2 | 2 | Content="Mods rechecked" on the dismiss/re-baseline button (MainWindow.xaml:374; comment :360 confirms) — past-tense status as control | CONFIRMED — persistence claim dropped; stands on source alone | Content="Mark as rechecked" | clean (wave 6) |
| F-042 | chip explanations | a11y | 2 | 2 | ~9 chips (corrected from 16) whose only explanation is a tooltip on a non-focusable Border: MANAGED, UPDATE, UE4SS BUILT-IN, ban-risk, tier; glossary covers LOADER/VARIANT+ but renders only in the mods view | CONFIRMED IN PART — keyboard-reachable glossary exists (MainWindow.xaml:400 → .cs:710-764); gap is uncovered chips + other views | HelpText on the uncovered chips; extend glossary reach to library + catalog views | clean (wave 5 — 16 HelpText on peer-bearing TextBlocks; glossary-reach extension recorded as remaining nicety) |
| F-043 | ini casing | copy | 1 | 3 | "ini" (MainWindow.xaml:587) vs "Edit INI" (IniEditorDialog.xaml:6) vs "Edit .ini files" (tooltip :582) — three casings in one interaction | CONFIRMED — rescoped: lowercase micro-caption register is a defensible choice; only the INI clash survives | Pick one casing (INI) across the interaction | clean (road-to-zero B1 — INI casing unified across caption/tooltip/dialog) |
| F-044 | AI helper naming | copy | 1 | 2 | "AI agent" (AddGameDialog.xaml:24) vs "AI chat" (NewThemeDialog.xaml:17) for the identical flow; "Agent JSON" (:27) vs "Theme JSON" (:20) — one named for producer, one for content | PARTIALLY REFUTED → rescoped: within-dialog shortening is ordinary English, not renaming; the two cross-dialog inconsistencies survive | "AI chat" + content-named headers ("Pasted JSON") in both | clean (road-to-zero B1 — "AI chat" both dialogs; content-named "Pasted JSON" headers) |
| F-045 | palette swatches | a11y | 1 | 2 | RenderPaletteStrip emits bare Rectangles (SettingsDialog.xaml.cs:554-566); no text alternative | CONFIRMED — downgraded: non-interactive preview where color IS the content; narrow SC 1.1.1 gap; the checkbox it informs is properly labeled | AutomationProperties.Name + tooltip = hex value per swatch | clean (wave 5 — visible hex captions; a UIA name on a peer-less Rectangle reaches nobody) |
| F-047 | presenter-generated text (button Content, TextBox.Header, placeholders, ComboBox display) | consistency | 3 | 4 | Structurally unreachable by any TextBlock mechanism (wave-2 re-review, per-pixel across themes: "Enable all" #FFFFFF ×46 identical in all three; Theme name header, checkbox labels, Search placeholder all inert) | ADOPTED from wave-2 proposed additions (Este, wave-3 gate) | Foreground system-key family (Button/CheckBox/ComboBox/TextControl incl. placeholder + header) wired app-level + Apply + DialogTheming | clean (wave 3 — every sampled presenter string tracks theme ink; placeholder surfaces source-verified only, no capture shows one) |
| F-048 | regression guard for shipped design laws | consistency | 3 | 2 | Waves 1-2 laws (0-radius, no 9px, no hardcoded mono, tokened dimming, template ink) have no test; any new PR regresses them silently | ADOPTED from wave-2 proposed additions (Este, wave-3 gate) | XAML-lint test over src/ModManager.App XAML + code-behind | clean (wave 3 — 5 laws + 9 synthetic positive/negative controls + glob guard, 15/15) |
| F-046 | theme import contrast hint | a11y | 1 | 2 | NormalizeTheme checks presence only (Themes.cs:145-165); zero contrast math anywhere in src (verified); advisory-only proposal, user theming stays unrestricted | CONFIRMED — pure-function ratio math belongs in Core; invariant-clean on both counts | Themes.ContrastReport in Core + advisory warnings in NewThemeDialog StatusText | clean (wave 7 — total advisory (TryRatio), complete import flow incl. warned-path apply + re-armed retry) |

## Refused findings (recorded, not resurrected)

| claim | lens | skeptic's grounds |
| --- | --- | --- |
| Updates view is a dead end | qol | Affordance exists and is signposted: "Open game" tooltip names the destination, which carries per-mod Nexus hyperlinks (MainWindow.xaml:471-472). Residual: UpdateRow discards NexusModId/Domain it already has — a low-severity deep-link convenience, not a dead end. |
| No way to check all games for updates | qol | Factually wrong (footer names a count, not the games) and out of scope: the Updates surface is contracted "No network, ever" (UpdatesView.xaml.cs:60-63); check-all is a new networked feature against the user's rate-limited key, not a missing affordance. |
| First run hides the fastest path | qol | The empty-state copy names "+ Game up top", which opens multi-select "Quick add from Steam" — strictly faster than the collapsed home lane. Residual: the "finds installed games below" clause is circular — a low copy nit. |
| Connect-Nexus instruction points at a control that doesn't exist | copy | A Connect Nexus chip exists on the toolbar (MainWindow.xaml:304-316, label binds "Connect Nexus" when disconnected). Residual: ASCII "->" vs "→" and a third phrasing variant at MainViewModel.cs:1587 — typography nit. |
| Nested-button library card announces a concatenated name | a11y | Concatenation claim was speculation — a bare Grid content peer more likely yields an EMPTY name. The real defect (nameless invokable card) is merged into F-021. Click-shadowing concern nonexistent (Click doesn't bubble to ancestor Buttons). |
| Save-editor stat headers unnamed | a11y | Header IS the documented accessible-name source for NumberBox; SC 4.1.2 satisfied. Residual is SC 3.1.4 (abbreviations) — Level AAA, and the 3-letter forms are FromSoft domain convention. |

## Wave 1 outcome (2026-08-03, PR #209, re-reviewed post-merge)

Wave `theme-engine-owns-color` shipped F-003/006/007/011/012/036. Close-out
re-review over 9 re-captures (3 surfaces × 3 themes): **F-003, F-006,
F-007, F-012, F-036 clean** (per-pixel: zero legacy-accent pixels remain);
**F-011 stays open** — the app-level `TextFillColorPrimaryBrush` override
is inert (framework theme dictionary shadows it under
`RequestedTheme="Dark"`; byte-identical white pixel counts across themes).
Wave status: `shipped` (residue). Evidence: `docs/ui-evidence/wave-1-recapture/`.

**Proposed register additions (discovered during the wave, not fixed —
pending Este's approval before they get F-ids):**

1. Accent keys outside the wave-1 sweep: ProgressRing, NumberBox spin
   buttons, InfoBar severity fills have no App.xaml overrides. NumberBox
   spin buttons verified theme-neutral grey in re-capture (a downstream
   F-011 instance); ProgressRing/InfoBar are source-level inference only —
   need their own capture before severity is assigned.
2. Retired sub-4.5:1 danger hexes still live as `tag_vortex` in 626-labs
   (#e13a5a) and ember (#ef4444). Inert today (no WinUI consumer), but
   `NormalizeTheme` falls `tag_vortex` back to the FIXED danger for user
   themes — builtins and user themes will disagree the moment a shell
   surface consumes `tag_*` (F-009/F-013 territory).
3. Durability gap: `ThemeBrushContractTests` locks brush keys three ways,
   but nothing guards DialogTheming call-site parity — a future
   `new ContentDialog` without `DialogTheming.Apply` regresses silently.

## Wave 2 outcome (2026-08-03, PR #211, re-reviewed post-merge)

Wave `hangar-skeleton` shipped F-001/004/010/011/014. Close-out re-review
over 15 re-captures (5 surfaces × 3 themes, all content-verified, no
mislabels): **F-004 and F-014 clean** (Bahnschrift condensed + Cascadia Mono
verified by letterform; 21-vs-10 title hierarchy); **F-001, F-010, F-011
stay open** at reduced scope (see updated rows). F-008's radius third
closed as a side effect. Wave status: `shipped` (residue).
Evidence: `docs/ui-evidence/wave-2-recapture/`.

**Proposed register additions from wave 2 (pending approval):**

1. Non-TextBlock ink surfaces (TextBox.Header, PlaceholderText, string
   Content on buttons/checkboxes, ComboBox display text) are structurally
   unreachable by any TextBlock mechanism — need their own finding + fix.
2. `Style="{x:Null}"` under a non-HyperlinkButton parent is an ink hole
   (ToolsPanel.xaml:109 renders framework white in every theme) — rule:
   x:Null only under controls whose own Foreground is themed.
3. No test guards the wave-2 laws — a new `CornerRadius="6"`, `FontSize="9"`,
   hardcoded mono string, or bare DataTemplate TextBlock regresses silently.
   A XAML-lint test over src/ModManager.App would hold all of it.
4. Four dead ramp resources (Hero/RowTitle/Body/Meta) — declared, zero
   consumers, no drift guard until the deferred body sweep.
5. Capture-fixture blind spots: Launch options button and tools-rail
   HyperlinkButton appear in no capture (visibility-gated) — two wave-2
   fixes are source-verified only; fixture needs a game+tool combo that
   renders them.

## Wave 3 outcome (2026-08-03, PR #213, re-reviewed post-merge)

Wave `ink-completion` shipped F-011/F-013/F-047/F-048 (F-001 residue
deliberately deferred — re-templating the core control blind inside a sweep
wave was the wrong risk). Re-review over 9 re-captures (3 surfaces × 3
themes): **F-011 clean** (third attempt — matrix-green mod names, matched
glyph coverage), **F-047 clean**, **F-048 clean** (15/15 incl. its own
positive controls); **F-013 open** on the 8 code-behind opacity sites the
XAML-only guard can't see. Wave status: `shipped` (residue).
Evidence: `docs/ui-evidence/wave-3-recapture/`.

**Proposed register additions from wave 3 (pending approval):** (1) .cs arm
for the opacity guard law + the 8 MainWindow.xaml.cs sites; (2) companion
non-empty-glob guard for the *.cs scans; (3) `Style=` exemption in the ink
laws is unconditional — a non-ink style passes; (4) six presenter/placeholder
surfaces never captured (LibraryView, NexusCatalog×2, AddGame, Saves,
Profiles, NewTheme) — placeholder clause is pixel-unprovable today;
(5) evidence protocol: pin the same game across theme rounds so byte-diffs
stay available; (6) MainViewModel.cs:301 falls back to Colors.White on
ThemeInk lookup miss — the last hardcoded white in the ink path.

## Wave 4 outcome (2026-08-03, PRs #215 + #216, re-reviewed post-merge)

Wave `felt-qol` shipped F-015/016/017/018/019/024/030. Close-out re-review
(4 captures + deep source audit): **all seven clean** — the campaign's
first fully clean wave. The re-review caught one filter-state leak on
master (regroup fed the filtered list into _allRows; Disable-all/vanilla
then acted on the visible subset) — hotfixed same-session in #216.
Wave status: `clean`. Evidence: `docs/ui-evidence/wave-4-recapture/`.

**Proposed register additions from wave 4 (pending approval):** (1) zero-
match filter needs an empty state ("No mods match 'x'" — blank list reads
as broken); (2) filter should also match the on-screen FileTag key
(2RingSlots etc.); (3) filter survives a game switch (pre-narrowed first
render); (4) SettingsDialog.xaml.cs:546 is the last bare ex.Message
surface; (5) two SavesDialog sites surface CLR type names in user copy —
decide diagnostic-vs-remedy.

## Wave 5 outcome (2026-08-03, PR #218, source-verified close-out)

Wave `heard-not-just-seen` shipped F-021/034/035/040/042/045 — **all six
clean** (second fully clean wave). Mid-wave review caught three mechanisms
inert against UI Automation (HelpText on peer-less Borders, LiveSetting
without LiveRegionChanged, names on peer-less Rectangles) — all re-landed
on peer-bearing targets and verified on master. Runtime Narrator
confirmation delegated to the smoke checklist. Wave status: `clean`.

**Proposed register additions from wave 5 (pending approval):** (1)
WireLiveRegion uses FromElement — first status write can be missed if no
peer exists yet; smoke must confirm; (2) per-item names for the other list
surfaces (SavesDialog rows, ToolsPanel frameworks, ProfilesDialog needs a
wrapper); (3) two resource-fetch idioms coexist (hard-cast vs as+fallback)
— pick one; (4) documented detector gap: glyph-string Content buttons
escape the icon-only law (zero today).

## Wave 6 outcome (2026-08-04, PRs #220/#221/#222, re-reviewed post-merge)

Wave `reveal-prep` shipped F-028/029/031/032/033/038/039/041 — **all eight
clean** (third fully clean wave). Two same-session regression fixes: the
SavesDialog scroll wrapper (Este caught the clip live in re-capture, #221)
and the NOTICE-line truncation the wave's own Configure column caused
(verifier catch, #222). Wave status: `clean`.
Evidence: `docs/ui-evidence/wave-6-recapture/`.

**THE REVEAL GATE IS OPEN: zero severity-4+ findings remain open.**
(CORRECTED 2026-08-04 at the reveal gate: this claim was wrong — F-002, F-005,
F-008, F-009 were still open. Scoreboard lines must be re-derived from the
register rows, never carried forward. Wave 8 closed all four.)

**Proposed register additions from wave 6 (pending approval):** (1)
GameLabel shows the folder key ("For windrose") not the display name;
(2) tool rail chip carries no visible menu affordance (tooltip + Settings
route cover it — low-sev); (3) capture 01 renamed tools-rail (no flyout
shown; drag/flyout states need a two-person capture protocol).

## Wave 7 outcome (2026-08-04, PRs #224/#225, re-reviewed post-merge)

Wave `loose-ends`: **F-001, F-013, F-020, F-027, F-046 clean; F-037 open**
(fill-at-rest shipped; hover VSM treatment remains). The verifier caught two
in-wave defects fixed same-session in #225: the F-027 copy shipped as dead
code (code-behind reassignment), and F-037's bare style would have replaced
the entire Button template. Wave status: `shipped` (F-037 residue).

**Proposed register additions from wave 7 (pending approval):** (1)
dead-string detector — XAML Text on x:Name'd elements reassigned in
code-behind; (2) VSM-aware danger-button rule (BasedOn + scoped hover keys);
(3) raw-opacity dimming on non-TextBlock text carriers (HyperlinkButton,
FontIcon); (4) glyph carve-out needs a written codepoint allowlist (♥↻↓ grew
by one this wave); (5) SDK-pin guard for SquaredControls.xaml (csproj
version vs template header); (6) warn-on-APPLY for low-contrast themes
(recompute at surface — Este's persistence question, answered: derive,
don't persist).

## Wave 8 outcome (2026-08-04, PR #227, re-reviewed post-merge)

Wave `conformance-heavies` — the four severity-4+ rows the reveal gate
surfaced: **F-002, F-005, F-008, F-009 all clean.** F-002's glow rule
finally consumes accent_bloom: a hand-rolled composition DropShadow service
(no toolkit dep) blooms Play modded (accent) and the ban-risk banner
(danger), restyled live by ThemeService.Apply, 160ms ease-out on appearance
— halo pixel-verified against the shell background. F-005 landed Este's
design call (toggle first, art second) in both geometry and declaration
order. F-008's rail needed two mechanism discoveries: the stock template
pins the Title presenter HorizontalAlignment=Left (stretched via
DialogTheming), and Opened races the popup tree wiring in both directions —
the Title content's own Loaded is the deterministic hook. F-009 demoted six
accent fills to outlines and killed the hardcoded Black inks.

Pre-merge review (fresh Opus) caught three should-fixes landed in #227's
second commit: the anti-cheat toggle ink claimed-but-not-shipped (the smoke
doc asserted a fix the diff didn't contain — corrected both), toggle
declaration order (tab/UIA order followed declaration, not Grid.Column),
and the 17 dialogs' lost UIA names from non-string Titles. Reviewer's
"bloom may render nothing" was refuted with on-screen pixel sampling.
Post-merge re-review: CLEAN, one stale-comment nit (NexusCatalogView update
chip still documented the fill rationale) fixed in this close-out.
Wave status: `clean`.
Evidence: `docs/ui-evidence/wave-8-verify/` (UIA-driven captures).

**Proposed register additions from wave 8 (pending approval):** (1) toggle
and active-nav bloom — the two sanctioned glow surfaces not yet consuming
accent_bloom (needs a per-control composition strategy, not the shell
host-Border pattern); (2) Loadout active segment is an accent fill with
Colors.Black ink outside F-009's shipped scope; (3) code-built ContentDialogs
(~22 sites) have no rail/eyebrow shell — decide whether the shell is
XAML-fleet-only by design or the builder pattern should grow one.

## Post-campaign additions (2026-08-04 — Este's "get to 0" directive; F-ids assigned from the wave proposals, see docs/superpowers/plans/2026-08-04-road-to-zero.md)

| ID | Surface | Lens | Sev | Vis | Evidence | Verification | Fix | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F-049 | accent keys outside wave-1 sweep | conformance | 2 | 2 | ProgressRing / NumberBox spin / InfoBar severity fills have no App.xaml overrides; spin buttons pixel-confirmed theme-neutral grey (wave-1 recapture) | from wave-1 close-out | Add key overrides + ThemeService.Apply lines; capture ProgressRing/InfoBar first | clean (road-to-zero B5 — ProgressRing x2 accent Foreground; InfoBar error family themed at app + popup scope (5 keys in App.xaml/Apply/DialogTheming.SharedKeys). NumberBox compact spin flyout stays stock — RepeatButtons use global Button keys, no scoped override exists; accepted, low-vis) |
| F-050 | builtin theme data | consistency | 2 | 1 | Retired sub-4.5:1 danger hexes live as tag_vortex in 626-labs (#e13a5a) and ember (#ef4444); NormalizeTheme falls user themes back to fixed danger — builtin/user divergence when tag_* gets a consumer | from wave-1 close-out | Update the two builtin hexes to their theme danger values | clean (road-to-zero B5 — 626-labs tag_vortex #e13a5a→#f25c73, ember #ef4444→#f25545; builtin/user parity restored, contrast theories re-passed) |
| F-051 | non-TextBlock ink surfaces | conformance | 2 | 2 | TextBox.Header/Placeholder/string-Content/ComboBox display unreachable by TextBlock styles | Covered by F-047's system-key sweep (wave 3) | — | clean (duplicate of F-047) |
| F-052 | x:Null ink hole | conformance | 2 | 2 | Style="{x:Null}" under non-HyperlinkButton parent renders framework white every theme (ToolsPanel.xaml:109) | from wave-2 close-out | Fix the site; rule: x:Null only under controls whose own Foreground is themed | clean (road-to-zero B5 — site verified moot: since wave-3's app-level ButtonForeground theming, x:Null under a Button inherits themed ink; the lint rule lands with B8) |
| F-053 | design-law regression guard | consistency | 2 | 1 | No test guarded wave-2 laws at the time | Landed as DesignLawTests (waves 3–5): radius/type/mono/dimming/ink laws + detector controls | — | clean (shipped as DesignLawTests) |
| F-054 | opacity guard .cs arm | consistency | 1 | 1 | Raw-opacity dimming law lints XAML only; 8 MainWindow.xaml.cs sites unlinted | from wave-3 close-out | Extend DesignLawTests to .cs Opacity assignments on text carriers | clean (already shipped — FindRawOpacityDimmingCode + the *.cs scan landed with the wave-7 close; verified present) |
| F-055 | lint glob totality | correctness | 1 | 1 | *.cs design-law scans would pass vacuously on an empty glob | from wave-3 close-out | Non-empty-glob guard asserts the scan saw files | clean (already shipped — App_sources/Cs_sources non-empty guards landed with the earlier law waves; verified present) |
| F-056 | ink-law Style= exemption | correctness | 1 | 1 | Any Style= attribute exempts a TextBlock from ink laws — a non-ink style passes | from wave-3 close-out | Exempt only styles that actually set Foreground | clean (road-to-zero B8 — Style= exempts only x:Null or styles that actually set Foreground (ink-style set parsed from App resources); synthetic control added) |
| F-057 | evidence coverage | process | 1 | 1 | Six presenter/placeholder surfaces never captured (LibraryView, NexusCatalog x2, AddGame, Saves, Profiles/NewTheme) | from wave-3 close-out | Capture them (UIA-driven mode) or record why not | clean (road-to-zero B13 — 5/6 captured UIA-driven (docs/ui-evidence/road-to-zero-b13/): AddGame placeholders, Saves list, Profiles empty, NewTheme JSON box, library placeholder; NexusCatalog needs a connected plugin — pinned to Este's next connected session in the checklist) |
| F-058 | evidence protocol | process | 1 | 1 | Theme rounds captured different games — byte-diffs meaningless | from wave-3 close-out | Pin the same game across rounds; write into capture checklist | clean (road-to-zero B13 — pin-the-game protocol written into the capture checklist; Windrose is the pinned reference) |
| F-059 | mods filter empty state | qol | 2 | 3 | Zero-match filter renders a blank list — reads as broken | from wave-4 close-out | "No mods match 'x'." empty state | clean (road-to-zero B3 — named zero-match state, only when a query narrowed a non-empty list) |
| F-060 | mods filter scope | qol | 2 | 2 | Filter misses the on-screen FileTag key (2RingSlots etc.) | from wave-4 close-out | Match FileTag too | clean (road-to-zero B3 — ModSearch overload matches FileTag; 6 Core cases) |
| F-061 | mods filter lifetime | qol | 2 | 2 | Filter text survives a game switch — next game renders pre-narrowed | from wave-4 close-out | Clear filter on game switch | clean (road-to-zero B3 — filter cleared on game switch) |
| F-062 | last bare ex.Message | copy | 1 | 1 | SettingsDialog.xaml.cs:546 surfaces raw exception text | from wave-4 close-out | Route through ErrorRemedy | clean (road-to-zero B1 — cause framing on the Apply failure) |
| F-063 | CLR type names in copy | copy | 1 | 1 | Two SavesDialog sites print CLR type names to users | from wave-4 close-out | Cause + remedy copy | clean (road-to-zero B1 — CLR types to Debug only; user copy is cause + file) |
| F-064 | live-region first write | a11y | 2 | 1 | WireLiveRegion uses FromElement — a missing peer at first status write drops the announcement | from wave-5 close-out | Smoke-confirm; if dropped, defer wiring to first peer availability | clean (road-to-zero B7 — CreatePeerForElement fallback: the peer exists before the first write's LiveRegionChanged; Narrator confirmation stays on smoke) |
| F-065 | per-item names, other lists | a11y | 2 | 2 | SavesDialog rows / ToolsPanel frameworks / ProfilesDialog lack per-item UIA names | from wave-5 close-out | Name them (ProfilesDialog needs a wrapper) | clean (road-to-zero B7 — per-item UIA names: SaveFileRow clone, ProfileRow wrapper (load/delete), framework edit pencil) |
| F-066 | resource-fetch idiom | consistency | 1 | 1 | Hard-cast and as+fallback coexist for Application.Current.Resources fetches | from wave-5 close-out | Pick hard-cast (fail loud); sweep | clean (road-to-zero B8 — five as+fallback sites swept to hard-cast; ThemeBrushContractTests guards the keys) |
| F-067 | icon-only detector gap | correctness | 1 | 1 | Glyph-string Content buttons escape the icon-only-button law (zero instances today) | from wave-5 close-out | Extend detector; keep the zero | clean (road-to-zero B8 — glyph-string Content counts as icon in the icon-only law; control added, zero instances confirmed) |
| F-068 | GameLabel copy | copy | 2 | 3 | Drop caption shows the folder key ("For windrose") not the display name | from wave-6 close-out | Use the game display name | clean (road-to-zero B1 — display name via the VM's games list, folder-key fallback) |
| F-069 | tool rail chip affordance | qol | 1 | 2 | Chip carries no visible menu affordance (tooltip + Settings route exist) | from wave-6 close-out | Decide: accept current routes, or add a chevron | clean (road-to-zero B13 — decision: accept tooltip + Shift+F10 + Settings rows as the affordance; a chevron adds chrome the rail's density doesn't want. Revisit only if users miss the menu in the wild) |
| F-070 | interaction-state capture | process | 1 | 1 | Drag/flyout/hover states not capturable single-handed | from wave-6 close-out | Two-person protocol note in capture checklist | clean (road-to-zero B13 — UIA-driven single-shot is the first-choice protocol (proven: hover via cursor-park, flyouts via invoke); two-person capture is the fallback; written into the checklist) |
| F-071 | dead-string detector | correctness | 1 | 1 | XAML Text on x:Name'd elements reassigned in code-behind ships dead copy (F-027's failure mode) | from wave-7 close-out | DesignLawTests detector | clean (road-to-zero B8 — dead-string detector pairs each XAML literal-Text x:Name with its code-behind assignments; allowlist carries justifications (CharactersEmpty)) |
| F-072 | VSM-aware danger rule | conformance | 2 | 2 | Bare BasedOn danger fill loses to hover VSM (F-037's residue mechanism) | from wave-7 close-out | Rule + scoped ButtonBackgroundPointerOver pattern | clean (road-to-zero B4 — rule written: .claude/rules/vsm-danger-buttons.md; SafeClearDialog is the reference implementation) |
| F-073 | opacity dimming carriers | conformance | 2 | 2 | HyperlinkButton/FontIcon dimmed via raw Opacity — outside the tokened-dimming law | from wave-7 close-out | Token brushes; extend the law to these carriers | clean (road-to-zero B8 — opacity law extended to HyperlinkButton/FontIcon/Run; the two live sites (folder glyph, Get-it-here link) moved to token ink) |
| F-074 | glyph allowlist | consistency | 1 | 1 | Emoji-strip carve-out has no written codepoint list; grew silently | from wave-7 close-out | Write the allowlist; DesignLawTests reads it | clean (road-to-zero B8 — allowlist written into the law itself (— … → ↻ ♥) with per-glyph justification; scan enforces it) |
| F-075 | SquaredControls SDK pin | correctness | 1 | 1 | Re-templated ToggleSwitch is a WASDK 2.1.x snapshot; csproj bump would silently drift | from wave-7 close-out | Test: csproj WASDK version == template header version | clean (road-to-zero B8 — csproj WindowsAppSDK version must appear in SquaredControls.xaml's provenance header; failure message says how to re-extract) |
| F-076 | contrast warn-on-apply | qol | 2 | 2 | Contrast advisory fires on import only; applying a stored low-contrast theme is silent | from wave-7 close-out | Recompute at apply; status-line advisory (derive, don't persist) | clean (road-to-zero B5 — ContrastReport recomputed on every applied theme, status-line advisory, skipped during startup restore) |
| F-077 | toggle + active-nav bloom | conformance | 3 | 3 | Two sanctioned glow surfaces still not consuming accent_bloom; shell host-Border pattern doesn't fit per-control | from wave-8 close-out | Per-control composition strategy (template part or attached glow) | clean (road-to-zero B6 — AttachStateGlow: row toggles glow while ON (recycle-safe, bounded by container pool), Loadout active segment glows while accent-filled (reference-equality signal); both pixel-verified live) |
| F-078 | Loadout active segment | conformance | 2 | 3 | Accent fill + Colors.Black ink outside F-009's shipped scope (MainViewModel.cs Loadout brushes) | from wave-8 close-out | ThemeBg ink; align with fill discipline | clean (road-to-zero B4 — active segment ink is the resource-backed ThemeBg instance; re-themes live like the inactive ThemeInk) |
| F-079 | code-built dialog shells | conformance | 2 | 2 | ~22 code-built ContentDialogs have no rail/eyebrow shell | from wave-8 close-out | DialogTheming auto-wraps string Titles into the shell (eyebrow optional per site) | clean (road-to-zero B6 — DialogTheming auto-wraps string Titles into rail + title with the UIA name preserved; rail Raw view; stretch hook applies; verified live on the code-built glossary dialog) |
| F-080 | theme persistence | qol | 3 | 5 | Theme choice is not persisted anywhere; every launch lands on Default — post-Forge this is the whole user experience | discovered at the reveal (default flip) | Persist theme id (camelCase JSON, round-trip test); restore on launch; Default only for first-run/missing | clean (road-to-zero B2 — themeId in app-settings.json (camelCase, serializer-escaped), restored in the VM ctor, saved on every pick; restart-proven live: Obsidian survived kill+relaunch, deleted/missing ids fall back to Forge) |

## Road-to-zero outcome (2026-08-04, PRs #232-#248)

Este's directive after the reveal: **get the register to 0.** Fourteen
batches later it is — **80 of 80 rows clean, re-derived from the rows
themselves at close** (the only scoreboard rule this register trusts).
The 8 remaining audit rows and all 32 wave proposals (F-049-F-080) closed:
theme persistence, the filter trio, danger-hover discipline, theme-key
gaps, the glow rule's last two surfaces + code-built dialog shells,
keyboard access + per-item names, nine lint-armor detectors, the add-game
reorder, motion tokens + reduced-motion, the body type sweep, and the
spacing scale. Two mid-sweep consolidated reviews (fresh Opus) caught ten
should-fixes — all landed same-session. Documented acceptances live in
their rows (NumberBox spin flyout, dialog-open XBF animation, knob timing,
chip micro-density, Loadout hover/glow divergence); each names its reason.
Evidence: docs/ui-evidence/road-to-zero-b13/ + wave-8-verify/.

## Audit notes


- Skeptic batching: 9 refuters over 52 findings instead of one-per-finding,
  to stay inside the approved cost gate (~14 agents total). Every refuter
  had zero authorship of the findings it judged.
- Root-cause clusters worth fixing together: F-003/F-006/F-007 are one
  ThemeService.Apply brush-key sweep; F-011/F-013/F-036 are one
  token-to-brush wiring pass; F-001/F-010/F-022 are one resource-tokenization
  sweep. F-028/F-031 are one reversibility-note pattern.
- Two mechanism leads to verify at fix time: dialog-button Background
  resolution (BrushTransition composition path, F-006) and ContentDialog's
  open animation (compiled XBF, F-023).
