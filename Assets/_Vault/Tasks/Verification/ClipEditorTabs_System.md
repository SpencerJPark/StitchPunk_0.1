# Clip Editor tabs + floating viewport overlay

> **Status:** 🔨 **BUILT 2026-08-29** — all seven phases. The four remaining `← DECISION` markers below went the recommended way; they are stamped in [`verify-clipeditortabs.md`](verify-clipeditortabs.md), which also records two things the build found that this spec did not have: `VatBakePanel`'s second host (`VatBakeWindow`, which forced a bound mode rather than deleting its fields) and both ticks driving one shared preview controller in the same frame. Awaiting the owner's play-test.
> **Supersedes:** the cover-pane *switching model* only. `ShowVatBakeTab` / `ShowNewRigTab` / `Show2DDirectionSetsTab` keep their absolute-cover geometry and their "tear nothing down on hide" contract — this plan replaces the three independent `ToolbarToggle`s that drive them with one exclusive tab bar, and moves what is left of the top bar off it.
> **Also supersedes:** `VatBakePanel.OfferClipSet`/`OfferRig`'s "only fills an empty field" doctrine, and `DirectionSetsPanel.OfferRig`'s. The owner has decided both panes read the window's selection outright. Their doc comments defend the old rule and must be rewritten, not deleted silently.
> **Depends on:** `DirectionSetsPanel_System.md` (built 2026-08-29) — §5 here rewrites that panel's viewer.

---

**Skills Needed:**

None of the `dots-*` scaffolding skills apply — this is entirely toolkit editor UI, with no ECS
component, system, baker or blob in it. `dots-test` applies only if §5's context-entry change turns
out to need a game-side fixture; the layout contract itself is pinned by the toolkit's own
`ClipEditorLayoutTests`, which is not a Stitch Punk fixture.

---

## 1. Context — what the owner asked for

Four asks, stamped 2026-08-29:

1. **"The 2D Direction Sets tab should just reference the clip set and rig you already have selected
   in the main toolbar, so should VAT Bake."**
2. **"I don't want to have to toggle on and off pages to switch between them. Add a tab called Clip
   Editor and have it be the default starting tab that can be clicked back to."**
3. **"I want to make the top bar eventually just tabs you can switch between."**
4. **"Billboard and ragdoll only affect the Clip Editor, so move them into the scene view part in the
   top right as toggles there. Eventually I'd like to add gizmo options along with them like move,
   rotate and scale."**

**Stamped decisions (owner Q&A 2026-08-29 — do not reopen):**

- [x] **Rig *and* clip set both come from the window.** Both panes lose their own `Clip Set` and
  `Rig` `ObjectField`s. The Direction Sets queue therefore draws its clips from the open clip set,
  which is what lets it share the window's single `ClipPreviewController` and registry instead of
  building a second one (§5). The cost is accepted: a direction set spanning two clip sets is not
  authorable in this tool.
- [x] **The top bar is tabs and nothing else.** Everything currently on it that is not a tab moves
  into the floating overlay (§3).
- [x] **One floating overlay stack, two rows, pinned to the viewport's top-right**, both rows growing
  right-to-left: identity row on top, tool row beneath it. Validation findings stack below both.
- [x] **Move/Rotate/Scale ship in this plan.** `gizmoMode` already exists (`ClipEditorWindow.cs:179`)
  and is driven only by the W/E/R keys with no UI at all — the buttons surface state that is already
  there, they do not add gizmo behaviour.

## 2. The tab bar

### 2a. What exists

Three cover panes, each an absolutely positioned full-body element toggled by its own independent
`ToolbarToggle`:

| Pane element | Toggle | Show method |
|---|---|---|
| `vat-bake-pane` | `vat-bake-toggle` | `ShowVatBakeTab(bool)` |
| `new-rig-pane` | `new-rig-toggle` | `ShowNewRigTab(bool)` |
| `direction-sets-pane` | `direction-sets-toggle` | `Show2DDirectionSetsTab(bool)` |

