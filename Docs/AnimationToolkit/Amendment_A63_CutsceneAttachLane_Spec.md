# Amendment A63 — Cutscene Attach Lane

> **Status:** ✅ spec, not built. Written 2026-09-04.
> **Roadmap:** `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` — read its §4 protocol first.
> **Depends on:** A62 (schema 2, `TrySampleTransform`, `CutsceneKeySampler` in `Authoring`). **Parallel-safe with:** G1 (game side).
> **Session budget:** one Sonnet session, possibly two — commit per task; the checkboxes are the resume point.

## 1. Owner product call (2026-09-04)

Actors and props must be able to *touch*: **carry and throw** (a prop rides an actor's hand socket, then leaves it with an impulse), **ride or board** (actors ride a prop — a cart — hidden or seated, and the prop's root keys carry them), and **hand-over** (a prop moves from one actor's socket to another's). Today the tool can move things independently and nothing else.

## 2. Read first

- `Runtime/Components/SocketComponents.cs` — `SocketAttachment` and *why attachments are never children*. `Runtime/Systems/SocketResolveSystem.cs` — what drives an attachment each frame and the ordering it sits at. `Runtime/Blobs/SocketRegistryBlob.cs`, `Authoring/Assets/RigAsset.cs` `SocketDefinition` (lines ~456–500: `displayName`, `Id`, `mode`, `targetId`).
- `Runtime/Systems/CutsceneTimelineSystem.cs` (all), `Runtime/Components/CutsceneComponents.cs`, `Runtime/Blobs/CutsceneBlob.cs`, `Authoring/Build/CutsceneBlobBuilder.cs` (bucketing), `Authoring/Assets/CutsceneAsset.cs`.
- `Editor/ClipEditor/Cutscene/CutsceneEditorPanel.cs`: `BuildSlotRows`, `BuildMomentRow` (both overloads), `SelectItem`, `RebuildInspector`, `BuildEventInspector` (the moment-lane + inspector pattern to copy), `InsertEventDefault`. `CutsceneMomentLaneElement.cs`. `CutscenePreviewController.cs`: `EnterPreview`/`ExitPreview` snapshot machinery, `ApplyPose`, `GetBoundPartTransform`.
- Unity.Entities.Graphics `DisableRendering` (the Runtime asmdef already references `Unity.Entities.Graphics`).

## 3. Design

### 3.1 Data (`CutsceneAsset.cs`)

`CutsceneSlot` gains `public List<CutsceneAttachMarker> attachMarkers = new List<CutsceneAttachMarker>();` (Actor and Prop slots alike — a prop rides a cart too).

```csharp
public enum CutsceneAttachKind : byte { Attach = 0, Detach = 1 }   // Runtime/Components/AnimationToolkitEnums.cs — the blob needs it

[Serializable]
public sealed class CutsceneAttachMarker
{
    public float time;
    public CutsceneAttachKind kind = CutsceneAttachKind.Attach;
    public uint hostSlotId;                 // Attach only: which slot this one rides
    public uint socketId;                   // 0 = the host's root; else a SocketDefinition.Id on the host slot's rig
    public float3 localOffset;              // in socket/host space
    public float3 localEulerDegrees;        // root attach only; a socket carries its own rotation
    public bool hideWhileAttached;          // riders inside a cart
    public float3 detachImpulse;            // Detach only; host-space, rotated to world at detach time
}
```

Semantics, written into the class remark:

- **Attach** while already attached (to anyone) = hand-over: silent detach (no signal, no impulse), then attach. Order within one frame is marker order.
- While attached, the slot's **root lane is ignored** (the host owns the transform). Part tracks and clip blocks keep working — an actor riding a cart can still wave.
- **Detach** writes the slot's `LocalTransform` to its world pose at that instant, so it stays where it was let go (Phase G §4 "actors stay where the cutscene left them"), then the root lane resumes from the next key.

