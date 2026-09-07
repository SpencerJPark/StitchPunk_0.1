# Cutscene API reference

A member-by-member reference for the cutscene runtime and authoring surface: every public
component, buffer, blob struct, sampling function, system, and authoring type, sourced from the
code as it stands today. For the authoring workflow (staging a scene, keying lanes, the cast panel)
see [`cutscenes.md`](cutscenes.md); this page is what a host programmer or a systems maintainer
reaches for.

**Naming note.** An earlier planning document for this feature named the play-request API
`CutscenePlaybackApi`. It was renamed to `CutsceneApi` before this page was written, and the class
below is the one that ships.

## Runtime components — `Runtime/Components/CutsceneComponents.cs`

| Type | Purpose | Written by | Read by | Lifecycle |
|---|---|---|---|---|
| `CutsceneStage` | A baked, scene-resident cutscene: the blob and its scene bindings. One entity per staged `CutsceneAsset`. | `CutsceneStageBaker` at bake | `CutsceneApi.TryFindStage` / `CreatePlayRequestFromStage` | Created at bake; never mutated at runtime. |
| `CutsceneStageBinding` (buffer, capacity 4) | One slot's scene binding baked from the cast panel: `slotId` → `target` entity. | `CutsceneStageBaker` | `CutsceneApi.CreatePlayRequestFromStage`, copied into the new request's `CutsceneActorBinding` | Baked once; a host may still add or overwrite `CutsceneActorBinding` entries afterward for actors spawned rather than staged. |
| `CutscenePlay` | The identity half of a running request: which blob, and which `PlaybackLayer` index (`layerIndex`) clip blocks target on every bound actor. Immutable once created. | `CutsceneApi.CreatePlayRequest` | `CutsceneTimelineSystem`, `CutscenePartOverrideSystem` | Added at request creation; never removed by the toolkit — destroying the request entity is the host's job. |
| `CutsceneControl` | The live control surface: `paused`, `speed` (1 = normal, clamped to 0 if non-positive), `skipRequested`. | Host, freely, at any time | `CutsceneTimelineSystem` | Added with `paused = false`, `speed` as passed to `CreatePlayRequest` (default 1), `skipRequested = false`. Free for the host to keep rewriting. |
| `CutscenePlaybackState` | The player's own advance state: `segmentIndex`, `timeInSegment`, `isPausedOnHold`, `isComplete`, `nextEventIndex`, `appliedLayerSpeed` (-1 = never applied, forcing one SetSpeed on the first frame). | `CutsceneTimelineSystem` only | `CutscenePartOverrideSystem`, host (read-only — e.g. checking `isComplete`) | Zeroed at creation (`appliedLayerSpeed = -1`); a host must never write it. |
| `CutsceneActorBinding` (buffer, capacity 4) | Host-filled: which live entity plays slot `slotId`. Explicit casting — the toolkit has no "this entity is Bertha" marker. | Host | `CutsceneTimelineSystem`, `CutscenePartOverrideSystem` | Empty buffer added at request creation (or pre-filled by `CreatePlayRequestFromStage`); the host must fill every Actor/Prop slot before the player can do anything with it. |
| `CutsceneSlotRuntimeState` (buffer, capacity 4, **internal**) | Player-owned per-slot bookkeeping, parallel to `CutsceneBlob.slots` by index (never by `CutsceneActorBinding` order). Cursors into each segment's clip-block/attach/mark arrays, attachment state, active variant/block/speed, outstanding-mark and ever-detached flags. | `CutsceneTimelineSystem` only | `CutsceneTimelineSystem`, `CutscenePartOverrideSystem` does not read it | Pre-sized to the blob's slot count at creation, every field initialized (`attachedHostSlotIndex = -1`, `activeBlockSegmentIndex = -1`, `activeBlockSpeed = 1`). Internal — a host never reads or writes this directly, but it explains why several public components behave the way they do (see the mark/attach rows below). |
| `CutsceneMoveToMark` (`IEnableableComponent`) | Enabled on a bound entity when its slot's mark time is reached: `position`, `facingRadians` (applied only on timeout), `toleranceMeters` (XZ distance that counts as arrived; Y ignored), `timeoutSeconds` (0 = wait forever), `elapsedSeconds` (player-written only). | `CutsceneTimelineSystem` (added/enabled), host's own movement (reads position/facing to walk there) | `CutsceneTimelineSystem` (judges arrival/timeout), host | Added structurally the frame the mark's cursor is reached; disabled when the host arrives (XZ distance within tolerance) or when the toolkit teleports it there after `timeoutSeconds`. Frozen (not ticked) while `CutsceneControl.paused` — but arrival still resolves every frame regardless of pause, since whatever is moving the entity may not itself be paused. |
| `CutsceneFacing` (`IEnableableComponent`) | The facing angle (`angleDegrees`, measured 0 = east, 90 = north, from +X toward +Z — the Direction Sets / `FacingResolver.FromMovement` convention, **not** a `LocalTransform` Y-euler) a cutscene is driving a bound Actor slot toward. | `CutsceneTimelineSystem` | Host (maps onto its own facing model) | Added/enabled the first frame the slot has an answer (override key, mark-vector, or root-travel direction); disabled when the cutscene completes (`DisableActorFacing`). The toolkit never writes `PartFacing` itself. |
| `CutsceneDetachSignal` (`IEnableableComponent`) | `worldImpulse` (authored impulse rotated out of host space at the instant of detachment), `previousHost` (so a host can credit a throw to it). | `CutsceneTimelineSystem`, on a Detach attach marker | Host (reads and disables it) | Added/enabled the exact frame a Detach marker fires. The toolkit applies no physics of its own — a sellable package cannot assume a physics stack — so the host is responsible for both reading and disabling this. |
| `CutsceneHoldRelease` (`IEnableableComponent`) | `holdId` — must match the current segment's hold id exactly. | Host | `CutsceneTimelineSystem` | Added (disabled) at request creation. Host sets `holdId` and enables it; the player consumes and disables it the frame it matches the paused-on segment's hold id. A mismatched id is left enabled and ignored, not an error. |
| `CutsceneCameraPose` | World-scoped singleton: `position`, `rotation`, `fieldOfView`, `isCut` (true only on the exact frame a cut marker fires), `isDriven` (true while a running, incomplete cutscene's current segment has a camera lane). | `CutsceneTimelineSystem` | Host (applies however it applies a camera — `Camera.main` is never touched by the toolkit) | Created lazily the first frame any `CutscenePlay` exists (there is only ever one — one camera, one cutscene's shot drives it). `isDriven` is cleared to false every frame before any cutscene runs, so "no camera keys" and "just ended/skipped" both read as not-driven rather than holding a stale pose. |

