# Amendment A67 — Cutscene Editor Polish II: Viewport Picking, In-Viewport Gizmo, Frozen Headers

> **Status:** ✅ T1–T6 built and gated 2026-09-06; stopped at the ⏸ owner checkpoint. Written 2026-09-04. Closes the A59/A60 backlog (A60 §4 items 2–6).
> **Roadmap:** `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` — read its §4 protocol first.
> **Depends on:** A66 (selection set, Auto Key). **Parallel-safe with:** nothing package-side.
> **Session budget:** one Sonnet session. Editor assembly only; no fixtures (UI wiring), every task proved live and by the owner's eyes.

## 1. Why

A60 §4 recorded the remaining backlog after the owner's third correction on the same axis ("it needs the scene there to actually work"): click-select in the tab's viewport, an in-viewport gizmo, cast/inspector compaction, the frozen header column, and navigation parity with the Clip Editor viewport. Without these the in-tab viewport is a monitor, not a workspace — you still reach for the Scene view to touch anything.

## 2. Read first

- `Editor/ClipEditor/Cutscene/CutsceneViewportElement.cs` (utility camera + RT, orbit/free navigation, `RenderShot`/`RenderFree`, `AdoptRenderedPoseAsFreeRig`, `NavigationBrokeShot`).
- `CutsceneEditorPanel.cs`: `BuildViewportArea`, `RenderViewport`, `RefreshViewportOverlay`, `FrameViewportOnCast`, `OnUnitySelectionChanged`, `SetSelectedGameObject`, `SyncSceneSelectionToTimelineSelection`, `RebuildTimeline`, `CreateRow`, `BuildSlotRows`; `CutsceneCastPanel.cs`.
- Clip Editor precedents: `Preview/PreviewScenePicker.cs` (`BuildRay`, `CollectHits` — bounds-based picking with no colliders), `Preview/PreviewTransformGizmo.cs` + `Preview/PreviewGizmoMath.cs` + `Editing/GizmoDragRouting.cs` (a GameObject-built gizmo rendered by the preview camera and dragged from pointer events), `ClipEditorWindow.CameraNavigation.cs` (right-drag look, WASD fly, F frame), `ClipEditorWindow.ComponentStack.cs` for the W/E/R `SetGizmoMode` single-writer rule.
- `Amendment_A59_EmbeddedSceneViewport_Spec.md` §5 (T2/T3 intent) and `Amendment_A60_CutsceneUiOverhaul_Spec.md` §1 (the capture-before-and-after method — use it for every visual task here).

## 3. Design

### 3.1 Click-select in the viewport (A59-T2)

Pointer-down without drag on the viewport image: convert to RT pixel coordinates (image local → `renderTarget` size), `utilityCamera.ScreenPointToRay`, then bounds-based picking over the **bound cast only** (each slot's bound GameObject and its renderers' `bounds`; the Clip Editor's `PreviewScenePicker.CollectHits` shape, adapted to scene objects). Nearest hit → `SelectSlotHeader(slotIndex)` + `Selection.activeGameObject` (existing two-way sync). Click on nothing → clear selection. Ctrl-click toggles a slot into A66's set. A short hover highlight (outline via the overlay label, not a shader) is optional; skip if it costs more than an hour.

### 3.2 In-viewport gizmo + Key (A59-T3)

Reuse `PreviewTransformGizmo`: build its gizmo object in the **open scene** with `HideFlags.HideAndDontSave`, on the `CutsceneViewport` layer? — no: the utility camera renders every layer; instead place it and render it as today, but exclude it from picking (§3.1) and from the Scene view via `SceneVisibilityManager`/`HideFlags`. Modes W/E/R through one `SetGizmoMode` writer, matching the Clip Editor. Pointer drags on the gizmo route through `GizmoDragRouting`/`PreviewGizmoMath` against the utility camera and write the bound object's (or bound part's) transform directly — the same transform the Scene-view gizmo writes, so **Key** and A66's **Auto Key** work unchanged (Auto Key's `hotControl` test needs an in-tab equivalent: set a panel-level `isViewportGizmoDragging` flag the detector also honours). Hide the gizmo when nothing selected, when the transport plays, and on `OnHidden`.