They do not close each other. Whichever was ticked last is on top, and unticking it reveals whatever
is underneath — which is the behaviour the owner is asking to be rid of.

### 2b. What replaces it

`ClipEditorWindow.uxml` gains a `Toolbar name="clip-editor-tabs"` as the **first** child of
`clip-editor-root`, holding four `ToolbarToggle`s in this order:

```
tab-clip-editor  ·  tab-new-rig  ·  tab-direction-sets  ·  tab-vat-bake
   "Clip Editor"     "New Rig"      "2D Direction Sets"     "VAT Bake"
```

`ToolbarToggle` rather than `ToolbarButton` because the active tab must *look* held down, and
because `ClipEditorLayoutTests` can then assert the default state from the cloned tree without a
window on screen. They behave as a **radio group**, not as toggles:

- Exactly one is on. Clicking the active tab is a **no-op** — it does not turn off, because there is
  nothing behind it to reveal. Guard the re-entry (`if (activeTab == requested) { toggle.SetValueWithoutNotify(true); return; }`)
  or a click on the lit tab will write `false` and blank the window.
- `SetActiveTab(ClipEditorTab)` is the single writer. Every existing `Show*Tab(bool)` becomes
  private and is called only from it, in one place, hiding the two panes that are not wanted and
  showing the one that is. `ClipEditorTab.ClipEditor` shows none of them — the dock is what is
  underneath, so "the Clip Editor tab" is the absence of a cover, exactly as `false` on all three
  toggles is today.
- Every `SetValueWithoutNotify` on the other three, so switching tabs raises exactly one callback.

**`ClipEditorTab` is a new enum** (`ClipEditor`, `NewRig`, `DirectionSets`, `VatBake`) in the Editor
assembly. Four bools that must sum to one is the shape that goes wrong.

← DECISION at build time: reuse `.clip-editor__bar-action` for the tab run and add a
`--active` modifier, or give tabs their own class? (recommend: their own
`.clip-editor__tab` + `.clip-editor__tab--active`. `bar-action`'s doc comment is explicit that it
styles *runs of independent controls*, and a radio group is not that — it needs a held-down state
that the shared class deliberately does not define.)

### 2c. The New Rig flow's self-close

`NewRigPanel` raises `Closed` after a successful Create, and `CloseNewRigTab()` currently answers it
by writing `newRigToggle.value = false`, which reveals whatever was underneath. Under tabs there is
no "underneath": it must become `SetActiveTab(ClipEditorTab.ClipEditor)`. The existing doc comment on
`CloseNewRigTab` — "written through the toggle rather than straight at the pane" — survives the
change and still explains why; update it to name the tab bar as the single writer instead.

### 2d. Entry points and session state

Four static entry points exist, two of them driven by `ClipEditorStageOverlay`'s Scene-view buttons:

| Entry point | Called by | Becomes |
|---|---|---|
| `FocusClipEditing()` | stage overlay, `DirectionSetsPanel.OnOpenClipRequested` | `SetActiveTab(ClipEditor)` |
| `FocusVatBakeSettings()` | stage overlay | `SetActiveTab(VatBake)` |
| `FocusDirectionSetsTab(DirectionSetAsset)` | `DirectionSetAssetOpener` | `SetActiveTab(DirectionSets)` + load |
| `FocusWithVatBakeTab(bool)` | the two above | **deleted** — folded into a `FocusTab(ClipEditorTab)` helper |

Its remark ("the view is switched through the toolbar toggle, not by calling `ShowVatBakeTab`") is
the rule that keeps the lit tab honest, and it carries over verbatim to the tab bar. Keep the
fallback for a layout that failed to load.

**The active tab rides the two state channels the window already has:** a
`[SerializeField] private ClipEditorTab sessionTab` beside `sessionClipSet`/`sessionRig`
(`ClipEditorWindow.cs:794`), written by `RememberSessionState` and read by `RestoreView`; and a
`tab` field on `ClipEditorDocking.CarriedState` so a re-dock does not silently drop you back on the
Clip Editor tab. Both channels already carry the clip set, the rig, the playhead and rig-edit mode —
the tab belongs with them, and forgetting one of the two is how this comes back as "it loses my tab
after a recompile, but only sometimes".