### 3.2 Blob

`CutsceneSlotSegmentBlob.attachMarkers : BlobArray<CutsceneAttachMarkerBlob>`:

```csharp
public struct CutsceneAttachMarkerBlob
{
    public float time;                 // segment-relative
    public CutsceneAttachKind kind;
    public int hostSlotIndex;          // index into CutsceneBlob.slots, −1 unresolved (warn at bake, skip at play — rule T2's spirit)
    public uint socketId;
    public float3 localOffset;
    public quaternion localRotation;   // from localEulerDegrees at bake
    public bool hideWhileAttached;
    public float3 detachImpulse;
}
```

Bucketed by its own instant like events. `SchemaVersion = 3`. Bake warnings: unresolved host slot id; a `socketId != 0` on a host slot with no rig or whose rig declares no such socket; a host that is the slot itself.

### 3.3 Runtime

`CutsceneSlotRuntimeState` gains `nextAttachMarkerIndex`, `attachedHostSlotIndex (−1)`, `attachedSocketId`, `isHiddenByAttachment`.

New component, `Runtime/Components/CutsceneComponents.cs`:

```csharp
/// Enabled on the detached entity for the frame a Detach marker fires. The host reads and disables it —
/// the toolkit applies no physics of its own.
public struct CutsceneDetachSignal : IComponentData, IEnableableComponent
{
    public float3 worldImpulse;
    public Entity previousHost;
}
```

`CutsceneTimelineSystem`:

- A new `ProcessAttachMarkers` runs beside `ProcessEvents` (cursor walk, `time <= timeInSegment`). It **collects** operations into a `NativeList<PendingAttachOp>` (a private struct: kind, entity, host, socketId, offset, rotation, hide, impulse) because structural changes are illegal inside the `SystemAPI.Query` loop. After the loop, `ApplyPendingAttachOps` runs with `EntityManager`:
  - **Attach, socket ≠ 0:** `AddComponentData(entity, new SocketAttachment { actorRoot = host, socketId, localOffset })` (replace if present). Requires the host to carry `SocketRegistry`; if not, log once and treat as root attach. `SocketResolveSystem` drives the transform from here.
  - **Attach, socket = 0:** `AddComponentData(entity, new Parent { Value = host })` + `LocalTransform { Position = offset, Rotation = localRotation, Scale = current }`. Unity's `ParentSystem` handles `Child` bookkeeping. Requires the attached entity to have no `SocketAttachment` (remove it — a hand-over from a socket to a cart).
  - **Hide:** add `Unity.Rendering.DisableRendering` to the entity and every `LinkedEntityGroup` member that has `MaterialMeshInfo`; record on the slot state. Un-hide on detach by removing it from the same set.
  - **Detach:** compute the world pose — socket case: the entity's own `LocalTransform` is already world (it is a root); root-attach case: `host LocalTransform` composed with the local offset (`LocalTransform.TransformTransform` — verify the exact 6.5 API name in `Library/PackageCache/com.unity.entities*/Unity.Transforms/LocalTransform.cs`). Remove `SocketAttachment` / `Parent`, write the world pose, ensure `CutsceneDetachSignal` exists (add disabled on first need), set `{ worldImpulse = math.rotate(hostRotation, detachImpulse), previousHost }` and enable it.
