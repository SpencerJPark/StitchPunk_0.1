# Amendment A64 — Cutscene Marks and Rendezvous Holds

> **Status:** T1–T5 built 2026-09-05, machine-verified; ⏸ owner checkpoint open. Written 2026-09-04.
> **Roadmap:** `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` — read its §4 protocol first.
> **Depends on:** A63 (schema 3, pending-op pattern in `CutsceneTimelineSystem`).
> **Session budget:** one Sonnet session.

## 1. Owner product call (2026-09-04)

> "An actors-move-to-spot option where I can create targets actors have to move to before other parts of the cutscene can continue — like the player and other guys hopping into a car together; you want them all to hop in and not leave without them."

Decided with the owner: **NPC slots pathfind through the host's movement system; the player keeps control and walks there by hand.** The clock waits at a *rendezvous hold* until every mark is reached. A per-mark timeout (off by default) teleports stragglers so a stuck NPC cannot softlock the scene.

The toolkit has no pathfinding and must not grow one. It issues a **request** on the bound entity, detects **arrival** itself (a distance test needs nothing host-specific), and releases the hold. What walks the entity there is the host's business (Stitch Punk: `MovementAPI.BeginPathRequest`, see the game's `CutsceneInteractions_System.md`).

## 2. Read first

- `Runtime/Systems/CutsceneTimelineSystem.cs` (post-A63: `ProcessAttachMarkers`, pending ops, hold branch post-A62 §3.6), `Runtime/Components/CutsceneComponents.cs`, `Runtime/Blobs/CutsceneBlob.cs`, `Authoring/Build/CutsceneBlobBuilder.cs`, `Authoring/Build/CutsceneKeySampler.cs`.
- `Editor/ClipEditor/Cutscene/CutsceneEditorPanel.cs`: `BuildMomentRow`, `BuildTransformKeyInspector`, `KeySelection`, `FindFirstHoldCrossed`, `Tick`, `ReleaseHold`; `CutscenePreviewController.cs` `ApplyPose`; `CutsceneSceneBindingUtility.cs` (bound-object lookup); the Clip Editor's `Preview/PreviewSceneGizmos.cs` only for how this package draws `Handles` in `SceneView.duringSceneGui`.

## 3. Design

### 3.1 Data

`CutsceneSlot.markKeys : List<CutsceneMarkKey>` (Actor slots; a Prop slot may carry them too — a self-driving cart is just an actor without a rig, and the host decides what "move" means):

```csharp
[Serializable]
public struct CutsceneMarkKey
{
    public float time;                    // when the move order is issued
    public float3 position;               // world
    public float facingDegrees;           // arrival facing, same 0–360 model as CutsceneFacingKey
    public float toleranceMeters;         // default 0.5 — XZ distance that counts as "there"
    public float timeoutSeconds;          // 0 = wait forever; else teleport when exceeded (real seconds, not cutscene time)
    public float previewTravelSeconds;    // default 2 — editor-only rehearsal of the walk; see A64-D2
}
```

`CutsceneHoldMarker.autoReleaseWhenMarksReached : bool = true`.

### 3.2 Bake

- `CutsceneSlotSegmentBlob.markKeys : BlobArray<CutsceneMarkKeyBlob>` (same fields minus `previewTravelSeconds`, `facingRadians` instead of degrees). `CutsceneSegmentBlob.autoReleaseWhenMarksReached : bool` (from the hold that ends the segment; `false` for the final segment).
- **A64-D2 — a mark is also a root key.** The builder merges each mark into the slot's flat root lane as a Linear `CutsceneTransformKey` at `time + previewTravelSeconds` (position = mark position, rotation = `(0, facingDegrees, 0)`, scale = the value the lane samples at that time, or `1` if the lane is empty) *before* bucketing and A62's boundary pass. Effects: the editor preview shows the walk (a lerp — rehearsal, not pathing); A62's synthetic boundary key at a rendezvous hold carries the mark pose, so the segment after the hold starts exactly where the actor arrived and the root lane resumes without a snap.
- Bake warning: a hold with `autoReleaseWhenMarksReached` lies between a mark's `time` and `time + previewTravelSeconds` — the rehearsal would release mid-walk. Reported, not fatal.
- `SchemaVersion = 4`.

### 3.3 Runtime

New component:

```csharp
/// Enabled on a bound entity when its slot's mark time is reached. The host moves the entity;
/// the player disables it on arrival (distance test) or timeout (teleport).
public struct CutsceneMoveToMark : IComponentData, IEnableableComponent
{
    public float3 position;
    public float facingRadians;
    public float toleranceMeters;
    public float timeoutSeconds;
    public float elapsedSeconds;    // player-owned
}
```

`CutsceneSlotRuntimeState` gains `nextMarkIndex`, `hasOutstandingMark`.

`CutsceneTimelineSystem`:

- `ProcessMarks` (cursor, `time <= timeInSegment`): queue a pending op "issue mark" → after the loop, `AddComponentData` (or `SetComponentData` + enable if present) `CutsceneMoveToMark` on the bound entity; `hasOutstandingMark = true`.
- Every frame, for each slot with an outstanding mark: read the entity's `LocalTransform.Position`; if XZ distance ≤ tolerance → disable the component, `hasOutstandingMark = false`. Else `elapsedSeconds += SystemAPI.Time.DeltaTime` (real time, unscaled by `CutsceneControl.speed`); if `timeoutSeconds > 0 && elapsed ≥ timeout` → write `LocalTransform.Position = mark.position`, `Rotation = quaternion.RotateY(facingRadians)`, disable, clear, log one warning naming the slot index.
- **Root lane suspended** for a slot with an outstanding mark (the mover owns the transform), exactly like A63's attached state.
- **Rendezvous release:** in the hold branch, if `segment.autoReleaseWhenMarksReached` and no slot has an outstanding mark → advance (same path as a matching `CutsceneHoldRelease`). A manual release still works and overrides (a host may decide to leave without them).
- Skip: outstanding marks are resolved by teleport, no warning.
- Completion: disable any `CutsceneMoveToMark` still enabled.

### 3.4 Editor

- **Marks** moment row per slot. Inspector: time, position (float3), facing, tolerance, timeout, preview travel, and a **Set From Object** button that reads the bound object's current world position and Y rotation.
- **Scene-view handles:** while the tab is visible and the cutscene's scene is open, draw every mark of every slot in `SceneView.duringSceneGui` — `Handles.DrawWireDisc` at `position` with radius `toleranceMeters`, a label `"<slot name> @ <time>s"`, and for the *selected* mark a `Handles.PositionHandle` whose drag writes `position` back through the `SerializedProperty` (one Undo step on mouse-up, live during the drag). Unregister on `OnHidden`/detach — a leaked `duringSceneGui` handler survives domain reloads badly.
- **Preview:** nothing new — the merged root key (A64-D2) makes the flat sampler walk the actor there.
- **Transport:** a rendezvous hold in the editor auto-continues when the playhead passes every merged mark's arrival time (in rehearsal, arrival *is* timeline time); `FindFirstHoldCrossed` treats it as crossed when that condition holds, else it waits for **Continue** as today.

## 4. Decisions

- **A64-D1** The toolkit issues `CutsceneMoveToMark` and judges arrival; it never moves the entity itself except on timeout. Pathfinding belongs to the host.
- **A64-D2** A mark is merged into the root lane at its arrival time (above) — one sampler, one preview, no snap at release.
- **A64-D3** Timeout is real time. A cutscene paused by the host must not tick a timeout, so `elapsedSeconds` only accrues while `!control.paused`.
- **A64-D4** The player character is not special to the toolkit. "The player keeps control" is the host not issuing a path for entities it deems player-driven (G2); the arrival test is the same for everyone.

## 5. Tasks

- [x] **T1 — Data, blob, builder merge (§3.1–3.2).** Test (EditMode, `CutsceneBlobBuilderTests.cs`): `Mark_IsMergedIntoTheRootLaneAtArrivalTime` — one mark at 1 s, travel 2 s, position (5,0,0), no other keys → segment 0 has a root key at 3 s at (5,0,0); with a rendezvous hold at 2 s the builder emits the mid-walk warning.
- [x] **T2 — Runtime issue, arrival, timeout, release (§3.3).** Tests (PlayMode, new `CutsceneMarkTests.cs`): `MarkTime_EnablesMoveToMarkOnTheBoundEntity`; `RendezvousHold_AutoReleasesWhenEveryMarkIsReached` (advance to the hold, move the entity within tolerance by writing `LocalTransform`, advance, assert `segmentIndex == 1`); `MarkTimeout_TeleportsAndReleases` (timeout 0.5 s, never move the entity, advance past it, assert position == mark and the hold released).
- [x] **T3 — Editor lane + inspector + Set From Object.** **[parallel-safe with T4]** Live proof via `execute_code`.
- [x] **T4 — Scene-view handles + transport auto-continue.** **[parallel-safe with T3]** Live proof: register, open the scene view, `SceneView.RepaintAll`, confirm no exception in the console; move a mark via the handle in the owner checkpoint.
- [x] **T5 — Docs.** `cutscenes.md` "Marks and rendezvous holds" (author flow + the host contract for `CutsceneMoveToMark`), CHANGELOG, HANDOFF §4.
- [ ] **⏸ Owner checkpoint.** Two actor slots, marks beside a crate, rendezvous hold, then a clip block after it. Scrub in the editor: both walk (slide) to their discs and the hold releases at arrival. Drag a disc in the Scene view; the inspector position follows.

