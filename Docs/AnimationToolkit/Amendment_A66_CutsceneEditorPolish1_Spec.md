# Amendment A66 — Cutscene Editor Polish I: Selection, Clipboard, Auto Key, Curves

> **Status:** ✅ T1–T6 built and gated 2026-09-06; stopped at the ⏸ owner checkpoint. Written 2026-09-04.
> **Roadmap:** `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` — read its §4 protocol first.
> **Depends on:** A65 (every lane exists: clip, root, facing, part, attach, marks, camera, events, holds). **Parallel-safe with:** G2 (game side).
> **Session budget:** one Sonnet session. Editor assembly only. Two small EditMode fixtures for the pure math; everything else is proved live.

## 1. Owner product call (2026-09-04)

Must-have before "finished": **Auto Key**, **multi-select / box-select / copy-paste**, and **a curve editor for cutscene keys**. G2 recorded all three as v1 cuts ("one item drags at a time", "no Auto Key", "Bézier handles only as inspector numbers").

## 2. Read first

- `Editor/ClipEditor/Cutscene/CutsceneEditorPanel.cs`: `SelectItem`, `SelectedLaneKind`, `BuildSlotRows`, `BuildMomentRow` (both), `CommitMomentTime`, `CommitClipBlockChange`, `DeleteArrayElement`, `KeySelection`, `Tick`, `RebuildInspector`, `BuildTransformKeyInspector`, `BuildCameraKeyInspector`.
- `CutsceneMomentLaneElement.cs`, `CutsceneClipBlockLaneElement.cs` (drag is visual-only until release; one `SerializedProperty` commit per drag — keep that).
- The Clip Editor's precedents, read for the *pattern* not to be copied wholesale: `Editor/ClipEditor/KeyAddress.cs`; `TrackLaneElement.cs` (`isKeySelected`, `keyPointerDown`, `ResolveTiedClick`); `BoxSelectElement.cs`; `ClipEditorWindow.cs` around lines 2824 (box-select creation), 5891–5905 (additive selection rules), 6064 (iterating selection); `ClipKeyClipboard.cs` (relative-time paste anchored at the earliest key); `EasingCurveEditorElement.cs` (`SetCurveWithoutNotify`, `curveEdited`, `CurrentInterpolation`).
- `CutscenePreviewController.cs`: `ApplyPose`, `TryKeyRoot`, `TryKeyPartTrack`; the `TransformSnapshot` capture.
- `Assets/_Vault/Memories/Code/AnimationToolkit.md` → "Never rebuild a pane from a value-changed callback" (the drag-kill trap; it applies to every field this amendment adds).

## 3. Design

### 3.1 Selection model

Replace the single `(slotIndex, laneKind, partTrackIndex, itemIndex)` selection with a set:

```csharp
internal readonly struct CutsceneItemAddress : IEquatable<CutsceneItemAddress>
{ public readonly int slotIndex; public readonly SelectedLaneKind laneKind; public readonly int partTrackIndex; public readonly int itemIndex; }
```

`HashSet<CutsceneItemAddress> selectedItems` + `CutsceneItemAddress? primaryItem` (the last clicked; drives the inspector, which shows "+ N more" when the set is larger). Rules mirror the Clip Editor's lines 5891–5905: plain click selects one; Ctrl/Cmd toggles; Shift adds; clicking an already-selected item without a modifier keeps the set (so a drag moves all of them). Lane elements get `Func<int, bool> isItemSelected` and raise pointer-down with modifiers instead of `MomentSelected`/`BlockSelected` deciding alone.

**Multi-drag:** dragging a selected item previews the same time delta on every selected item in every lane (each lane element exposes `PreviewOffsetForSelected(float deltaSeconds)`); on release, one `SerializedObject` commit writes every moved time/start (clamped at 0), then one re-sort per touched list. **Delete** removes all selected. A clip block's resize (edge drag) stays single-item.

**Box select:** a `BoxSelectElement` over the lane stack; drag on empty lane space (not on an item, not on the ruler) draws the band; on release, every item whose marker/block rect intersects the band joins the set (additive with Shift). Each lane element exposes `CollectItemsInBand(Rect worldRect, List<int>)`.

### 3.2 Clipboard

`Editor/ClipEditor/Cutscene/CutsceneKeyClipboard.cs`, static, in-memory (survives across cutscenes, not domain reloads — same as `ClipKeyClipboard`):

- `Copy(CutsceneAsset, IEnumerable<CutsceneItemAddress>)` stores deep copies grouped by lane kind with times **relative to the earliest copied time**; part-track items store the tag id; slot-scoped items store the source slot index for the "same lane" paste rule.
- `Paste(CutsceneAsset, SerializedObject, float playheadSeconds, int targetSlotIndex)`: items land at `playhead + relativeTime`; slot-scoped lanes paste into `targetSlotIndex` (the selected slot, else the source slot); part-track keys paste into the target slot's track with the same tag (created if missing, via the existing `AddPartTrack` path); camera/event/hold items ignore the slot. One Undo step. Returns a count for the transport status line.
- Shortcuts on the panel root: Ctrl+C / Ctrl+X / Ctrl+V / Ctrl+D (duplicate = copy + paste at the same time + select the copies) / Delete. Register via `RegisterCallback<KeyDownEvent>` on the panel with `focusable = true`, same as the Clip Editor.