## 3. The floating viewport overlay

### 3a. Shape

One absolutely positioned, frame-sized, **picking-ignore** column pinned inside `viewport-frame`,
right-aligned, holding three children top to bottom:

```
┌─ viewport-frame ───────────────────────────────────────────────┐
│                        ╭──────────────────────────────────────╮ │
│                        │ Clip Set [__] New  Rig [__] Prefab ⚠ │ │  identity row
│                        │        ▐M▌ R  S │ Billboard │ Ragdoll│ │  tool row
│                        ╰──────────────────────────────────────╯ │
│                        ╭──────────────────────────────────────╮ │
│                        │ validation findings (when opened)    │ │
│                        ╰──────────────────────────────────────╯ │
│                    (preview scene)                              │
└─────────────────────────────────────────────────────────────────┘
```

**`picking-mode: Ignore` on the container, not on the rows.** Picking mode is not inherited — the
existing `validation-overlay-slot` comment says so and relies on it — so the rows' own controls still
take clicks while an orbit drag started anywhere the rows are not still reaches the viewport beneath.
This is the proven pattern; do not invent a second one.

### 3b. `validation-overlay-slot` is absorbed, not left beside it

Today `validation-overlay-slot` is its own full-frame absolute layer with `overflow: hidden`, and
`.clip-editor__validation-overlay`'s `max-width: 60%` / `max-height: 60%` resolve **against that
frame-sized layer**. That is load-bearing: the percentages are what keep the findings panel a corner
of a small viewport as well as of a large one, and the `overflow: hidden` is what stops it escaping
over the inspector when the viewport is dragged to its minimum.

So the slot is **deleted as a layer and its duties move to the new overlay container**, which is also
frame-sized and also clips. `validationBadge.AttachMessagePanel(...)`
(`ClipEditorWindow.cs:2563`) is re-pointed at the overlay column, and the findings panel becomes its
third child. Two frame-sized layers both anchored top-right would simply overlap, and shrinking the
slot to hug its content would silently break the 60% clamp into 60%-of-nothing.

`ClipEditorLayoutTests.RequiredElementNames` lists `validation-overlay-slot`; rename the entry rather
than dropping it, and keep the comment explaining that a rename here presents as an error button that
appears to do nothing.

### 3c. Identity row

Moved off the toolbar verbatim — same elements, same names, same bindings, so `Q<T>(name)` and every
callback keep working:

`clip-set-field` · `new-clip-set-button` · `skinned-source-field` · `edit-prefab-button` ·
`validation-badge-slot`

Plus the two `clip-editor__toolbar-label`s that head the two field pairs. Nothing about
`OnClipSetChanged`, `OnSkinnedSourceChanged`, `RefreshPrefabActionState` or the badge's `Refresh`
calls changes — only where the widgets sit.

← DECISION at build time: the overlay is over the *preview*, which the Direction Sets tab has its own
version of and VAT Bake has none of. Does the identity row also appear on those tabs (a second
instance, or a shared element re-parented), or is it Clip-Editor-only and the other tabs simply
inherit a selection they cannot see? (recommend: **Clip-Editor-only in v1.** A re-parented element is
one element in two places with one set of bindings and is the cheap-looking option that produces the
"my field went blank" bugs; two instances is two sources of truth for one selection. Both panes now
*derive* from the window rather than holding their own, so each should show what it resolved to as
read-only text in its own header — "Zombie_Locomotion on ZombieRig" — and send the author to the Clip
Editor tab to change it. Revisit if that round trip annoys in practice.)

### 3d. Tool row

`gizmo-move-toggle` · `gizmo-rotate-toggle` · `gizmo-scale-toggle` │ `billboard-preview-toggle` │
`ragdoll-preview-toggle`

