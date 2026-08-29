---
title: Verify — Clip Editor tabs + viewport overlay
status: active
created: 2026-08-29
area: code
---

## Goal

Confirm the Clip Editor's top bar is four exclusive tabs and nothing else, that the identity
controls and the viewport tools now float over the preview, and that VAT Bake and 2D Direction Sets
read the window's clip set and rig rather than holding their own. Spec:
[`ClipEditorTabs_System.md`](ClipEditorTabs_System.md). Built and gated this session — EditMode
765/765, PlayMode 241/241, console clean. Everything below marked `[ ]` needs the Editor on screen.

## How the four open DECISIONs were stamped

All four went the way the spec recommended:

- **Tab styling:** own `.clip-editor__tab` + `--active`, not `bar-action` + a modifier. `bar-action`'s
  own comment says it styles runs of *independent* controls; a radio group needs a held-down state
  that rule deliberately has no business defining.
- **Identity row placement:** Clip-Editor-only. The whole overlay hides on the other three tabs (the
  cover panes are drawn over the body, so the rows would be underneath them anyway). VAT Bake and
  2D Direction Sets each show what they resolved to instead — see below.
- **Tool row on the Direction Sets tab:** no. The pane forces `BillboardPreviewEnabled = true` and
  `DisableRagdollPreview()` while its tab is active and restores both on the way out.
- **`previewClipSet` from an actor with several clip sets:** the first, with a console warning
  naming the count and the one chosen.

## Two things found in the build the spec did not have

1. **`VatBakePanel` has a second host.** `VatBakeWindow` (`Window ▸ DOTS Animation Toolkit ▸ VAT
   Bake`) hosts the same element, and deleting its Clip Set and Rig fields outright — which is what
   the spec said — would have left that window with no way to say what to bake. Instead the panel
   gained a **bound mode**: `SetSource(clipSet, rig)` writes both fields, disables them, and shows a
   line saying where to change them. The standalone window never calls it and keeps both pickers
   live. The fields are disabled rather than removed so the assets can still be clicked through to.
2. **Both ticks ran on both tabs.** `OnEditorTick` called `UpdatePreview` unconditionally, so on the
   2D Direction Sets tab the window and the panel would both have sampled and rendered the *same*
   controller in one frame, each showing whatever the other posed last. One
   `PreviewRenderUtility` cannot serve two viewports, and which won would have depended on tick
   order — so it would have read as the direction viewer flickering rather than as two writers.
   `UpdatePreview` is now gated on `activeTab == ClipEditor`. Also: the panel's own
   `EditorApplication.update` subscription is now untangled **before** the controller is disposed in
   `OnDisable`, or it would call `Render` on a disposed controller every tick from a closed window.

## Steps

### Compile + tests (done this session)

- [x] Full recompile — console free of `error CS####`. `ClipEditorTab` and
      `UnitDirectionSetContextProvider` confirmed loaded in the live domain via `unity_reflect`, so
      both the package and the game assemblies built.
- [x] EditMode 765/765 (was 764; net +1 from two new layout cases replacing one). Includes the two
      new `ClipEditorLayoutTests` — `Tabs_CloneAsToolbarToggles_WithOnlyClipEditorLit` and
      `GizmoModeToggles_CloneWithMoveLit` — plus `PackagingConformanceTests` and
      `SystemPlacementConformanceTests`.
- [x] PlayMode 241/241. Counts did not drop.

### Tabs (owner, needs the Editor)

- [ ] Open the Clip Editor: the top bar is four tabs and nothing else, with **Clip Editor** lit.
- [ ] Each tab shows exactly its own pane. No two panes visible at once.
- [ ] **Click the lit tab.** It must stay lit and nothing must change — the failure to look for is
      the window going blank, which is what a plain toggle would do.
- [ ] Leave a tab and come back: VAT Bake's settings, New Rig's ticked nodes, the direction queue
      and the dock's three split positions are all as you left them.
- [ ] Create a rig from the New Rig tab → you land on the Clip Editor tab.
- [ ] Double-click a `DirectionSetAsset` in the Project window → the Clip Editor opens on the 2D
      Direction Sets tab with that set loaded.
- [ ] From a prefab stage, the Scene view's Clip Editor overlay buttons still land on the Clip
      Editor and VAT Bake tabs.