### 3.3 Auto Key

Toolbar toggle **Auto Key** beside **Key** (default off, persisted in `SessionState`). While on and the preview is active, `Tick` runs `DetectGizmoEdits`:

- `CutscenePreviewController` records, per bound object and per bound part it wrote this frame, the exact local pose it applied (`lastAppliedPose[objectInstanceId]`). A comparison against that record — never against the sampled value — is what tells a gizmo drag from the preview's own writes; this is the feedback loop G3 declined to risk, and the record is how it is avoided.
- A difference beyond `1e-4` on any channel while `GUIUtility.hotControl != 0` means a drag is in progress: remember the object as *pending*. When `hotControl` returns to 0 with a pending object: call `TryKeyRoot` / `TryKeyPartTrack` for its slot (or the selected part track for a part transform) at the playhead, clear pending, rebuild the affected lane through `RequestTimelineRebuild` (never directly — the drag-kill trap).
- Undo: the key write is already one Undo step; Unity's own gizmo move is another. Two steps per Auto Key is acceptable and matches Unity's Animation window.

### 3.4 Curve editor

In `BuildTransformKeyInspector` and `BuildCameraKeyInspector`, after the Interpolation dropdown, host an `EasingCurveEditorElement`: `SetCurveWithoutNotify(interpolation, bezierStartHandle, bezierEndHandle)` on build and whenever the dropdown changes; `curveEdited` writes both handles through the `SerializedProperty` pair (one Undo step per drag end — the element already batches). Shown only for `Interpolation.Bezier`; for presets it shows the preset shape read-only (the element already draws it). No new element: this is the Clip Editor's, reused.

## 4. Decisions

- **A66-D1** A new address type rather than reusing `KeyAddress`: the Clip Editor's `TimelineTrackKind` has no notion of slot, part-track index, or block; forcing it would leak cutscene lanes into the clip editor's enum.
- **A66-D2** Clipboard is separate from `ClipKeyClipboard`; nothing meaningful crosses between a clip's normalized keys and a cutscene's absolute seconds.
- **A66-D3** Auto Key detects on `hotControl` release, not per-frame — one key per gesture, no key spam during a drag.

## 5. Tasks

- [x] **T1 — Selection set + modifiers + multi-drag + delete (§3.1).** Test (EditMode, new `CutsceneSelectionMathTests.cs`): `ShiftTimes_ClampsAtZero_AndPreservesOrder` for the pure delta function you extract (`CutsceneSelectionMath.ShiftTimes(List<float>, indices, delta)`). Live proof: select three markers across two lanes, drag one, all three move, one Undo reverts all.
- [x] **T2 — Box select.** Live proof only.
- [x] **T3 — Clipboard + shortcuts (§3.2).** Test (EditMode, `CutsceneKeyClipboardTests.cs`): `Paste_AnchorsRelativeTimesAtThePlayhead` (copy keys at 1.0 and 2.5, paste at 5 → 5.0 and 6.5) and `Paste_PartTrack_CreatesTheTaggedTrackWhenMissing`. Both against an in-memory `CutsceneAsset` + `SerializedObject`.
- [x] **T4 — Auto Key (§3.3).** **[parallel-safe with T5]** No fixture. Live proof via `execute_code`: enable, move a bound object's transform *with the preview active* (simulate `hotControl` by calling the private detect method with a forced "released" state), assert a key was upserted at the playhead and that scrubbing (which writes poses) adds **no** key.
- [x] **T5 — Curve editor (§3.4).** **[parallel-safe with T4]** Live proof: select a Bézier key, the element appears, dragging a handle (call `curveEdited` by reflection) writes the property.
- [x] **T6 — Docs.** `cutscenes.md` "Editing" subsection (selection, clipboard shortcuts, Auto Key, curves); delete the three matching Known-gaps lines. CHANGELOG, HANDOFF §4.
- [ ] **⏸ Owner checkpoint.** Box-select a beat, Ctrl+C, select another slot, Ctrl+V at a later playhead; Auto Key on, move an actor with the gizmo, a root key appears; open a Bézier key and drag the curve.

## 6. Risks and traps

- Every new field goes through `RequestInspectorRebuild`/`RequestTimelineRebuild` from callbacks, never a direct rebuild — the memory note's drag-kill trap.
- `KeyDownEvent` on a panel steals Ctrl+C from text fields inside it; check `evt.target is TextInputBaseField` (or the focused element is editable) and return early.
- Auto Key must be **off** while the transport plays: the transport writes poses every tick and `hotControl` can be non-zero for unrelated UI.

## 7. Build log

Built 2026-09-06 in one session, T1–T6, each task committed alone. Suites at the end: toolkit
EditMode **724 discovered / 723 passed** (the one failure being the standing `Conformance_A` asmdef
drift, untouched), toolkit PlayMode **261/261**, `StitchPunk.Tests` **59/59**,
`StitchPunk.Tests.PlayMode` **7/7** — three fixtures added against the A69 baseline of 721/720,
nothing dropped.