## `CutsceneApi` — `Runtime/Api/CutsceneApi.cs`

Static class. The write side of starting and skipping a cutscene; everything else (pause, speed,
hold release) is a direct field write on the public components above, so this class exists only for
the multi-component setup those single-field writes don't need.

| Member | Signature | Purpose |
|---|---|---|
| `CreatePlayRequest` | `static Entity CreatePlayRequest(EntityManager entityManager, BlobAssetReference<CutsceneBlob> blob, byte layerIndex = 0, float speed = 1f)` | Creates a request entity: `CutscenePlay`, a fresh `CutsceneControl`, zeroed `CutscenePlaybackState`, an empty `CutsceneActorBinding` buffer, a disabled `CutsceneHoldRelease`, an `AnimEventOutput` buffer + disabled `AnimEventsPending` (same output shape a clip's own events use, scoped to the request entity rather than any one actor), and the internal `CutsceneSlotRuntimeState` buffer pre-sized to the blob's slot count. The player never disposes the blob. The host must still fill `CutsceneActorBinding` before anything plays. |
| `RequestSkip` | `static void RequestSkip(EntityManager entityManager, Entity requestEntity)` | Convenience for the one-field case: reads, sets `CutsceneControl.skipRequested = true`, writes back. |
| `TryGetCurrentHoldId` | `static bool TryGetCurrentHoldId(EntityManager entityManager, Entity requestEntity, out FixedString64Bytes holdId)` | Answers "what is the clock waiting for?" without the caller reaching into the blob. Returns false whenever the request doesn't exist, is missing `CutscenePlay`/`CutscenePlaybackState`, isn't paused on a hold, or has an uncreated/out-of-range blob reference. |
| `CreatePlayRequestFromStage` | `static Entity CreatePlayRequestFromStage(EntityManager entityManager, Entity stageEntity, byte layerIndex = 0, float speed = 1f)` | Same as `CreatePlayRequest`, using `stageEntity`'s `CutsceneStage.blob`, plus every `CutsceneStageBinding` on it copied into the new request's `CutsceneActorBinding`. The host may still add entries for actors the stage never baked or that spawned at runtime. |
| `TryFindStage` | `static bool TryFindStage(EntityManager entityManager, ulong cutsceneKey, out Entity stageEntity)` | Finds the `CutsceneStage` whose `cutsceneKey` matches (`CutsceneAsset.StableId`). A linear scan over an `EntityQuery` on `CutsceneStage` — cache the result rather than calling this every frame if there are many stages. |

All five members are main-thread-only (they take an `EntityManager` directly and perform structural
changes); none are `[BurstCompile]`.

## Blob types — `Runtime/Blobs/CutsceneBlob.cs`

The baked form of a `CutsceneAsset`. Carries no clip registry of its own — clip ids resolve against
whichever `ClipRegistryBlob` the bound actor entity itself carries, at the moment `CutsceneTimelineSystem`
issues the `Play` command.

| Type | Fields | Notes |
|---|---|---|
| `CutsceneBlob` | `schemaVersion` (bumped on layout change, stamped at bake — currently `CutsceneBlobBuilder.SchemaVersion = 5`), `cutsceneKey` (diagnostic only), `slots: BlobArray<CutsceneSlotMetaBlob>` (authored order — what a host's binding buffer must cover), `segments: BlobArray<CutsceneSegmentBlob>` (chronological order, always ≥ 1 element) | Root struct. |
| `CutsceneSlotMetaBlob` | `slotId`, `kind: CutsceneSlotKind` | Who a slot is and its kind — never what it's bound to; that's `CutsceneActorBinding`. |
| `CutsceneSegmentBlob` | `duration`, `holdId: FixedString64Bytes` (empty for the final segment), `slotTracks: BlobArray<CutsceneSlotSegmentBlob>` (parallel to `CutsceneBlob.slots`), `cameraKeys`, `cameraCutTimes: BlobArray<float>` (segment-relative), `events: BlobArray<CutsceneEventMarkerBlob>` (segment-relative), `autoReleaseWhenMarksReached` (always false for the final segment) | One elastic-time segment: the clock runs for `duration` seconds then pauses at `holdId` (unless final). Every per-slot/camera/event time inside is rebased relative to the segment's own start. |
| `CutsceneSlotSegmentBlob` | `clipBlocks` (empty for a Prop slot), `transformKeys` (root motion for Actor, full authored motion for Prop), `facingKeys` (empty for Prop), `partTracks` (empty for Prop), `attachMarkers`, `markKeys` | One slot's baked timeline for one segment. |
| `CutsceneMarkKeyBlob` | `time` (segment-relative), `position`, `facingRadians`, `toleranceMeters`, `timeoutSeconds` | One baked move-to-mark order, bucketed by the instant its order is issued (not by its rehearsed arrival time). |
| `CutsceneAttachMarkerBlob` | `time`, `kind: CutsceneAttachKind`, `hostSlotIndex` (index into `CutsceneBlob.slots`; -1 = unresolved, warned at bake, skipped at play), `socketId` (0 = host's root), `localOffset`, `localRotation` (root-attach only), `hideWhileAttached`, `detachImpulse` (detach only, host-space) | One baked attach/detach moment, bucketed by its own instant like an event. |
| `CutsceneClipBlockBlob` | `clipId`, `start` (segment-relative), `duration`, `loop`, `blendDuration` (crossfade window inherited from the block's true predecessor on the slot's *flat* pre-segment-split lane — baked, never derived at play time, since a hold can split two overlapping blocks across segments), `speed` (0 = pre-schema-5 bake; see `CutsceneBlockTiming.EffectiveBlockSpeed`), `clipStartOffset` (seconds into the clip, issued as a `SetTime` right after `Play`), `directionVariants: CutsceneDirectionVariantsBlob` | One baked clip block. |
| `CutsceneDirectionVariantsBlob` | `hasVariants`, `south`/`southEast`/`east`/`northEast`/`north` (clip ids, 0 = unfilled), `targetDirections: AnimationDirections` (the actor's own turn granularity before the set's coverage folds it), `effectiveDirections: AnimationDirections` (what the set's filled slots actually cover) | One clip block's turn table — the direction set's east-side clips plus the resolve chain's counts. |
| `CutsceneTransformKeyBlob` | `time`, `position`, `rotation` (radians, Euler ZXY), `scale`, `interpolation`, `bezierStartHandle`, `bezierEndHandle` (read only for `Interpolation.Bezier`) | One baked transform key. Rotation stored in radians; authoring is degrees. |
| `CutsceneFacingKeyBlob` | `time`, `angleRadians` | One baked facing-override key. Angle stored in radians; authoring is degrees. |
| `CutscenePartTrackBlob` | `tagId` (diagnostics only — the runtime never looks it up again), `targetIndex` (-1 = the tag didn't resolve against the slot's rig at bake; resolved once at cutscene bake time, **not** re-resolved if the slot's rig is recast at play time — that needs a rebake), `channels: AnimatedChannels`, `keys: BlobArray<CutsceneTransformKeyBlob>` (sorted by time) | One baked per-part override track, addressed by tag at authoring time. |
| `CutsceneCameraKeyBlob` | `time`, `position` (world space), `rotation` (world space, radians, Euler ZXY), `fieldOfView` (degrees, vertical), `interpolation`, `bezierStartHandle`, `bezierEndHandle` | One baked camera pose key. |
| `CutsceneEventMarkerBlob` | `time`, `eventKey` (same vocabulary as `EventMarkerBlob.eventKey`), `intParam`, `floatParam`, `fireOnSkip` (authored on by default) | One baked event marker, same vocabulary and payload shape as a clip's own event marker. |

`CutsceneSlotKind` (`Actor = 0` — plays clip blocks on a rig, moves via root keys; `Prop = 1` — a
plain transform target with no rig and no clip lane) and `CutsceneAttachKind` (`Attach = 0`;
`Detach = 1`) live in `Runtime/Components/AnimationToolkitEnums.cs`.

## Sampling — `Runtime/Sampling/CutsceneBlobSampler.cs` and `CutsceneBlockTiming.cs`

`CutsceneBlobSampler` is `[BurstCompile]` static — the Burst-jobbable twin of the authoring-only
`CutsceneKeySampler`, evaluating the same math against `BlobArray<T>` instead of `List<T>`.

| Member | Signature | Behaviour |
|---|---|---|
| `TrySampleTransform` | `static bool TrySampleTransform(ref BlobArray<CutsceneTransformKeyBlob> keys, float time, out float3 position, out float3 rotation, out float3 scale)` | Holds the nearest key outside the authored range; interpolates between the two straddling `time` otherwise, using a per-component Euler lerp (never a quaternion slerp, matching `ClipSampler`). Returns false when `keys` is empty — every out value is the identity pose, and the **caller must leave the target's current transform alone** rather than write it, or an unkeyed slot snaps to the world origin every frame. |
| `SampleCamera` | `static void SampleCamera(ref BlobArray<CutsceneCameraKeyBlob> keys, ref BlobArray<float> cutTimes, float time, out float3 position, out quaternion rotation, out float fieldOfView, out bool isCut)` | Samples the camera lane the way it plays: a cut marker splits the lane into independent interpolation windows instead of blending across it. `isCut` is true within a `1/60`s epsilon of a cut time. Falls back to holding the most recently applied key when the current window has none of its own — same "hold last frame" rule a clip uses past its own end. |
| `TryResolveFacingAngle` | `static bool TryResolveFacingAngle(ref BlobArray<CutsceneFacingKeyBlob> facingKeys, ref BlobArray<CutsceneTransformKeyBlob> rootKeys, float time, out float angleDegrees)` | The facing angle at `time`: the last override key at or before it, else the direction the root lane is travelling. Composes `TryResolveFacingOverride` then `TryDeriveFacingFromRootTravel`. |
| `TryResolveFacingOverride` | `static bool TryResolveFacingOverride(ref BlobArray<CutsceneFacingKeyBlob> facingKeys, float time, out float angleDegrees)` | The last facing-override key at or before `time`; false when none exists yet. |
| `TryDeriveFacingFromRootTravel` | `static bool TryDeriveFacingFromRootTravel(ref BlobArray<CutsceneTransformKeyBlob> rootKeys, float time, out float angleDegrees)` | The direction the root lane travels at `time`, by finite difference against a hair (`1/60`s) earlier — forward-differenced at `t == 0`. False when travel is near-zero (`lengthsq < 1e-8`) or there are no root keys. |

`CutsceneBlockTiming` is plain static (not Burst-attributed, but trivially Burst-compatible — pure
math over floats):

| Member | Signature | Behaviour |
|---|---|---|
| `SeamBlendDuration` | `static float SeamBlendDuration(float previousBlockStart, float previousBlockDuration, float blockStart)` | The crossfade window a block inherits from its predecessor on the same lane: their overlap. Touching or gapped blocks give 0 — a hard cut. |
| `ElapsedInBlock` | `static float ElapsedInBlock(float blockStart, float timeSeconds)` | `max(0, timeSeconds - blockStart)`. |
| `ClipTimeInBlock` | `static float ClipTimeInBlock(float blockStart, float timeSeconds, float speed, float clipStartOffset)` | Where in its clip a block is at `timeSeconds`: `clipStartOffset` plus elapsed timeline seconds run at the block's own effective speed. `ElapsedInBlock` itself is timeline geometry, not scaled by speed. |
| `EffectiveBlockSpeed` | `static float EffectiveBlockSpeed(float speed)` | `speed > 0 ? speed : 1`. 0 means "unset" (a block baked before speed existed, schema < 5) — never "frozen"; the authored value is floored at 0.01, so an author can never encode "stopped" this way. That's `CutsceneControl.paused`'s job. |
| `SeamBlendWeight` | `static float SeamBlendWeight(float blockStart, float blendDuration, float timeSeconds)` | How far a seam crossfade has progressed: 0 at the incoming block's start, 1 at the end of the overlap. A zero-length window returns 1 (already fully the incoming block). |
| `LoopPhaseNormalized` | `static float LoopPhaseNormalized(float clipTimeSeconds, float clipDuration, bool loop)` | Delegates to `ClipSampler.MapTimeNormalized` with `LoopMode.Loop`/`LoopMode.Once`. |

## Systems

### `CutsceneTimelineSystem` — `Runtime/Systems/CutsceneTimelineSystem.cs`

```csharp
[UpdateInGroup(typeof(AnimationToolkitLogicSystemGroup))]
public partial struct CutsceneTimelineSystem : ISystem
```

Requires `CutscenePlay` to exist (`state.RequireForUpdate<CutscenePlay>()` in `OnCreate`). Not
`[BurstCompile]` — it drives structural changes and calls the `EntityManager` directly, main-thread
only.

Per frame, per running `CutscenePlay` request: lazily creates the `CutsceneCameraPose` singleton if
absent and clears its `isDriven` flag; applies `CutsceneControl.speed`/`paused` to every bound Actor
slot's clip layer whenever the effective speed has changed; handles `skipRequested` (jumps straight
to the cutscene's final `(segmentIndex, timeInSegment)`, so a skipped run and a watched one settle
on the identical sampled pose); resolves outstanding `CutsceneMoveToMark` orders every frame
(including while paused — a rendezvous hold is exactly when an actor is walking); advances the
segment clock and issues clip-block `Play`/`SetTime` commands through the existing
`AnimationCommand` API (no second animation pipeline); fires events into the request's own
`AnimEventOutput` buffer; processes attach/detach markers (as pending structural ops applied after
the query loop, since `SystemAPI.Query` forbids structural changes mid-iteration); processes mark
orders the same deferred way; resolves and writes `CutsceneFacing`, re-picking a block's direction
variant when the actor has turned far enough to call for a different clip; writes root/prop
transforms via `CutsceneBlobSampler.TrySampleTransform` (skipped for a slot that is attached,
walking to a mark, or has ever finished a ride); and writes the `CutsceneCameraPose` singleton.

Part-track overrides are **not** applied here — they need to land between `TransformSampleSystem`
and `TransformApplySystem` in the Presentation group; that's `CutscenePartOverrideSystem`.

### `CutscenePartOverrideSystem` — `Runtime/Systems/CutscenePartOverrideSystem.cs`

```csharp
[UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
[UpdateAfter(typeof(TransformSampleSystem))]
[UpdateBefore(typeof(TransformApplySystem))]
public partial struct CutscenePartOverrideSystem : ISystem
```

Requires `CutscenePlay` (`OnCreate`). Not `[BurstCompile]` — calls the `EntityManager` directly,
main-thread only.

Per frame, per incomplete `CutscenePlay` request: for every Actor slot with a resolved
`RigPartRef` buffer, walks that slot's `CutscenePartTrackBlob`s, resolves `targetIndex` to a live
part entity, samples the track's keys with `CutsceneBlobSampler.TrySampleTransform`, and composes
the sampled position/rotation/scale onto that part's `TargetPose` — **only the channels the track's
`AnimatedChannels` mask owns**; an unmasked channel is left at whatever the clip composited beneath
it, never reset to the rest pose. This is why the system must run after `TransformSampleSystem` (so
there is a composited `TargetPose` to layer over) and before `TransformApplySystem` (so the override
reaches `LocalTransform`/`PostTransformMatrix`).

## Authoring surface

### `CutsceneAsset` — `Authoring/Assets/CutsceneAsset.cs`

`ScriptableObject`, `IStableIdMintReporter`. A multi-actor timeline authored on one flat,
unnormalized timeline in raw seconds — hold points are markers on it, not a break in it; splitting
into elastic segments is bake-only work.

| Member | Notes |
|---|---|
| `sceneGuid` / `scenePath` | Authoritative scene binding by GUID; `scenePath` is display-only and may go stale. |
| `slots: List<CutsceneSlot>` | The abstract actor/prop slots this cutscene stages. |
| `cameraLane: CutsceneCameraLane` | The camera's keyed pose/FOV curve and hard-cut markers. |
| `events: List<CutsceneEventMarker>` | Typed markers on the shared timeline, same event-key vocabulary clips use. |
| `holdMarkers: List<CutsceneHoldMarker>` | Points where the clock pauses until the host releases it. |
| `sceneBindings: List<CutsceneSceneBinding>` | Editor-only slot-to-GameObject bindings, one entry per scene this cutscene has been opened against. Stored as strings (never the editor-only `GlobalObjectId` type) so `Authoring/` never references the editor assembly. |
| `StableId` (property) | This asset's stable 64-bit identity, folded into the bake dedup key. |
| `EnsureStableIds()` | Assigns a fresh stable id to the asset and to every slot still carrying the reserved-0 value. Idempotent; public because an asset built from code (`CreateInstance`, populate, save) hits no lifecycle hook in between — call it after populating slots and before reading any `CutsceneSlot.SlotId`. Also called from `Awake`/`OnEnable`/`OnValidate`/`Reset`. |
| `HasUnpersistedStableId` / `MarkStableIdPersisted()` | Session-only "needs saving" flag; never serialized. |

Nested authoring types:

| Type | Key fields | Notes |
|---|---|---|
| `CutsceneSlot` | `name`, `SlotId` (stable 32-bit, property), `kind: CutsceneSlotKind`, `rig: RigAsset` (ignored for Prop), `clipSets: List<ClipSetAsset>` (ignored for Prop), `directionSet: DirectionSetAsset` (ignored for Prop), `actorPrefab` (editor-only staging convenience, never read at runtime), `clipBlocks`, `transformKeys`, `facingKeys` (ignored for Prop), `partTracks` (ignored for Prop), `attachMarkers`, `markKeys` | One named, recastable slot. `EnsureStableId()` mints from the same 32-bit space `RigAsset` mints target/socket/billboard-root/ragdoll-body ids from — collisions across the two owners are harmless since a slot id and a target id are never compared. |
| `CutsceneClipBlock` | `clipId`, `start`, `duration` (`[Min(0)]`), `loop`, `speed` (`[Min(0.01)]`, default 1), `clipStartOffsetSeconds` (`[Min(0)]`) | One clip block; overlapping blocks cross-fade over the overlap, touching blocks hard-cut. |
| `CutsceneTransformKey` (struct) | `time`, `position`, `rotation` (degrees, Euler ZXY), `scale`, `interpolation`, `bezierStartHandle`, `bezierEndHandle` | A `TransformKey` reshaped for absolute seconds instead of a clip's normalized [0,1] fraction, since a cutscene's timeline has no fixed duration to normalize against. |
| `CutsceneFacingKey` (struct) | `time`, `angleDegrees` (`[Range(0,360)]`) | A continuous angle, not a discrete `Direction` — same model as the Direction Sets pane's slider. |
| `CutsceneKeyedTrack` | `tagId`, `channels: AnimatedChannels` (default `PositionXY`), `keys: List<CutsceneTransformKey>` | Tag-addressed only, no target-id fallback — a slot recast to a different rig keeps its keys wherever tags line up. |
| `CutsceneCameraLane` | `keys: List<CutsceneCameraKey>`, `cutMarkers: List<CutsceneCameraCutMarker>` | Continuous by default; cuts are the named exception. |
| `CutsceneCameraKey` (struct) | `time`, `position` (world), `rotation` (degrees, Euler ZXY, world), `fieldOfView`, `interpolation`, `bezierStartHandle`, `bezierEndHandle` | Zero FOV is not a usable value — the editor's Add Key path writes a real default (60). |
| `CutsceneCameraCutMarker` (struct) | `time` | A hard-cut instant. |
| `CutsceneEventMarker` (class, not struct — `fireOnSkip` needs a field-initializer default of `true`, which only a class gets for free in Unity's list-element UI) | `time`, `eventKey`, `intParam`, `floatParam`, `fireOnSkip` (default true), `holdUntilReleased` | `holdUntilReleased` makes the event a cue: the clock pauses the instant it fires and resumes when the host releases a hold named after the event's registry name. |
| `CutsceneHoldMarker` | `time`, `holdId` (plain string, compared for equality exactly once against a host-written control component — no shared resolution step to drift out of sync), `autoReleaseWhenMarksReached` (default true) | The clock pauses here until released; a rendezvous hold resumes on its own once every slot with an outstanding mark has arrived, though a host release still overrides it. |
| `CutsceneMarkKey` (struct) | `time`, `position`, `facingDegrees` (`[Range(0,360)]`), `toleranceMeters` (default 0.5), `timeoutSeconds`, `previewTravelSeconds` (editor-only rehearsal length, default 2 — also where the merged root key lands) | The toolkit issues the order and judges arrival; the host's own pathfinding walks the entity there. |
| `CutsceneAttachMarker` | `time`, `kind: CutsceneAttachKind` (default Attach), `hostSlotId`, `socketId` (0 = host root), `localOffset`, `localEulerDegrees` (root-attach only), `hideWhileAttached`, `detachImpulse` (host-space, rotated to world at detach time) | Attach while already attached is a silent hand-over, not an error — the previous binding drops and the new one takes its place. |
| `CutsceneSceneBinding` | `sceneGuid`, `slotBindings: List<CutsceneSlotBindingEntry>` | Editor-only, per scene. |
| `CutsceneSlotBindingEntry` | `slotId`, `globalObjectId` (string form) | One slot's binding within a `CutsceneSceneBinding`. |

### `CutsceneStageAuthoring` — `Authoring/Baking/CutsceneStageAuthoring.cs`

`MonoBehaviour`, `[DisallowMultipleComponent]`. Fields: `cutscene: CutsceneAsset` (an unassigned
cutscene bakes nothing) and `bindings: List<CutsceneStageSlotBinding>` (which live scene object plays
each slot — `slotId` + `target: GameObject`, authored by the cast panel's Sync to Stage action).

Its nested `Baker` (`CutsceneStageBaker`) calls `CutsceneBlobBuilder.Build`, logs any validation
warnings, adds the resulting blob via `AddBlobAsset`, and writes the `CutsceneStage` +
`CutsceneStageBinding` buffer described above. A binding whose target GameObject lives outside the
subscene being baked resolves to `Entity.Null` — `Baker.GetEntity` only resolves GameObjects baked
in the same subscene — so the host must supply that binding at play time instead. A binding naming a
`slotId` the cutscene doesn't declare is dropped with a warning.

### `CutsceneBlobBuilder` — `Authoring/Build/CutsceneBlobBuilder.cs`

Static class. Public surface:

| Member | Signature | Notes |
|---|---|---|
| `SchemaVersion` | `public const int SchemaVersion = 5` | Blob layout version, bumped on any layout change and stamped into `CutsceneBlob.schemaVersion` at bake. |
| `Build` | `static void Build(CutsceneAsset cutscene, out BlobAssetReference<CutsceneBlob> blob, List<string> validationWarnings)` | Turns a `CutsceneAsset` into the single `CutsceneBlob` the runtime reads. Allocates `blob` with `Allocator.Persistent` — ownership passes to the caller. `validationWarnings` is appended with one message per unresolved clip id/tag/socket/hold conflict found while baking (never thrown as an error); pass a fresh list to collect them or null to discard (each is also logged via `Debug.LogWarning`). Throws `ArgumentNullException` if `cutscene` is null. |

Internally splits the authored flat timeline into elastic segments at hold points (a clip block is
assigned to exactly one segment by its *start* time and never clipped across a boundary — clipping a
looping block at a hold would restart its loop phase at every release), resolves part-track tags and
attach-marker host-slot-ids to dense indices once at bake time, and inserts synthetic "boundary
continuity" keys so a lane still animating across a hold doesn't go stale in the segment that ends
there or empty in the one that resumes.

### `ICutsceneEventInspectorProvider` — `Editor/ClipEditor/Cutscene/CutsceneEventInspectorProviders.cs`

```csharp
public interface ICutsceneEventInspectorProvider
{
    bool TryBuildInspector(uint eventKey, SerializedProperty markerProperty, VisualElement container);
}
```

A host's editor for one event key's payload — e.g. turning a cue's raw `intParam` into a picker over
the game's own dialogue-sequence ids. Returns false to leave the default int/float fields in place.

`CutsceneEventInspectorProviders` (static): `Register(provider)` / `Unregister(provider)` maintain a
list of registered providers (registering the same instance twice is a no-op); the internal
`TryBuild(eventKey, markerProperty, container)` lets the first registered provider that claims a key
build its fields — first-claim wins, so registration order is the tie-break.

## Host integration checklist

1. **Bind.** Find the scene's staged cutscene with `CutsceneApi.TryFindStage(entityManager, cutsceneKey, out stageEntity)`, then create the request with `CutsceneApi.CreatePlayRequestFromStage(entityManager, stageEntity)`. For a cutscene with no baked stage (or actors spawned only at runtime), use `CreatePlayRequest` directly and fill `CutsceneActorBinding` by hand.
2. **Apply the camera.** Every frame, read the `CutsceneCameraPose` singleton. Apply `position`/`rotation`/`fieldOfView` to your camera only while `isDriven` is true; snap instead of easing on the frame `isCut` is true.
3. **Move to marks.** Watch for `CutsceneMoveToMark` becoming enabled on a bound entity. Walk that entity toward `position` with whatever movement/pathfinding the host has; the toolkit judges arrival (XZ distance ≤ `toleranceMeters`) and disables the component itself. Do nothing else — don't disable it yourself, and don't fight `elapsedSeconds`, which the player owns.
4. **React to detach.** Watch for `CutsceneDetachSignal` becoming enabled. Apply `worldImpulse` however the host applies impulses (the toolkit has no physics of its own), optionally crediting `previousHost`, then **disable the component** — the toolkit does not disable it for you.
5. **Map facing.** Watch for `CutsceneFacing` becoming enabled/changing on a bound Actor entity and feed `angleDegrees` into the host's own facing/orientation model. The toolkit never writes `PartFacing` directly.
6. **Release holds.** To advance past a non-rendezvous hold (or override a rendezvous one early), write `CutsceneHoldRelease.holdId` to match the current hold (`CutsceneApi.TryGetCurrentHoldId`) and enable the component.
7. **Destroy the request on completion.** Poll `CutscenePlaybackState.isComplete` (or watch for the `CutsceneCameraPose.isDriven` transition to false). Once true, the player takes no further action on that request — the host should destroy the request entity when it's done reading any trailing state (e.g. final event pulses) from it.

## Frame order

Confirmed from each system's own `[UpdateInGroup]`/`[UpdateBefore]`/`[UpdateAfter]` attributes — not
aspirational:

```
CutsceneTimelineSystem            (AnimationToolkitLogicSystemGroup)
        │  issues AnimationCommand Play/SetTime/SetSpeed/Stop, writes root/prop
        │  transforms, writes CutsceneCameraPose, fires events, resolves marks/attach
        ▼
host's own movement systems        (outside this package — walks CutsceneMoveToMark orders)
        ▼
TransformSampleSystem             (AnimationToolkitPresentationSystemGroup)
        │  composites clip tracks into TargetPose
        ▼
CutscenePartOverrideSystem        (AnimationToolkitPresentationSystemGroup, UpdateAfter TransformSampleSystem, UpdateBefore TransformApplySystem)
        │  layers CutscenePartTrackBlob keys onto TargetPose, masked by AnimatedChannels
        ▼
TransformApplySystem              (AnimationToolkitPresentationSystemGroup, UpdateAfter TransformSampleSystem)
        │  writes LocalTransform / PostTransformMatrix from TargetPose
        ▼
SocketResolveSystem               (AnimationToolkitPresentationSystemGroup, UpdateAfter TransformApplySystem)
           resolves socket-space attachments (a riding Prop, for instance) to world transforms
```

A consumer that needs this frame's cutscene-driven pose (camera, facing, part overrides) must be
ordered no earlier than `SocketResolveSystem`; a consumer of the *events*/*marks*/*attach* output
only needs to run after `AnimationToolkitLogicSystemGroup`, one group earlier.