### 3.3 Frozen header column (the G2 cut)

Split the timeline into two `ScrollView`s in one row: a fixed-width **headers** column (vertical scroll only, no scrollbar drawn) and the **lanes** column (both axes). Mirror vertical offsets both ways through `verticalScroller.valueChanged` with a re-entrancy guard; the ruler stays in the lanes column's sticky top row. Row heights must be computed once and shared (`CreateRow` returns the height; both columns use it) or the two drift after a wrap.

### 3.4 Cast compaction + inspector styling (A60 §4 item 4)

Cast rows become one line: state dot · name · kind chip · four icon buttons (Place/Bind/Select/Frame) with tooltips; the Sync-to-Stage control from A61 stays in the header. Inspector: headings, spacing and field widths follow the Clip Editor's inspector USS (`clip-editor__inspector-*` tokens) so both tabs read as one tool.

### 3.5 Navigation parity + zoom-to-playhead (A60 §4 item 6)

Free camera: right-drag look, WASD/QE fly with Shift boost, scroll dolly, **F** frames the selection (already) and **Shift+F** the whole cast; Alt-drag orbit stays. Timeline: Ctrl+wheel zooms around the cursor time; **Home** frames the whole cutscene; **Alt+P** centres the playhead. All shortcuts registered on the panel root with the same focus rules as A66's clipboard keys.

## 4. Decisions

- **A67-D1** Picking is bounds-based over the bound cast, never `Physics.Raycast` — parts have no colliders and the cast is small.
- **A67-D2** The in-tab gizmo writes the same transforms the Scene-view gizmo does; there is one keying path.
- **A67-D3** Two synced scroll views for the header column, not a custom layout element. Simple, and the Clip Editor does not need it (its headers already freeze differently) — do not try to share.

## 5. Tasks

- [x] **T1 — Click-select (§3.1).** Live proof: two bound objects, simulate a pointer-down at the projected pixel of each (project via `utilityCamera.WorldToScreenPoint`), assert the matching slot header lights.
- [x] **T2 — In-viewport gizmo + Key (§3.2).** Live proof: select a slot, gizmo appears at the object; drive a drag through the routing math by reflection, assert the transform moved, press Key, assert the key.
- [x] **T3 — Frozen header column (§3.3).** **[parallel-safe with T4]** Live proof: scroll the lanes horizontally, headers stay; scroll vertically, both move; capture before/after per A60 §1.
- [x] **T4 — Cast compaction + inspector styling (§3.4).** **[parallel-safe with T3]** Capture before/after.
- [x] **T5 — Navigation parity + zoom-to-playhead (§3.5).**
- [x] **T6 — Docs.** `cutscenes.md` viewport/navigation subsection; remove the "header column scrolls" Known-gaps line. CHANGELOG, HANDOFF §4 (the A59/A60 backlog closes here — say so).
- [ ] **⏸ Owner checkpoint.** Without opening the Scene view: place two actors from the cast panel, click one in the viewport, move it with W, press Key, scrub, box-select in the timeline while the headers stay put.

## 6. Risks and traps

- `GUIView.GrabPixels` captures (A60 §1) lie under occlusion — capture with the window unobstructed, before and after.
- A gizmo object left in the scene after a domain reload is a leaked scene object; destroy on `OnHidden`, `DetachFromPanelEvent`, and `AssemblyReloadEvents.beforeAssemblyReload`. The viewport element already has `DestroyLeakedCameras`; mirror it.
- Two scroll views syncing each other re-enter; guard with a bool, not with unsubscribing.

## 7. Build log

Built 2026-09-06 immediately after A66, in the same session, T1–T6, each task committed alone.
Suites at the end, unchanged from A66's baseline: toolkit EditMode **724 discovered / 723 passed**
(the standing `Conformance_A` asmdef drift, untouched), toolkit PlayMode **261/261**,
`StitchPunk.Tests` **59/59**, `StitchPunk.Tests.PlayMode` **7/7**. No fixtures were added — the
spec's own budget says this is UI wiring.

### The one design the spec got wrong, and how it was found

