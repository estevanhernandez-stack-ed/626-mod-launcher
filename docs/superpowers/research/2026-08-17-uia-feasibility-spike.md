# Can an agent see and drive the app? — UIA feasibility spike

**Date:** 2026-08-17 · **For:** backlog E2 · **Status:** answered, nothing kept

Ran against the **installed Store package (0.17.0.0)** with a real 27-mod Windrose library open —
the shipped binary, out of process, not a test host.

## Verdict

**Feasible, and cheaper than expected. The harness is not the hard part; identification is.**

| Question | Answer |
|---|---|
| Can UIA read a WinUI 3 tree out of process? | **Yes.** 625 elements, full walk in **0.8s** |
| Can it drive controls? | **Yes.** 163 `Invoke`, 31 `Toggle`, 4 `ExpandCollapse`, 3 `Scroll` |
| Can it read rendered state? | **Yes.** 357 `Text` patterns; 92% of elements carry a Name |
| Can it distinguish visible from hidden? | **Yes.** `IsOffscreen` — 342 of 625 were offscreen |
| Can it identify controls reliably? | **No.** This is the finding |

## The finding: identification rests on display strings

**Zero `AutomationProperties.AutomationId` in the entire XAML.** Only 11 elements in the live tree
carry an AutomationId at all, and 3 of those are the OS window buttons — so **8 app-authored ids
across 625 elements (2%)**.

```
ModFilterBox   LoadoutAllSegment  LoadoutMpSegment  LoadoutSpSegment
UsageTip       ModListView        AppStatusText     VerticalScrollBar
```

Those 8 arrive because WinUI promotes `x:Name` to AutomationId for some controls. It is partial and
unreliable: the XAML has **720 `x:Name` attributes** and 8 of them surfaced.

Everything else is found by its **Name**, which is its display text:

```
Enable all   FOUND  Button  invoke=True   aid=''
+ Game       FOUND  Button  invoke=True   aid=''
Settings     FOUND  Button  invoke=True   aid=''
626 Labs     FOUND  Button  invoke=True   aid=''
Play         FOUND  Text    invoke=False  aid=''      <-- matched the label, not the button
```

That is a test suite keyed on UI copy, in a repo whose own conventions treat microcopy as
something to keep improving. Rename a button and a green suite goes red for no behavioural reason;
worse, a *moved* button keeps passing. The `Play` row shows the sharper failure — searching for the
obvious string found a `Text` element, so a naive harness would assert against a label and never
touch the control.

`626 Labs` is the interesting one both ways: the theme picker's Name **is** the current theme, so
reading it answers "which theme is active" for free — and there is no stable way to ask for "the
theme picker" without already knowing the answer.

## Correction to backlog E2

E2 said *"Groundwork exists and is better than expected: `AutomationProperties` are already set
across the XAML — 19 in `MainWindow.xaml`…"* and called it better than expected. **That was wrong,
and it was my error.** The grep counted `AutomationProperties` without separating the two kinds:

- `AutomationProperties.Name` — **92**, all of them. Accessibility labels for screen readers.
- `AutomationProperties.AutomationId` — **0**.

The accessibility work is real and it is why Name coverage reaches 92%. It was not done for
automation and it does not serve it. E2 is a bigger job than that entry implied, and the extra work
is an id pass, not harness plumbing.

## What already works today, verified

- `AppStatusText` reads back **"27 of 27 enabled"** — the status line is directly queryable, which is
  the surface the duplicate-add fix changed.
- Mod-row toggles expose `TogglePattern` with names like `Disable More Ring and Necklace Slots` — 29
  of them. Per-mod enabled state is both **readable and settable** through the real UI path.
- `IsOffscreen` answers the absence question E2 needs. "Is the discovery lane visible" has a real
  negative, not a lookup that finds nothing and shrugs.

## Cost shape

- **Reading and driving:** near zero. `UIAutomationClient` loads in stock PowerShell 7; no FlaUI, no
  WinAppDriver, no new dependency. The two probe scripts behind this document are ~60 lines each.
- **Making it stable:** the actual work. An `AutomationId` pass over the surfaces the smoke list
  touches, which is a XAML edit per control and a convention going forward.
- **Screenshot channel:** already solved. `scripts/capture-store-screenshots.ps1` does DPI-aware
  exact-size capture and encodes the WinUI 3 `PrintWindow` trap.

## Recommendation

Do the `AutomationId` pass **scoped to what the triaged smoke list actually touches** — not all 720
`x:Name`s. E3 names the targets; this pass gives them stable handles; then the harness is small.

Nothing here was kept. The probe scripts were scratch and are not in the repo.