- **Billboard and Ragdoll move verbatim** from the toolbar. Their element names, tooltips and
  callbacks are unchanged; `ragdollPreviewToggle` stays a field because two paths still push it back
  off with `SetValueWithoutNotify(false)` — a refused enable (`ClipEditorWindow.cs:1822`) and the
  rig/set change at `:5018`.
- **The gizmo group is a radio over the existing `gizmoMode` field**, the same shape as the tab bar:
  exactly one on, clicking the active one is a no-op. It must be **bidirectional** — the W/E/R
  handler at `ClipEditorWindow.cs:2918-2931` already writes `gizmoMode`, so it must now also
  `SetValueWithoutNotify` the three toggles, or pressing W leaves the lit button describing the wrong
  mode. One `SetGizmoMode(GizmoMode)` writer, called by both the keys and the buttons, is the shape
  that cannot drift. **No gizmo behaviour changes** — `SetGizmo`, `PickGizmoHandle` and the drag
  handling are untouched.

← DECISION at build time: does the tool row show on the 2D Direction Sets tab, which now shares the
preview (§5)? (recommend: **no** — the owner's directive is that these are Clip Editor controls.
Instead the Direction Sets pane forces `BillboardPreviewEnabled = true` on entering its tab and
restores the toggle's value on leaving, and force-disables the ragdoll on entry: a ragdolling rig
cannot preview a facing, and a non-billboarded one is not what the game shows. Both are documented
stamped decisions of `DirectionSetsPanel_System.md` §3e and this is how they survive the shared
controller.)

## 4. The panes bind to the window's selection

### 4a. VAT Bake

Delete `clipSetField` and `rigField` from `VatBakePanel`, and `OfferClipSet`/`OfferRig` with them.
The panel takes both from the window instead — `SetSource(ClipSetAsset, RigAsset)` called from
`SetActiveTab` and from `OnClipSetChanged`/`OnSkinnedSourceChanged`, so it cannot go stale while
sitting open.

`skinnedRendererField` **stays**. It is a scene `SkinnedMeshRenderer`, not a `RigAsset` — a different
question the toolbar does not answer.

The old fields' doc comments defend a deliberate cross-rig bake ("switch to the tab, correct it,
switch away to check something, and the correction is gone"). That capability is genuinely lost, and
the replacement comment should say so plainly rather than pretend the question never existed: baking
something other than what the window is showing now means switching the window to it first.

### 4b. 2D Direction Sets

Delete `rigField` and `OfferRig`. Rig comes from the window. The clip set is new input — see §5.

`DirectionSetContextEntry` gains **`ClipSetAsset previewClipSet`**: picking "Zombie · Moving" has to
be able to set the clip set as well as the rig, or the entry loads a rig whose clips the queue cannot
offer. `UnitDirectionSetContextProvider` resolves it from the same `ActorAuthoring` it already reads
the rig from (`actor.clipSets`, first non-null — ← DECISION: first, or all of them merged? recommend
first, and warn when an actor lists more than one, since the pane can only show one).

Applying a context entry then writes the **window's** clip set and rig fields rather than the pane's,
so one path sets the selection whether it came from a unit pick or from the identity row.

## 5. The Direction Sets viewer folds into the window's preview

This is the largest consequence of §1's first stamped decision, and the payoff for it.

### 5a. What goes away

`DirectionSetsPanel` currently owns a **second** `ClipPreviewController` — a second
`PreviewRenderUtility`, a second `Persistent`-allocator registry blob, a second instantiated rig —
plus a synthetic `ClipSetAsset` it keeps in step. With the queue's clips guaranteed to be members of
the open clip set, all of that is the window's registry by construction. Deleted:

`previewController` · `syntheticClipSet` · `registryClips` · `RebuildRegistryIfClipsChanged` ·
`SameClips` · `RefreshClipWarnings`'s `ClipValidation.ValidateBind` probe and its temporary probe set ·
`CollectQueuedClips`'s registry role · `IDisposable` and the window's `directionSetsPanel.Dispose()`
call in `OnDisable`.

The panel keeps its own `Image` and renders the shared controller into it at its own size. The two
tabs are mutually exclusive, so only one of them ticks and renders per frame — which is exactly why
sharing is safe here and would not be if both panes could be visible at once.