- [ ] The active tab survives a **script recompile** (rename a target tag to force one) **and** a
      **re-dock** (drag the window out of its dock and back). Both channels carry it and only one
      being wired would present as "it forgets my tab, but only sometimes".

### The floating overlay

- [ ] Identity row (Clip Set / New Set / Rig / Edit Prefab / ⚠) floats top-right over the preview,
      right-aligned. Tool row (Move / Rotate / Scale │ Billboard │ Ragdoll) sits below it.
- [ ] **An orbit drag started on empty viewport still orbits; one started on a row does not.** This
      is the `picking-mode: Ignore` container with pickable children — if orbiting is dead
      everywhere, the container is taking clicks it should pass through.
- [ ] Click the ⚠ badge on an invalid set: findings open **below both rows**, still clipped to the
      3D area. Then drag the viewport pane down to its minimum width — the findings panel must stay
      a corner of it and must not spill over the inspector. (Its `max-width: 60%` resolves against
      the overlay container now, which is why that container stays frame-sized.)
- [ ] The overlay is gone on the other three tabs.

### Gizmos, billboard, ragdoll

- [ ] Press W / E / R in the viewport — the matching button lights. Click a button — it does what
      its key does. Gizmo dragging itself is unchanged.
- [ ] Billboard and Ragdoll behave as they did on the toolbar, including the ragdoll toggle
      springing back off when an enable is refused (a rig with no ragdoll bodies).

### Panes read the window

- [ ] VAT Bake tab: Clip Set and Rig show the window's, greyed out, with the line saying where to
      change them. Change the clip set on the Clip Editor tab, come back — VAT Bake followed it.
- [ ] Bake from the tab: it bakes what the window is showing.
- [ ] `Window ▸ DOTS Animation Toolkit ▸ VAT Bake` — the **standalone** window still has both
      pickers live and no bound-source line. This is the regression the bound mode exists to avoid.
- [ ] 2D Direction Sets tab: the read-only line names the clip set and rig, and no rig picker is
      present.
- [ ] Queue a clip that is **not** in the open clip set — that row warns naming the set, and the
      other rows keep previewing.
- [ ] Sweep the direction slider on a Six-coverage set: still turns through all six members **with
      no hitch**. A hitch means something is rebuilding the registry per facing, which the shared
      preview is supposed to have made impossible.

### The camera hand-off

- [ ] On the Clip Editor tab, orbit the camera well off front-on. Switch to 2D Direction Sets: the
      viewer is **head-on**. Switch back: the orbit is exactly where you left it. Losing it is the
      trap the spec was written around — `FrameRig` sets focus and distance and does not touch the
      angles.
- [ ] Turn Billboard off on the Clip Editor tab, visit 2D Direction Sets (which forces it on), come
      back: Billboard is off again.
- [ ] Enable Ragdoll, then switch to 2D Direction Sets: the ragdoll drops away rather than the
      viewer trying to show a facing on a collapsed rig.

### Unit context

- [ ] Pick `<Unit> · Moving`: the clip set, the rig and the turn granularity all load in one click,
      and the two toolbar fields show the change — the pane asks the window rather than setting them
      itself, so a unit pick and a hand pick take the same path.
- [ ] A unit whose prefab lists more than one clip set logs one warning naming the count and the set
      chosen.

## Files

**New (toolkit):** `Editor/ClipEditor/ClipEditorTab.cs`
**Edited (toolkit):** `ClipEditorWindow.uxml` · `.uss` · `.cs` · `Authoring/ClipEditorDocking.cs` ·
`Preview/ClipPreviewController.cs` (`OrbitYaw`/`OrbitPitch`, `IsClipInRegistry`) ·
`VatBaking/VatBakePanel.cs` · `DirectionSets/DirectionSetsPanel.cs` ·
`DirectionSets/DirectionSetContext.cs` · `Tests/EditMode/ClipEditorLayoutTests.cs`
**Edited (game):** `Editor/DirectionSetContext/UnitDirectionSetContextProvider.cs`

## Follow-ups deliberately not built

- Showing the identity controls on the New Rig / VAT Bake / 2D Direction Sets tabs. Both panes state
  what they resolved to instead; revisit if the round trip to the Clip Editor tab annoys in practice.
- Authoring a direction set whose clips span two clip sets. That is the accepted cost of the shared
  preview, and the row warning names it rather than hiding it.
- Merging every clip set an actor lists into one preview registry, so a multi-set actor previews
  fully from its unit context.