## 6. Risks and traps

- Arrival is judged on XZ only. A mark above or below the walkable plane still resolves; the Y of the merged root key is the mark's authored Y — author marks on the ground.
- Suspending the root lane while a mark is outstanding means an actor with root keys *during* the walk ignores them. That is intended; document it.
- `Handles.PositionHandle` inside `duringSceneGui` needs `EditorGUI.BeginChangeCheck`/`EndChangeCheck` around it; write to the property only inside the check or every repaint dirties the asset.

## 7. Build log

**2026-09-05 — T1–T5 built.** EditMode `CutsceneBlobBuilderTests` 4/4, PlayMode
`CutsceneMarkTests` 3/3 (each proven to fail with the fix reverted), attach and timeline suites
still green. The A64 checkpoint is `Assets/Scenes/CutsceneA64Checkpoint.unity` +
`Assets/ScriptableObjects/Animations/A64CheckpointCutscene.asset`, copied from A63's pair rather
than edited into it.

### ESCALATION — §3.4's `Handles` prescription is banned by a shipped conformance test

`PackagingConformanceTests.Conformance_E_NoImguiApis_InEditorSources` fails any package Editor
source matching `\bOnGUI\b|\bGUILayout\b|\bHandles\.`, with no exemption list.
`Handles.DrawWireDisc`, `Handles.PositionHandle` and `Handles.Label` are therefore all unavailable,
and §2's pointer at `Preview/PreviewSceneGizmos.cs` "for how this package draws `Handles`" is
itself wrong — that file draws line meshes precisely *because* of this ban, and says so.

Built instead, in `CutsceneMarkSceneOverlay.cs`: a `SceneView.duringSceneGui` handler that draws
each mark as a line-mesh ring (`Graphics.DrawMeshNow` with `Hidden/Internal-Colored`, the same
idiom `PreviewSceneGizmos` uses) scaled to its tolerance with a facing tick, picks a mark by
casting `HandleUtility.GUIPointToWorldRay` at the mark's own Y plane, and drags it on that plane
with one `Undo.RegisterCompleteObjectUndo` per drag. **Two capabilities are lost against the spec:**
the per-mark text label, and the 3-axis position handle — a mark now drags on its ground plane
only, with height authored in the inspector. For a spot on the ground that is arguably the better
interaction, but it is a change from what was specified. *Question for the owner: accept the planar
drag, or relax `Conformance_E` for the Scene-view overlay?*

### Not machine-verified

The overlay registers on `SceneView.duringSceneGui` and repainting raises no exception, and the
transport's `RendezvousIsSatisfiedAt` / `FindFirstHoldCrossed` were exercised live (arrivals
1.8 s / 1.2 s against a 2 s hold → plays through; travel raised to 3 s → gates and waits for
Continue). **The click-and-drag path itself is unproven**: the pick and drag both need a live
Scene-view GUI context, and an unfocused Editor never repaints its Scene view, so the probe that
would have exercised them never ran. This is the one thing the owner checkpoint has to establish
by eye.

### Notes worth keeping

- The merge helper (`CutsceneMarkMerge`) is deliberately shared by the builder and the editor
  preview. A64-D2 is only true if *both* walk the merged lane; merging at bake alone would leave
  the editor showing no travel at all.
- Marks resolve every frame, including while the clock is stopped — a rendezvous hold exists to be
  released by movement happening while nothing else advances.
- T3 and T4 were **not** split across subagents despite the [parallel-safe] markers: both land in
  `CutsceneEditorPanel.cs`, so two writers would have collided in one file. HANDOFF §2's
  "do not spawn subagents" stood.