### 5b. What replaces the per-row warning

The rig-mismatch warning was answering "can this clip bind to the preview rig". The window's own
`ValidationBadgeElement` already answers that for the whole set, so the per-row warning narrows to the
one thing the row can now be wrong about: **the clip is not in the open clip set**. Detect it the
honest way — `previewController.SamplePose(clipId, t)` returns false for a clip the registry does not
hold — and mark that row, leaving the others previewing. The whole viewport still never goes dead
over one bad row.

### 5c. The camera trap

`FrameRig()` sets `orbitFocus` and `orbitDistance` and **does not touch `orbitYaw`/`orbitPitch`**
(`ClipPreviewController.cs:1415`). With a shared controller the Clip Editor's orbit angle therefore
follows you into the direction viewer, which breaks that panel's stamped "fixed front-on camera"
decision in a way that looks like the mirror flag being wrong rather than like a camera bug.

`ClipPreviewController` gains **`OrbitYaw` / `OrbitPitch` as get/set properties**. The pane captures
both on entering its tab, writes `0, 0`, and restores them on leaving — the same save-and-restore
shape §3d's DECISION uses for the billboard toggle. Adding a bare `ResetOrbitToFront()` is not enough:
without the getter the Clip Editor silently loses the angle the author had set up.

### 5d. Pose bleed is self-healing, and should be left alone

Switching tabs leaves the shared mirror posed wherever the other tab last sampled it. On returning to
the Clip Editor, `UpdatePreview` re-samples `selectedClip` at `playheadTime` on the very next tick, so
it corrects itself within a frame. Do not add a restore pass for this — it would be a second writer
of the pose, and the failure it prevents lasts one frame and is invisible.

## 6. File manifest

**New (toolkit):** `Editor/ClipEditor/ClipEditorTab.cs` (the enum)
**Edited (toolkit):**
`ClipEditorWindow.uxml` — tab `Toolbar` added as the first child; the five identity elements and the
two preview toggles move out of `clip-editor-toolbar` into the new overlay inside `viewport-frame`;
`validation-overlay-slot` renamed/absorbed; the old three pane toggles deleted ·
`ClipEditorWindow.uss` — `.clip-editor__tab` + `--active`, the overlay container and its two rows,
`validation-overlay-slot`'s rules moved onto the container ·
`ClipEditorWindow.cs` — `SetActiveTab`, `sessionTab`, the three `Show*Tab` methods made private,
`FocusWithVatBakeTab` folded into `FocusTab`, `CloseNewRigTab` re-pointed, `SetGizmoMode` writer,
W/E/R handler syncs the toggles, `AttachMessagePanel` re-pointed ·
`ClipEditorDocking.cs` — `CarriedState.tab` ·
`VatBakePanel.cs` — fields deleted, `SetSource` added ·
`DirectionSets/DirectionSetsPanel.cs` — second preview controller deleted, shared one adopted,
orbit save/restore, row warning narrowed ·
`DirectionSets/DirectionSetContext.cs` — `previewClipSet` ·
`Preview/ClipPreviewController.cs` — `OrbitYaw`/`OrbitPitch` properties ·
`Tests/EditMode/ClipEditorLayoutTests.cs` ·
`Docs/AnimationToolkit/HANDOFF.md`
**Edited (game):** `Editor/DirectionSetContext/UnitDirectionSetContextProvider.cs` (fills `previewClipSet`)
**Docs:** `_Vault/Memories/Code/Editor.md`; retire per §8.

## 7. Build phases

1. **Tab bar.** `ClipEditorTab`, the tab `Toolbar`, `SetActiveTab`, the three toggles deleted, the
   four entry points re-pointed, `CloseNewRigTab` re-pointed, session + `CarriedState` carry the tab.
   Layout fixture updated. **Nothing moves off the toolbar yet** — this phase is switching only, and
   is separately reviewable because of it.
