# Archive

Documents kept for the record, moved out of the paths a tool now claims.

## `2026-05-23-phase1-spec.md` and `2026-05-23-phase1-checklist.md`

The original .NET 10 / WinUI 3 rewrite scope and its Phase 1 build checklist — the port of
the Electron app's pure cores to C#, completed 2026-05-23 at 157 green tests. They lived at
`docs/spec.md` and `docs/checklist.md`.

They were moved because **Vibe Cartographer hardcodes those two paths** (plus `docs/prd.md`)
and its `build` step executes *the first unchecked box in `docs/checklist.md`*. That file's
header says COMPLETE, but 20 of its boxes are still unchecked — the first being "Scaffold the
solution." Running the tool against the repo would have tried to re-scaffold a solution that
now carries 1,800+ tests.

Nothing here is stale in the sense of wrong; it is the record of how the rewrite was planned
and executed. It is simply not the current plan, and it should not be mistaken for one by a
tool that reads by path.

**Current planning documents live in `docs/superpowers/specs/` and `docs/superpowers/plans/`,**
in dated `YYYY-MM-DD-<topic>` form. That is where a new spec belongs.
