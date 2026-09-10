# Handoff — what to build next (2026-09-09, after A80)

> Written at the close of A80 (0.27.0) from a pass over the shipped tree, HANDOFF §5–§9, the
> vault's `AnimationToolkit.md`, and the August systems gap audit. Five ideas, ranked; each says
> what it is, why now, and how big. Nothing here is started.

## 0. The audit that produced the list

Facts from the tree on 2026-09-09 (head `a7b149d7`):

| Measure | Value |
|---|---|
| Package `.cs` files (excluding `Samples~`) | 390 |
| Test fixtures | 124 (EditMode 820 tests, PlayMode 283) |
| `TODO` / `FIXME` / `HACK` markers | 0 |
| Largest file | `ClipEditorWindow.cs` 9,319 lines (+1,531 in its `ComponentStack` partial) |
| Second largest | `CutsceneEditorPanel.cs` 5,175 lines |
| Standing red gate | `Conformance_A`: the Editor asmdef references `Unity.RenderPipelines.Universal.Runtime`, the architecture doc §1.3 does not list it |

What the A80 build itself surfaced:

- **Three near-identical catalog columns now exist** — `ClipSetsPanel`'s inline catalog,
  `RigCatalogColumn`, `ActorProfileCatalogColumn` — each carrying the same five live-verified
  layout constants and comments. A76-D2 and A80-D8 chose to mirror rather than extract until the
  owner had seen all three. He is about to.
- **Every cover-pane split view settles at its floor after a tab hide/show.** A75, A76 and A80 each
  re-recorded it. The Profiles column opens at 260px and comes back at 200px. The `minWidth`
  floors keep the tabs alive; nothing restores the dragged width.
- **The window file dictated the wave structure.** Fifteen tasks ran in parallel; the only two
  that could not were the two that edited `ClipEditorWindow.cs`. Every window task in every
  amendment since A56 has been briefed with a list of line ranges because the file cannot be read.
- **A panel-less UI Toolkit element never dispatches `ChangeEvent`**, so no fixture can prove a
  field write reaches the shared selection. The drive proved it once, by hand, against a live window.
- Two known-gap items from HANDOFF §7 are still open and small: `CountTracksForTarget`
  undercounts tag-bound tracks in delete confirmations, and `ActorProfileBuilder` skips P2 at bake.

## 1. One catalog column, one cover-pane split, remembered dividers — `M`

**What.** Extract `ToolkitCatalogColumn<TAsset>` (search, New, Refresh, boxed two-line rows,
context-menu Rename/Delete, `userData` recycling) and re-home the three catalogs on it. Beside it,
a `CoverPaneSplitView` that re-applies its fixed-pane dimension on every show and remembers it in
`EditorPrefs`, replacing the eight raw `TwoPaneSplitView`s in the four cover panes.

**Why now.** The owner will judge all three catalogs at the A80 checkpoint; whatever he asks for
would otherwise be applied three times. The divider question has been open since A76 T8 and every
amendment since has re-discovered the settle-to-floor trap. This is the one visible defect on every
tab and the one duplication that is growing.

**Size.** Two new files, four panels re-pointed, `ClipEditorLayoutTests` + one
`CoverPaneSplitViewTests` (dimension survives a hide/show cycle — revert-to-fail is the re-apply).
Four or five parallel workers, one wave.

## 2. Decompose `ClipEditorWindow.cs` into pane elements — `L`

**What.** The window keeps the tabs, the selection, session state and the transport target; the
four dock panes become elements the way `ActorEditorPanel` already hosts `ActorEditorLayersColumn`
and `ActorEditorProfilesColumn`: `ClipListPane`, `RigHierarchyPane`, `ClipInspectorPane`,
`TimelinePane`, each bound to the selection and the preview controller. No behaviour change.

**Why now.** 9,319 + 1,531 lines is past what any session or subagent can read; A80's two waves
existed only because of this file. The three most recent amendments each spent a task on
"grep the member, read forty lines" briefs. Decomposition is also what makes idea 1's split-view
change and any future pane work parallel-safe.

**Size.** Four sequential extractions (each one pane, one worker, one gate) behind one
characterisation pass: capture the window on every tab before and after, `ClipEditorLayoutTests`
unchanged throughout. Expect two sessions; do it pane by pane, never as one move.

## 3. A79 — the VAT preview shows the whole animation — `M`, already specced

**What.** `Docs/AnimationToolkit/Amendment_A79_VatPreviewModes_Spec.md`: the VAT Bake preview
plays every part of the actor (cutout parts and baked VAT parts together) instead of only the
baked half, with a source/baked toggle.

**Why now.** It is the next queued feature, it is the other half of the owner's A78 request, and
A80 just made its inputs (clip set and rig) shared and live — so the preview now follows whatever
the Rigs and Clip Sets tabs pick, which the spec's T0 grounding pass should re-check. Blocked only
on the A78 owner checkpoint; ask for that first.

**Size.** As specced: a T0 grounding pass, then the preview element and a fixture or two.

## 4. Release readiness: clean-project import, player build, `Samples~` compile, asmdef truth — `M`

**What.** The three items `package.json` itself lists as "remaining before 1.0", plus turning the
standing `Conformance_A` failure green by deciding whether the Editor assembly legitimately depends
on URP (it does — the VAT preview material and the cutscene viewport) and updating architecture
§1.3, or removing the dependency. Add a nightly-style EditMode run over a temporary assembly that
compiles `Samples~` (the vault already records that it rots silently).

**Why now.** Every session reports "only the standing `Conformance_A` drift" — a red gate that is
always red trains everyone to ignore the gate. And the package is sellable only if it imports into
a project that is not this one.

**Size.** One session, mostly orchestrator work in the Editor: an empty-project import, a Windows
player build, one temp asmdef, one doc edit.

## 5. The second toolkit actor in the game (PlayerUnit) — `L`, game side

**What.** `MaleCitizen` is the only actor on the toolkit path; `PlayerUnit` and `BaseUnit` still
use a separate copy of the body-part tree (see the vault's "First Toolkit Actor"). Author the
player as a toolkit actor — a rig, a clip set, a profile with the same `AnimNames.*` vocabulary —
and run it through G5's name-binding path. Fold in the two small §7 fixes while in the area:
`CountTracksForTarget` matching by tag, and P2 reported at bake.

**Why now.** One actor proves a pipeline works; two prove the profile / play-by-name design
generalises, and the player is the actor whose animation the owner looks at most. It is also the
next step of the August audit's #1 item (the legacy `AnimationSystemGroup` cannot be deleted while
anything still runs on it).

**Size.** One content session (the re-runnable recipe scripts under
`Assets/_Scripts/Editor/ContentAuthoring/` are the template) plus a play-test the owner must watch.

## Not on this list, on purpose

- Sound while scrubbing — HANDOFF §5 says do not start it.
- The bone-reparent guard and the Spatial3D twist axis — HANDOFF §6, the owner's calls.
- Ragdoll limits and launch feel — three owner checkpoints are already open; nothing to build
  until he has looked.