### Drift from the spec, and what was done instead

1. **`RequestInspectorRebuild` / `RequestTimelineRebuild` did not exist on `CutsceneEditorPanel`.**
   §6 and §3.3 name them as if they did; they are private to `ClipEditorWindow`. Built onto the
   panel with the same shape (`IsPointerGestureInProgress` plus pending flags).
2. **`Tick` is the transport tick, subscribed only while playing.** §3.3 says "`Tick` runs
   `DetectGizmoEdits`", but §6 also requires Auto Key to be off while the transport plays — so
   detection there could only ever have run when it must not. The panel gained `OnEditorTick`, an
   always-on `EditorApplication.update` subscription taken on `AttachToPanelEvent`, which both
   flushes deferred rebuilds and runs Auto Key. The method is `RunAutoKeyDetectionStep`, not
   `DetectGizmoEdits`, because it both detects and commits.
3. **§3.3 says to key "the selected part track" for a part transform.** That would write the
   *selected* part's pose rather than the pose of the part actually dragged. Implemented as: resolve
   each of that slot's part-track tags back to a bound transform and key the one that matches. A
   dragged part with no track of its own is left alone rather than misattributed. Auto-creating a
   track for it is a behaviour decision the spec does not make, and was not implemented.
4. **`SelectedLaneKind` was a private nested enum** of the panel. Promoted to a public top-level
   enum beside `CutsceneItemAddress`, so the address struct, the clipboard and the fixtures can name
   it. §3.1 sketches the struct as `internal`; it is `public`, matching `KeyAddress` and
   `TimelineTrackKind`, which the sketch was modelled on.
5. **`Paste` gained a `List<CutsceneItemAddress> pastedAddresses` out-parameter** beyond §3.2's
   signature. Ctrl+D must leave the copies selected, and only `Paste` knows their post-sort indices.
6. **`CutsceneLaneEditing` is new and not in the spec.** The lane-list lookup, the time-field name
   and the selection-tracking sort are needed identically by the panel and the clipboard. The sort
   carries a flag beside each element rather than matching items back up by time afterwards, which a
   paste at the playhead makes wrong immediately — it produces tied times by construction.
7. **`CutsceneEditorPanel` is now `partial`**, its Auto Key half in `CutsceneEditorPanel.AutoKey.cs`
   (precedent: `ClipEditorWindow.ComponentStack.cs`). This is what let T4 and T5 run in parallel
   without writing to one file.
8. **`AddBoundField` returned `void`, so §3.4's "after the Interpolation dropdown" had nothing to
   hook** — it is a bound `PropertyField`. Both overloads now return the field they build; every
   existing caller ignores it.
9. **§6's `ShouldIgnoreBindingEcho` guard does not apply to the interpolation callback.** The guard
   is generic over `ChangeEvent<TValue>` and discriminates on `previousValue != newValue`; a bound
   `PropertyField` raises `SerializedPropertyChangeEvent`, which carries neither. The callback
   rebuilds no pane — it re-seeds the widget in place — so there is no loop for the guard to break.
10. **T6's "three matching Known-gaps lines" are two.** `cutscenes.md` had no Bézier/curve gap line;
    the two that existed (multi-key drag, Auto Key) are deleted. The frozen-header line stays — that
    is A67's.
11. **§2's `ClipEditorWindow.cs` line numbers have drifted** (A69 rewrote the file). The additive
    selection rules cited at 5891–5905 are `OnKeyPointerDown` around 5077; box-select creation cited
    at 2824 is around 2354.
12. **`Object.GetInstanceID()` is a compile error in Unity 6.5**, deprecated in favour of
    `GetEntityId()`. The Auto Key pose record and pending map are keyed by `EntityId`.

### A bug found while building, not introduced by it

`AddBoundField` registered `SerializedPropertyChangeEvent → RebuildTimeline()` — a *direct* rebuild
from a change callback, on every bound field in the cutscene inspector. That is exactly the trap
§6 warns about, already live: dragging Time (s), Start (s) or Duration in the inspector cleared the
timeline, released the pointer capture and ended the drag after about a pixel. It goes through
`RequestTimelineRebuild` now, which only became possible once drift 1 above was closed. Committed
with T5.

### What was machine-verified, and what was not

Machine-verified live through `execute_code` against a real open Cutscene Editor tab, numbers in the
commit messages: the selection set and its rigid-group clamp; the band-to-selection mapping over
real resolved lane rects, additive and non-additive; the clipboard across slots including part-track
creation by tag; Auto Key's four cases, including eight scrub steps producing zero keys; and the
curve editor writing Bézier handles from both the transform and camera key inspectors.

**Not machine-verifiable, and owed to the owner's eyes:** that any of it *feels* right under a real
pointer. Every gesture above was driven by calling the methods a pointer would call, not by a
pointer. In particular nobody has yet watched a band drawn on screen, a multi-drag follow the mouse,
or a curve handle move under the cursor.