- `ApplyPose` skips the root write for a slot whose `attachedHostSlotIndex >= 0`.
- **Skip:** `PerformSkip` walks every remaining attach marker in order through the same op list (end-state parity, G-D4's spirit) — signals fire with their impulse. **A63-D3** below.
- **Completion:** attachments are **left in place** — "actors stay where the cutscene left them" includes "still riding the cart". A cutscene that wants them free authors a Detach.

### 3.4 Editor

- **Lane:** every slot gets an **Attach** moment row (`CutsceneMomentLaneElement`, a distinct marker colour — pick from the existing palette in the USS, do not invent a new one). Double-click adds a default Attach at the playhead. Marker glyph differs for Attach vs Detach (two USS classes).
- **Inspector** (`BuildAttachMarkerInspector`): time, kind, host slot (dropdown of the other slots by name, storing `slotId`), socket (dropdown over the host slot's `rig.sockets` by `displayName` plus "(root)" = 0 — hidden when the host is a Prop or has no rig), offset/euler, hide toggle, impulse (Detach only). Bound through `SerializedObject` like every other inspector here — one Undo step per edit.
- **Preview** (`CutscenePreviewController`): while a slot is attached at the playhead (walk the flat marker list ≤ time), compute its world pose from the host's bound object: root attach → host `transform` × offset; socket in `SocketAttachMode.RigTarget` → the host's bound part transform for the socket's `targetId` (add `GetBoundPartTransformByTargetId`) × the socket's own `localPosition`/`localEulerAngles` × marker offset; socket in `Bone` mode → host root + a one-line warning in the inspector ("bone sockets preview at the host root") — recorded limitation. Hidden → every `Renderer` under the object gets `enabled = false`; the snapshot captures `Renderer.enabled` so `ExitPreview` restores it. Root keys are ignored while attached, same as runtime.
- The **transport** needs nothing new; skip-holds already replays the flat timeline.

## 4. Decisions

- **A63-D1** Socket attach reuses `SocketAttachment` verbatim (independent entity, world transform written by `SocketResolveSystem`); root attach uses `Parent`. Two mechanisms because the socket system's remark forbids parenting a socket attachment, and a cart has no sockets.
- **A63-D2** Detach physics is the host's. The toolkit raises `CutsceneDetachSignal` with a world impulse; Stitch Punk maps it to `ThrownItemRequest` (G2). A sellable package must not assume a physics stack.
- **A63-D3** Skip applies every remaining marker and fires their signals. Skipped and watched runs must leave the same world.
- **A63-D4** Hide is `DisableRendering` on the linked group, not `AnimVisible` — the host's visibility system (Stitch Punk mirrors `CameraVisible` into `AnimVisible` every frame) would fight anything written to the toolkit's own flag.

## 5. Tasks

- [x] **T0 — A hand socket on `NewRig.asset` (blocker, added 2026-09-05).** The rig declared zero
  sockets, so §5's checkpoint had nothing to attach to. One `SocketDefinition` — `displayName`
  "RightHand", `mode = SocketAttachMode.RigTarget`, `targetId = 3934483903` (the RightHand target),
  zero offset — added through `RigAsset.EnsureStableIds`. Gate: reload from disk, socket id non-zero
  and `SocketId.IsValid`, `SocketRegistryBuilder.HasSockets` true, `ClipValidation.ValidateRig` clean.
- [x] **T1 — Data + enum + blob + builder (§3.1–3.2).** Test (EditMode, `CutsceneBlobBuilderTests.cs`): `AttachMarker_ResolvesHostSlotIndex_AndWarnsOnUnknownHost` — two slots, one Attach naming the other → `hostSlotIndex == 1`; one naming id 0xFFFF → `−1` and exactly one warning.
- [x] **T2 — Runtime attach/detach (§3.3).** Tests (PlayMode, `CutsceneTimelineSystemTests.cs` helpers or a new `CutsceneAttachTests.cs`): `AttachToSocket_AddsSocketAttachment_AndSuspendsRootLane` (prop slot with root keys and an Attach at 0.5 s to an actor entity that carries an empty `SocketRegistry` — build a minimal `SocketRegistryBlob` in the fixture, or assert the root-attach fallback if that is too heavy; say which in §7); `Detach_RestoresIndependence_AndRaisesTheSignal` (after Detach: no `SocketAttachment`, `CutsceneDetachSignal` enabled, `worldImpulse` equals the authored impulse for an identity host rotation); `Skip_AppliesRemainingAttachMarkers`.
- [x] **T3 — Hide/unhide.** Extend T2's detach test: with `hideWhileAttached`, `DisableRendering` present while attached and gone after detach on an entity that has `MaterialMeshInfo` (add the component in the fixture; no renderer needed).
- [ ] **T4 — Editor lane + inspector (§3.4).** **[parallel-safe with T5]** No fixture. Prove live via `execute_code`: add an Attach through the private insert method, select it, inspector builds without exception, host/socket dropdowns list the right names.
- [ ] **T5 — Preview (§3.4).** **[parallel-safe with T4]** No fixture. Prove live: two bound objects, an Attach at 1 s with offset (0, 1, 0) to the host root, scrub to 2 s → the attached object's world position equals host position + (0, 1, 0); scrub to 0.5 s → back on its own keys; hide flag → renderers disabled and restored on `ExitPreview` (assert `Renderer.enabled` before/after).
- [ ] **T6 — Docs.** `cutscenes.md` "Attach lane" section (three recipes: carry & throw, board a cart, hand-over) + `CutsceneDetachSignal` in the runtime section. CHANGELOG, HANDOFF §4.
- [ ] **⏸ Owner checkpoint.** In the editor: actor with a hand socket, a prop, Attach at 1 s to the socket, Detach at 3 s. Scrub: the prop should jump into the hand at 1 s, ride the hand through a waving clip, and stay where the hand was at 3 s.

## 6. Risks and traps

- Structural changes inside `foreach (… in SystemAPI.Query…)` throw. Collect, then apply after the loop — the op list is the design, not an optimisation.
- `Parent` + `SocketAttachment` on one entity = double transform (the socket file's remark). The attach op must remove the other mechanism first.
- Removing `DisableRendering` from an entity that never had it is a no-op; adding it twice is not an error either — but track `isHiddenByAttachment` anyway so a cutscene that ends while hidden can be diagnosed (log at completion).
- `LinkedEntityGroup` on runtime-spawned actors is rebuilt by the host's spawn-init (Stitch Punk `BodyPartInitSystem`) — by the time a cutscene runs it is correct; do not cache the member list across frames.

## 7. Build log

**2026-09-05, this session.**

- **T0 was not in the written spec and had to be added at the front.** `NewRig.asset` declared
  `sockets: []`, and the only socket-shaped object in the project was a bare `HandSocket` GameObject
  under `PlayerUnit.prefab` — which is not a toolkit actor, so it is inert. §5's owner checkpoint
  ("actor with a hand socket") was unreachable. Added one `RigTarget` socket on the RightHand target
  (id `1287933773`, minted by `EnsureStableIds`); gate passed on a reload from disk.
- **T2's socket fixture builds a real `SocketRegistryBlob`** rather than falling back to a root
  attach, as §5 left open: `RagdollSystemOrderTests` already had the hand-built-registry shape to
  copy, so the socket path is covered rather than approximated.
- **T3 folded into T2's detach fixture** rather than getting one of its own — hide and detach ride
  the same attachment, and a second fixture would have been the first one retyped.
- **A hand-over needs no silent-detach op.** §3.1 describes attach-while-attached as "silent detach,
  then attach"; the implementation gets it for free because the attach op clears both `Parent` and
  `SocketAttachment` before adding either (§6's double-transform trap forces that anyway), and sets
  or clears `DisableRendering` from the incoming marker. One op, same semantics, no signal.
- **`AdvanceToNextSegment` must not reset the attachment fields**, only the two cursors. A rider that
  boarded before a hold is still aboard after it (§3.3's "attachments are left in place"), and the
  original wholesale `new CutsceneSlotRuntimeState` reset would have dropped every rider at every hold.
- **`attachedHostSlotIndex` defaults to −1, never 0.** A zeroed struct would read as "riding slot 0"
  and suppress the root lane of every slot before anything had attached — `CutscenePlaybackApi`
  initialises it explicitly for that reason.