2. **The overlay shell.** The container inside `viewport-frame`, `validation-overlay-slot` absorbed,
   findings re-pointed. Prove the findings panel still clips and still sizes to 60% of the frame at a
   viewport dragged to its minimum — that is the regression this phase can cause.
3. **Identity row moves in.** The five elements plus their two labels. No binding changes.
4. **Tool row.** Billboard and Ragdoll move in; the gizmo group is added and made bidirectional with
   W/E/R. The top bar is now tabs only — ask 1's end state is reached here.
5. **Panes bind to the window.** VAT Bake's two fields deleted + `SetSource`; the Direction Sets
   pane's rig field deleted; `previewClipSet` through the context seam and the game provider.
6. **Shared preview.** §5 in full — the second controller deleted, orbit save/restore, row warning
   narrowed. Last, because it is the one phase that can make the direction viewer worse, and it wants
   the other five already stable underneath it.
7. **Docs + retire.** `HANDOFF.md`, `Editor.md`, `PackagingConformanceTests` green, retire per §8.

## 8. Verification (→ `verify-clipeditortabs.md` at retire time)

- The top bar is four tabs and nothing else. **Clip Editor** is lit on a freshly opened window.
- Clicking each tab shows exactly that pane; clicking the lit tab does nothing (it must not blank the
  window). No two panes are ever visible at once.
- Leaving a tab and coming back restores it untouched — VAT Bake's settings, New Rig's ticked nodes,
  the direction queue and the dock's three split positions.
- Creating a rig from the New Rig tab drops you on the Clip Editor tab, not on whatever was
  underneath.
- Double-clicking a `DirectionSetAsset` opens the window on the 2D Direction Sets tab with that set
  loaded. The Scene-view overlay's two buttons still land on the right tabs from a prefab stage.
- The active tab survives a script recompile **and** a re-dock (drag the window out and back).
- Identity row floats over the preview, right-aligned; an orbit drag started on empty viewport still
  orbits, and a drag started on the row does not.
- Validation findings open **below** both overlay rows, still clipped to the 3D area, still a corner
  of the viewport when it is dragged to its minimum width.
- Billboard and Ragdoll behave exactly as they did on the toolbar, including the ragdoll toggle
  springing back when an enable is refused.
- Pressing W/E/R lights the matching gizmo button; clicking a gizmo button does what its key does.
  Gizmo dragging is unchanged.
- VAT Bake shows the window's clip set and rig, follows a change made on the Clip Editor tab while it
  is open, and bakes what the window is showing.
- 2D Direction Sets: the queue offers the open clip set's clips; sweeping the direction slider still
  turns through the coverage **with no hitch** (the shared registry is not rebuilding); a clip that is
  not in the open set marks its own row and the others keep previewing.
- Switch from Clip Editor with the camera orbited to 2D Direction Sets: the direction viewer is
  front-on. Switch back: the orbit is exactly where it was left.
- Unit Context still loads set + rig + turn granularity in one click, and now sets the clip set too.
- Toolkit `PackagingConformanceTests` and `ClipEditorLayoutTests` green; EditMode and PlayMode totals
  do not drop.

## Open decisions (collected)

- [x] Panes read the window's clip set **and** rig; Direction Sets shares the window's preview — stamped 2026-08-29.
- [x] Top bar becomes tabs only — stamped 2026-08-29.
- [x] One floating overlay stack, two rows, viewport top-right, right-aligned — stamped 2026-08-29.
- [x] Move/Rotate/Scale buttons ship in this plan — stamped 2026-08-29.
- [ ] Tab styling: own `.clip-editor__tab` class, or `bar-action` + a modifier? (recommend own class)
- [ ] Does the identity row appear on the New Rig / Direction Sets / VAT Bake tabs, or is it Clip-Editor-only with the other panes showing a read-only resolved line? (recommend Clip-Editor-only in v1)
- [ ] Does the tool row appear on the 2D Direction Sets tab? (recommend no; the pane forces billboard on and ragdoll off while active, restoring on leave)
- [ ] `previewClipSet` from an actor with several clip sets: first, or merged? (recommend first + a warning)