§3.2 sketched the in-tab gizmo as a `HideFlags.HideAndDontSave` GameObject in the open scene,
hidden from the Scene view through `SceneVisibilityManager`. **That does not work, measured before
any of it was written:** `SceneVisibilityManager.instance.Hide` on a `HideAndDontSave` object does
not take at all — `IsHidden` stays `false`, because the object belongs to no scene — and a plain
`Camera.Render` draws it regardless. The gizmo would have appeared in the owner's Scene view stacked
on Unity's own.

The replacement was probed the same way before being adopted: `Graphics.DrawMesh` naming the tab's
utility camera renders correctly through URP's `SubmitRenderRequest` (84 pixels of a test mesh, in a
64×64 readback). So the gizmo is drawn **for that camera alone** and is not a scene object. That
settles three of the spec's own worries at once: nothing to exclude from §3.1's picking, nothing for
§6's leak trap to catch, and nothing in the Scene view. `PreviewTransformGizmo` still builds the
mesh — reused whole, its object simply kept inactive and renamed off `ClipPreviewGizmo`, because the
Clip Editor keeps a live object under that name and a name-based sweep could not tell them apart.

### Drift from the spec, and what was done instead

1. **§3.1's "Ctrl-click toggles a slot into A66's set" has nothing to toggle into.** A66's set holds
   *items*; a slot header is not one, and nothing consumes a multi-slot selection. Implemented as the
   useful reading: a modifier-click moves the slot and **keeps** the timeline's items selected.
2. **§3.1 says to convert to RT pixels and call `utilityCamera.ScreenPointToRay`.** The utility
   camera is disabled and its transform and projection mean nothing between renders, so the ray is
   built from the pose last *rendered*, using the same construction `PreviewScenePicker.BuildRay`
   uses, inlined so it needs no `Transform` to read from. The spec's own sketch of a hidden proxy
   object would have been another instance of §6's leak hazard.
3. **`GizmoDragRouting` is not used.** It routes between sockets, ragdoll bodies and rig-edit mode,
   none of which a cutscene has; the cutscene's routing is that the gizmo writes the transform and
   Key/Auto Key do the rest. `PreviewGizmoMath` *is* reused whole.
4. **The fly keys had to be gated behind the right mouse button.** §3.5 asks for WASD/QE fly and
   §3.2 asks for W/E/R gizmo modes; both land on the same keys. Unity's own answer is that fly only
   runs while the right button is held, and that is what was implemented — without it the fly keys
   swallow W/E/R and the gizmo modes are unreachable.
5. **T6's "remove the header column Known-gaps line"** was one line and is gone. The mark-label gap
   above it stays: it is about `Handles.Label`, which `Conformance_E` still bans.
6. **`AddHeaderOnlyRow` wraps its element.** A bare element's margins are laid out outside its
   height, so the Add Part Track button pushed the header column past the lane column, 4px per slot.

### What was machine-verified, and what was not

Machine-verified live through `execute_code` against a real open Cutscene Editor tab, numbers in the
commit messages: viewport picking at each actor's projected pixel and on empty sky, both modifier
branches; the gizmo building, claiming a handle press, moving the actor along one axis, declining an
empty-space press, and feeding both Key and Auto Key; header/lane row alignment at 0.0000px across
33 rows with vertical scroll mirroring and horizontal scroll not; the cast panel's row and button
counts, icon resolution, enable rules and hidden bind fields; the inspector's resolved heading and
padding matching the Clip Editor's; and the timeline's Home, Alt+P and Ctrl+wheel arithmetic.

**Not machine-verifiable from a background Editor, and owed to the owner's eyes and hands:**

- **Every screen capture the spec asks for.** §6 says captures lie under occlusion and need an
  unobstructed window; a background session has neither focus nor an unobstructed window. No
  before/after was taken for T3 or T4, and none is claimed.
- **Real pointer gestures.** Everything above was driven by calling the methods a pointer calls. The
  click-versus-drag tolerance, the right-drag look, the fly keys and whether a handle is grabbable
  where it *looks* grabbable all need a real pointer.
- **Anything inside `SceneView.duringSceneGui`** — an unfocused Editor never repaints its Scene view,
  so such a handler is never called. Nothing in A67 depends on one, but the mark overlay from A64
  still does.
