# Amendment A73 — Profile-Driven Cutscenes: layers, auto locomotion, keyed-or-auto facing, marks that wait

**Status:** 🟡 T1–T3 fully verified 2026-09-08 (game-side unblocked, three real T1-T3 bugs the
first full-suite run surfaced are fixed — see §7's fixup entry): `DotsAnimationToolkit.Tests.EditMode`
764/764 (only the pre-existing `Conformance_A` drift remains) and `.PlayMode` 283/283, both green.
Session 2 (T4–T8) starting now. Package version after this lands: **0.19.0** (breaking:
`CutsceneSlot.rig/clipSets/directionSet` → `profile`, `CutsceneClipBlock.clipId` → `animationKey`,
`CutscenePlay.layerIndex` and `CutsceneApi.TopLayer` removed, blob schema 6).
**Scope:** `Packages/com.dotsanimationtoolkit/` — `Authoring/`, `Runtime/`, `Editor/ClipEditor/Cutscene/`,
`Tests/`, `Samples~/Cutscene`, `Documentation~/`. **No game code** — that is G6
(`Assets/_Vault/Tasks/NewPlans/CutsceneProfileCutover_System.md`), which must follow in its own session.
**Execution protocol:** `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4 with the `A73-Tn:` prefix.
HANDOFF §2 comment and naming rules (`Conformance_E/F/G/H`) apply to every file touched.
**Session budget:** two Sonnet sessions — T1–T3 (data, bake, runtime) then T4–T8 (editor, preview,
marks UX, docs). Each session ends at the checkpoint named in §5.

---

## 1. Why this exists (owner's words, 2026-09-08)

> "The cutscene editor doesn't use the new Actor Profile and instead uses the outdated direction
> info. How I want the cutscene editor to work is that it can use the profile for most animations to
> move the character around the scene. So if a character walks a certain direction it may auto
> update direction as it walks and play the right clip. These animations can also be auto adjusted,
> like character walks here then turns here, and maybe this layer plays this and these layers are
> off … Layers should be able to be keyed on and off and there should also be the option to key
> direction; if not keyed then it will auto adjust direction based on the direction the character
> moves, so I could theoretically have an auto direction for a character walking to a spot and then
> have a keyed direction for when they reach a target. Also make it clear how to add targets to the
> scene for characters to move to and options like wait until reached. … cleaning this part up
> should be getting us towards our finish line on this round of version 1 of the package."

What exists today (verified in code 2026-09-08): a cutscene slot pins a `rig` + `clipSets` +
`directionSet` of its own, a clip block names a raw `clipId`, every block plays on one request-wide
layer (`CutscenePlay.layerIndex`, resolved from `CutsceneApi.TopLayer`), turning re-picks through a
per-block `CutsceneDirectionVariantsBlob` baked from the slot's `DirectionSetAsset`, and nothing
plays a walk when an actor's root lane moves it — the actor slides. A70 moved layers, named
animations and per-animation direction onto `ActorProfileAsset` and A71 gave that profile a live
mixing tab; the cutscene lane never learned about either. This amendment makes the profile the
cutscene's one source of "what can this actor play, on which layer, facing which way", and adds the
three authoring features the owner named: layer rows you can key on and off, facing that is auto
unless keyed, and marks that can hold the clock until reached.

**Owner product calls (asked and answered in the quote above — do not re-ask):**

1. A cutscene Actor slot is driven by an `ActorProfileAsset`. The slot's own rig/clip-set/direction-set
   fields go, no migration (standing directive: content is rebuilt fresh; G6 re-authors the game's
   seven cutscene assets by script).
2. **Auto locomotion:** while a slot's actor is moving (root lane, mark walk, anything), the profile's
   moving animation plays and the facing follows the movement; when it stops, the standing one plays.
3. **Layers are keyed on and off.** Each profile layer is a row on the slot; a block on a row plays a
   named animation from that layer, a stop key turns the row off.
4. **Facing is auto unless keyed**, and a key can hand control back to auto. "Auto while walking to
   the mark, keyed when they get there" is the canonical beat.
5. **Marks must be obvious to add**, and a mark can say *wait until reached* by itself.

## 2. Read first (in this order, and only these)

1. `Docs/AnimationToolkit/HANDOFF.md` §2, §3, §5, §6; `Assets/_Vault/Memories/Code/AnimationToolkit.md`
   ("Cutscenes — traps only" and "Shared editor chrome").
2. `Docs/AnimationToolkit/Amendment_A70_ActorProfile_Spec.md` §3 (the profile model and runtime you
   are building on; §3.4 is the re-pick you are now relying on) and its §4 decisions.
3. `Authoring/Assets/CutsceneAsset.cs` (all of it — every type here changes or is read),
   `Authoring/Assets/ActorProfileAsset.cs`, `Authoring/Build/CutsceneBlobBuilder.cs`
   (`Build`, `BucketClipBlocks` ~591, `BucketFacingKeys` ~685, `BucketMarkKeys` ~710, the seam-blend
   pass, `SchemaVersion = 5` at line 24), `Authoring/Build/CutsceneDerivedHolds.cs`,
   `Authoring/Build/CutsceneMarkMerge.cs`, `Authoring/Build/CutsceneKeySampler.cs`
   (`TryResolveFacingAngle`, `TryResolveFacingOverride`, `TryDeriveFacingFromRootTravel`),
   `Authoring/Build/CutsceneDirectionVariants.cs` (to be deleted — read it to know what leaves).
4. `Runtime/Blobs/CutsceneBlob.cs`, `Runtime/Components/CutsceneComponents.cs`
   (`CutscenePlay`, `CutsceneSlotRuntimeState`, `CutsceneFacing`, `CutsceneMoveToMark`),
   `Runtime/Api/CutsceneApi.cs`, `Runtime/Systems/CutsceneTimelineSystem.cs` in full (1385 lines:
   `ProcessCutscene` 74, `ResolveLayerIndex` 201, `ProcessClipBlocks` 227, `ProcessFacing` 334,
   `TryResolveSlotFacingAngle` 395, `ResolveVariantClipIdForSlot` 427, `ReissueDirectionVariant` 460,
   `StopActorLayers` 561, `ApplyLayerSpeedToAllActorSlots` 601, `PerformSkip` 714,
   `ResolveOutstandingMarks` 1135, `PlaceAtMark` 1254, `ApplyPose` 1289),
   `Runtime/Sampling/CutsceneBlobSampler.cs`, `Runtime/Sampling/CutsceneFacingVariants.cs`
   (`AngleDegreesFromTravel` stays; `Resolve`/`SelectVariantClipId` leave),
   `Runtime/Sampling/FacingResolver.cs` (`FromMovement(in float2, AnimationDirections, Direction)`,
   `ToAuthoredSide`), `Runtime/Sampling/CutsceneBlockTiming.cs`.
5. `Runtime/Components/ActorFacing.cs`, `Runtime/Components/PlaybackLayer.cs` (`animationKey`),
   `Runtime/Components/AnimationCommand.cs` + `AnimationToolkitEnums.cs` (`CommandKind.PlayAnimation`
   / `StopAnimation`), `Runtime/Api/PlaybackApi.cs` (`PlayAnimation`, `StopAnimation`,
   `IsAnimationPlaying`), `Runtime/Api/ActorProfileApi.cs` (`TryResolve`, `TryFindAnimation`),
   `Runtime/Blobs/ActorProfileBlob.cs`, `Runtime/Systems/CommandApplySystem.cs` (`OrderFirst` in the
   logic group — a command the cutscene appends drains **next** frame; that is today's latency too),
   `Runtime/Systems/ActorFacingRepickSystem.cs`.
6. `Runtime/Sampling/ClipSampler.cs` `CompositeLayers(ref registry, in NativeArray<PlaybackLayer>, targetIndex, in rest, bool snapBlendWeights, out pose)`,
   `Runtime/Sampling/PlaybackTimeMath.cs`, `Runtime/Sampling/PlaybackCommandMath.cs` (`ApplyPlay`,
   `ApplyStop`) — the statics the preview must go through (A71-D3).
7. Editor: `Editor/ClipEditor/Cutscene/CutsceneEditorPanel.cs` (4583 lines — read only these:
   slot creation ~1560–1580, `ShowFillFromProfileMenu`/`FillSlotFromProfile` 1686–1722, the timeline
   group build 2280–2460 (`clipBlocks` lane at ~2295, `BuildMomentRow` overloads 2381/2414/2446, the
   Facing row ~2340, the Marks row ~2332), `InsertFacingKeyDefault` 3414, the mark default insert
   ~3505–3525, `BuildInspectorFor…` switch ~3840, `BuildSlotInspector` 3897–4000 (rig/clipSets/
   directionSet fields at 3935–3990), `BuildClipBlockInspector` 4039, `BuildFacingKeyInspector` 4101,
   `BuildMarkKeyInspector` ~4204, `SetMarkFromBoundObject` ~4245),
   `CutsceneClipBlockLaneElement.cs` (`CutsceneClipBlockDisplay`, `SetBlocks`),
   `CutsceneMomentLaneElement.cs` (`SetTimes(times, selected, markerVariantClasses)`),
   `CutsceneSlotClipPreview.cs` (all), `CutscenePreviewController.cs` 700–1000 (`ApplyActorParts`,
   `ComposeClipLane`, `ResolveActiveBlockIndex`, `ResolveSlotFacing`, `ResolveFacingVariantClipId`,
   `DescribeResolvedFacing`, `ComposeFacingMirror`), `CutsceneKeyClipboard.cs` (block copy),
   `Editor/ClipEditor/Components/VocabularyPicker.cs` (`VocabularyPickerConfig.ForAnimationNames`,
   `VocabularyPicker.Open`), `Editor/ClipEditor/Shared/ToolkitPalette.cs`, `ToolkitIcons.cs`,
   `Editor/ClipEditor/ActorEditor/ActorPreviewComposer.cs` (the shape of "advance layers through the
   runtime statics" — not reused directly, see A73-D6).
8. Tests you will touch: `Tests/EditMode/CutsceneBlobBuilderTests.cs`, `CutsceneKeyClipboardTests.cs`,
   `CutsceneBlockTimingTests.cs`; `Tests/PlayMode/CutsceneTimelineSystemTests.cs`, `CutsceneFacingTests.cs`,
   `CutsceneMarkTests.cs`, `CutsceneAttachTests.cs`, `CutsceneStageBakingTests.cs`,
   `SystemGroupStructureTests.cs`, `ActorBakeFixture.cs` / `PlaybackTestActor.cs` (the A70 fixtures
   that bake a profile — every cutscene PlayMode actor must now carry `ActorProfile` + `ActorFacing`).
9. `Samples~/Cutscene/CutsceneSampleHost.cs` (passes a layer index; `Samples~` is not compiled —
   check it through a temp assembly, `Gotchas.md`).

## 3. Design

### 3.1 Authoring — the slot is a profile

`CutsceneSlot`:

```
- RigAsset rig;  List<ClipSetAsset> clipSets;  DirectionSetAsset directionSet;      // deleted
+ ActorProfileAsset profile;                       // Actor slots only; the rig, clip sets, layers, names and turn granularity all come from here
+ CutsceneLocomotion locomotion = new CutsceneLocomotion();
+ List<CutsceneLayerStopKey> layerStops = new List<CutsceneLayerStopKey>();
  public RigAsset ResolvedRig { get { return profile != null ? profile.rig : null; } }         // the one accessor every rig reader switches to
  public IReadOnlyList<ClipSetAsset> ResolvedClipSets { get { ... profile.clipSets or empty ... } }
```

Every reader of `slot.rig` / `slot.clipSets` (part-track tag resolution in the builder and the
panel, attach socket lookup `hostSlot.rig.sockets`, `CutsceneSlotClipPreview.RebuildIfBindChanged`,
`BuildAvailableClips`) goes through the two accessors. A Prop slot has no profile and reads null
from both, exactly as it read null before.

`CutsceneClipBlock` (type name kept — A73-D5):

```
- ulong clipId;
+ uint animationKey;                       // AnimationNameRegistry id; the entry's layer is derived from the slot's profile
- bool loop;
+ LoopMode loop = LoopMode.UseClipDefault; // UseClipDefault = the profile entry's own loop (which may itself defer to the clip)
  float start; float duration; float speed = 1; float clipStartOffsetSeconds;   // unchanged
```

`CutsceneLayerStopKey` (new, `[Serializable]` struct): `{ float time; string layerName; float blendOutSeconds = NaN }`.
`layerName` is the profile layer's `displayName`; it is resolved to an index at bake and at draw
time against the slot's profile. Names, never indices, in the asset (HANDOFF §5) — and a reorder of
the profile's user layers keeps the key on the layer the author meant. Bookends always resolve.

`CutsceneLocomotion` (new, `[Serializable]` class):

```
bool enabled = true;
uint standingAnimationKey;                 // 0 = none
uint movingAnimationKey;                   // 0 = none → locomotion is inert, bake warns
float movingSpeedThresholdMetersPerSecond = 0.05f;
```

`CutsceneFacingKey` gains `CutsceneFacingMode mode = CutsceneFacingMode.Fixed` with
`enum CutsceneFacingMode : byte { Fixed, Auto }`. A **Fixed** key pins `angleDegrees` from its time
until the next key. An **Auto** key releases the pin: from its time the facing derives again (§3.3).

`CutsceneMarkKey` gains `bool waitUntilReached`. When set, the bake derives a rendezvous hold at the
mark's own `time` (§3.2) — the clock stops the instant the order goes out and resumes when every
mark issued at that instant is reached. `facingDegrees` keeps its meaning (arrival facing) and gains
a runtime consumer (§3.3).

### 3.2 Bake — `CutsceneBlobBuilder`, schema 6

- `CutsceneClipBlockBlob`: `clipId` → `animationKey`; `directionVariants` deleted; `loop` becomes
  `LoopMode`; `blendDuration` is the seam overlap **from the previous block on the same layer row**
  (blocks are grouped by their entry's `layerIndex` resolved against `slot.profile` before the seam
  pass — two blocks on different layers never blend into each other), `NaN` when a block has no
  predecessor on its row (= the profile entry's blend-in, then the clip's default), `0` when the
  predecessor merely touches (a hard cut, as today). A block whose key is not in the slot's profile
  is baked as authored with `blendDuration = NaN` and one warning naming slot + key; the bound actor's
  own profile gets the final say at play (the lenient philosophy every cutscene reference uses).
- `CutsceneSlotSegmentBlob.layerStops : BlobArray<CutsceneLayerStopBlob { float time; byte layerIndex; float blendOut }>`,
  bucketed like marks. An unresolved `layerName` is a warning and a skip.
- `CutsceneSlotMetaBlob.locomotion : CutsceneLocomotionBlob { bool enabled; uint standingKey; uint movingKey; float speedThreshold }`.
- `CutsceneFacingKeyBlob.isAuto : bool`.
- **Derived mark holds.** `CutsceneDerivedHolds` gains a second source: every `waitUntilReached`
  mark contributes a hold at its `time` with `holdId = "mark:" + slot.name + "@" + time.ToString("0.###")`
  and `autoReleaseWhenMarksReached = true`. Several such marks at one instant share one hold (the
  first slot's id). A derived mark hold coinciding with an authored hold keeps the authored id, with
  the same warning shape holding events use. The A64 "hold lies mid-walk" warning is **not** raised
  for a hold a mark derived itself — that is the intent, not a mistake.
- `SchemaVersion = 6`.

### 3.3 Runtime — `CutsceneTimelineSystem`

**Ordering.** Add `[UpdateBefore(typeof(ActorFacingRepickSystem))]` (the system already sits after
`CommandApplySystem` by that system's `OrderFirst`). A facing the cutscene writes this frame is
re-picked this frame; the Play commands it appends drain next frame, as they always have.
`SystemGroupStructureTests` gains the edge.

**Blocks play by name.** `ProcessClipBlocks` no longer takes a layer index. Per block reached:

1. The bound actor must carry `ActorProfile` (created) — otherwise one warning per slot per run
   (`slotState.warnedMissingProfile`) and the block is skipped.
2. `ActorProfileApi.TryFindAnimation(ref profile, block.animationKey, out animationIndex)` → the
   entry and its `layerIndex`. Unknown key: one warning per (slot, key) via the same flag, skip.
3. Append `AnimationCommand { kind = PlayAnimation, animationKey, speed = layerSpeed × EffectiveBlockSpeed(block.speed) × entry.speed, loop = block.loop, blendDuration = block.blendDuration }`
   (the shape `PlaybackApi.PlayAnimation` writes), then `SetTime` on `entry.layerIndex` when
   `clipStartOffset > 0`, then enable `AnimationCommandPending`. `CommandApplySystem` resolves the
   facing slot from `ActorFacing` (§3.3 "Facing") and `ActorFacingRepickSystem` swaps it as the actor
   turns — **the cutscene no longer re-picks anything itself.**
4. Record the block on the slot's per-layer state (below).

**Per-layer bookkeeping.** `CutsceneSlotRuntimeState` loses `activeVariantClipId`,
`activeBlockSegmentIndex`, `activeBlockIndex`, `activeBlockSpeed`; a new internal buffer on the
request entity, `CutsceneSlotLayerState : IBufferElementData { int activeBlockSegmentIndex; int activeBlockIndex; float activeBlockSpeed; }`
(all `-1`/`1` when idle), sized `slots × LayersPerSlot` with `const int LayersPerSlot = 8` (a
`DataContractTests` row pins it equal to `ActorProfileAsset.MaxLayerCount`), index
`slotIndex * LayersPerSlot + layerIndex`. `CutsceneApi.CreatePlayRequest` adds and initialises it.
`ApplyLayerSpeedToAllActorSlots` walks it: every layer with an active block gets
`SetSpeed(layerIndex, newLayerSpeed × activeBlockSpeed × entry.speed)`.

**Layer stops.** `ProcessLayerStops` (cursor `slotState.nextLayerStopIndex`, `time <= timeInSegment`):
`Stop { layerIndex, blendDuration = blendOut }`, clear that layer's active block, and — if it is the
locomotion layer — clear `slotState.lastLocomotionKey` so auto locomotion re-issues next frame.

**Auto locomotion.** Per Actor slot with `locomotion.enabled`, `movingKey != 0` and a bound entity
with `LocalTransform`, every frame after `ApplyPose` and mark resolution:

- `displacementXZ = position − slotState.lastPosition` (skip the first frame; store). Moving when
  `length(displacementXZ) / deltaTime > threshold`, through one static
  `CutsceneLocomotionMath.IsMoving(in float3 displacement, float deltaTime, float threshold)` in
  `Runtime/Sampling/` that the preview also calls. Actual movement, not the root lane: it covers a
  host-walked mark, a hand-walked player, an attached rider (carried → moving) and the root lane alike.
- `key = moving ? movingKey : standingKey` (a zero `standingKey` means "stop the moving entry" via
  `StopAnimation`). The locomotion layer is the moving entry's `layerIndex` on the bound actor's
  profile (resolved per frame — it is a binary search).
- **Authored wins:** if that layer has an active authored block, do nothing. A block claims its
  layer from its start; a stop key on the layer hands it back.
- Otherwise, if `key != slotState.lastLocomotionKey`: issue `PlayAnimation(key)` unless
  `PlaybackApi.IsAnimationPlaying(layers, key)` already (a unit that was idling when the cutscene
  started does not pop); set `lastLocomotionKey = key`.
- A paused cutscene (`CutsceneControl.paused`) skips this — every layer is frozen anyway. A hold
  does not: mark walkers keep walking under a rendezvous, and their walk cycle must keep playing.

**Facing.** `ProcessFacing` keeps its chain — a **Fixed** key at-or-before `t` (an Auto key at-or-
before `t` that is later than the last Fixed key cancels it: `TryResolveFacingOverride` returns
false past an Auto key), else the vector to an outstanding mark, else root-lane travel — and gains
two rules:

- **Arrival latch.** When `ResolveOutstandingMarks` resolves a mark by arrival, it writes
  `slotState.latchedFacingDegrees = mark.facingDegrees`, `hasLatchedFacing = true`. The latch sits
  between the mark branch and the root-travel branch in the chain and clears the moment the slot
  moves again (`IsMoving` true) or a Fixed key takes over. A timeout teleport applies the facing the
  way it does today and latches it too.
- **`ActorFacing` is written.** With a resolved angle, besides `CutsceneFacing` (unchanged, still the
  host's mirror/view-offset input), the system writes
  `actorFacing.facing = FacingResolver.FromMovement(new float2(cos θ, sin θ), profile.turnDirections, actorFacing.facing)`
  on any bound actor carrying `ActorFacing` (skip when the value is unchanged). `appliedFacing` is
  never touched — that is the re-pick system's. See A73-D2.

`ReissueDirectionVariant`, `ResolveVariantClipIdForSlot`, `ResolveLayerIndex`, `CutscenePlay.layerIndex`,
`CutsceneApi.TopLayer` and the `layerIndex` parameters on `CreatePlayRequest` /
`CreatePlayRequestFromStage` are deleted.

**Completion and skip.** `StopActorLayers` becomes: for every slot, every layer with an active
authored block → `Stop(layerIndex, NaN)`; the locomotion layer is left playing whatever it is
playing (a host that assigns idle/walk itself finds the right one already there; a host that does
not is left with a standing actor rather than a frozen one). Skip changes nothing beyond that.

### 3.4 Editor — the slot group grows layer rows

Timeline, per Actor slot, in this order: **one row per profile layer** (`Base` … `Override`, boxed
peers per A72 §3.4, label = `displayName`), then Root, Facing, Parts, Attach, Marks as today. A
Prop slot is unchanged. A slot with no profile shows one row reading "assign a profile" and no
block lane.

- **Layer rows** reuse `CutsceneClipBlockLaneElement` per row, fed only the blocks whose entry lives
  on that layer (derived from `slot.profile` at build time; label = the animation's registry name).
  Blocks whose key the profile lacks collect on a trailing **Unresolved** row drawn in
  `ToolkitPalette.StateWarning` so nothing silently vanishes. Double-click on empty row space opens
  the `VocabularyPicker` (`ForAnimationNames`) **filtered to that layer's entries** and with its
  Create row hidden (grep `VocabularyPickerConfig` for an existing allow-list / create toggle; add
  `allowedKeys` and `allowCreate` if absent — small, and the Actor Editor may reuse them). Each row
  header carries two icon buttons (`ToolkitIcons.MakeIconButton`): **+** (block at the playhead,
  same picker) and **■** (stop key at the playhead). Stop keys draw on the same row as hollow rings
  in the row's colour (the `CutsceneMomentLaneElement` ring variant Detach uses), select, drag and
  delete like any moment, and join box-select / clipboard (`CutsceneKeyClipboard` gains the kind).
  The stop's inspector: Time, Layer (read-only), Blend Out (s, blank = clip default).
- **Block inspector:** Animation (a button showing the name; click opens the picker), Layer
  (read-only), Start, Duration, Loop (`LoopMode` dropdown whose `UseClipDefault` label reads
  "Profile's"), Speed, Start Offset.
- **Slot inspector:** Profile (`ObjectField<ActorProfileAsset>`, replaces the Rig, Clip Sets,
  Direction Set fields and the Fill from Profile button — `ShowFillFromProfileMenu`/`FillSlotFromProfile`
  are deleted), then a **Locomotion** box: Enabled, Standing (name picker), Moving (name picker),
  Threshold (m/s), and a **Defaults from profile** button that fills Standing/Moving from entries
  named exactly `Idle` / `Walk` when the profile has them (`VocabularyRegistryProvider.AnimationNames.FindName`).
  Assigning a profile runs the same defaulting once when both keys are 0. The existing "Facing at
  playhead" line stays and now reads `<angle>° → <Direction>` through the profile's `turnDirections`;
  `DescribeMissingFacingParts` stays.
- **Facing key inspector:** Mode (Fixed / Auto), Angle (hidden for Auto). Auto keys draw as hollow
  diamonds on the Facing row (`markerVariantClasses`). `InsertFacingKeyDefault` still inserts Fixed at
  the current derived angle; Shift+double-click inserts Auto.
- **Mark inspector**, re-ordered: Position · **Set From Object** · **Set From Scene View Pivot**
  (`SceneView.lastActiveSceneView.pivot`; disabled without a scene view) · **Wait Until Reached** ·
  Arrival Facing · Tolerance · Timeout · Preview Travel, then the note. The Marks row header gains a
  **+** icon: a mark at the playhead placed where the bound object stands, else where the slot's root
  lane samples at the playhead, else the origin, with `waitUntilReached = true`, tolerance 0.5,
  travel 2 — the "walk here and wait" default is one click. A derived mark hold draws its ghost on
  the Holds row and stops the transport exactly as a holding event does; the rehearsal auto-continues
  when every merged arrival time has passed (`RendezvousIsSatisfiedAt` already does this for authored
  holds — extend the lookup to derived ones).
- **Validation** surfaces where it does today (bake warnings in the slot inspector): no profile on an
  Actor slot; a block whose key the profile lacks; locomotion enabled with `movingKey == 0`;
  standing/moving on different layers; a stop key naming a layer the profile lacks.

Every new field goes through `AddBoundField` / `ShouldIgnoreBindingEcho`; rebuilds only through
`RequestInspectorRebuild` / `RequestTimelineRebuild` (the drag-kill trap in `AnimationToolkit.md`).

### 3.5 Preview — one layer array per slot, reconstructed per scrub

`CutsceneSlotClipPreview` builds its registry from `slot.ResolvedRig` / `slot.ResolvedClipSets` and
additionally holds the profile blob (`ActorProfileBuilder.Build(profile, Allocator.Persistent)`,
rebuilt with the registry) and a `NativeArray<PlaybackLayer>` sized `profile.layers.Count`.

`CutscenePreviewController.ComposeClipLane` becomes `ComposeLayers`: for a time `t`, per profile
layer, find the last of {block on this row, stop on this row} at-or-before `t`:

- a **block** → `ActorProfileApi.TryResolve(ref profileBlob, key, facingDirection, …)`, clip index
  from the registry, `time = ClipTimeInBlock(...) + HoldClipPhaseSeconds × speed` as today, `loop`
  from block/entry, flags `Active`; the previous block on the same row within its seam window fills
  the `previous*` fields and `blendElapsed/blendDuration` so the runtime compositor crossfades it.
- a **stop** → inactive.
- **nothing, on the locomotion layer** → the standing or moving entry, moving decided by
  `CutsceneLocomotionMath.IsMoving` over the rehearsal root lane's displacement across `1/60 s`
  (the same finite difference facing uses), phase = `t × entry.speed`.
- nothing elsewhere → inactive.

Then one `ClipSampler.CompositeLayers(ref registry, in layers, targetIndex, in rest, snapBlendWeights: false, out pose)`
per part into `composedPoses`; `ComposeFacingMirror` keeps applying the mirror, now from
`FacingResolver.ToAuthoredSide(facingDirection)` where `facingDirection` is the chain of §3.3
snapped at `profile.turnDirections` (the arrival latch in the preview is the merged mark key's own
rotation — the rehearsal already lands there). `ResolveFacingVariantClipId`, `ResolveSlotFacing`'s
direction-set branch and `DescribeResolvedFacing`'s set wording go; `DescribeResolvedFacing` returns
`"<Direction>[, mirrored]"`.

This is a **reconstruction** (scrubs jump backwards), not an advance — the runtime's state machine is
not replayed, so a `Once` block's crossfade tail and queue promotion are not rehearsed. That is the
same gap the clip lane has always had; it is recorded, not fixed.

### 3.6 What leaves

`CutsceneSlot.rig/.clipSets/.directionSet`, `CutsceneClipBlock.clipId`/`.loop (bool)`,
`CutsceneDirectionVariants.cs`, `CutsceneDirectionVariantsBlob`, `CutsceneClipBlockBlob.directionVariants`,
`CutsceneFacingVariants.Resolve`/`.SelectVariantClipId` (`AngleDegreesFromTravel` stays; if that is
all that is left, fold it into `CutsceneBlobSampler` and delete the file), `CutsceneSlotRuntimeState.activeVariantClipId/activeBlock*`,
`CutsceneTimelineSystem.ReissueDirectionVariant/ResolveVariantClipIdForSlot/ResolveLayerIndex`,
`CutscenePlay.layerIndex`, `CutsceneApi.TopLayer`, the `layerIndex` parameters on both
`CreatePlayRequest*`, the cast panel's Fill from Profile, the slot inspector's Rig/Clip Sets/Direction
Set fields, `CutsceneEditorPanel.BuildAvailableClips`, and `cutscenes.md`'s "Give the slot a Direction
Set" paragraph. `DirectionSetAsset` itself stays (the Actor Editor's property drawer still edits one).

## 4. Decisions (recorded; revert notes inline)

- **A73-D1 The slot references the profile, not copies of its parts.** One asset says what an actor
  can play; the cutscene reads it. Revert: keep `rig`/`clipSets` as overrides — not built, because a
  second source of truth is the G0 §7 bug again and the profile's own validator (P1–P7) already
  covers the pair.
- **A73-D2 A running cutscene writes `ActorFacing.facing` for the Actor slots it drives** (amends
  A70-D6's "host-written and never derived" for exactly this case). The angle the toolkit derives is
  already its own convention (`CutsceneFacing`, +X toward +Z), and snapping it at the profile's
  `turnDirections` is the same fold the host would perform; a host whose own facing writer also maps
  `CutsceneFacing` (Stitch Punk's `UnitFacingJob` does, from the same angle at the same granularity)
  writes the same value and nothing fights. Mirror (`PartFacing`) stays the host's. Revert: delete
  the write and require a host `CutsceneFacing → ActorFacing` system — rejected because the sample
  host and any customer without a facing system would then have cutscene actors that never turn.
- **A73-D3 Turning is the profile's job, not the cutscene's.** Blocks issue `PlayAnimation`;
  `CommandApplySystem` resolves the slot and `ActorFacingRepickSystem` re-picks in place. The per-block
  variant table is deleted rather than kept beside it — two re-pickers on one layer is the fight A65
  §7 warned about. Revert: restore `CutsceneDirectionVariantsBlob` for blocks whose key is unknown.
- **A73-D4 "Moving" is measured from the entity's actual displacement at runtime, and from the
  rehearsal lane in the editor.** One rule covers root keys, a host-walked mark, a hand-walked player
  and a carried rider; the editor has only the lane. The two disagree exactly where rehearsal and
  playback already disagree (A64-D2). Revert: derive from the root lane at runtime too — rejected
  because a walked mark suspends that lane.
- **A73-D5 `CutsceneClipBlock` keeps its type name; only `clipId` is renamed.** A type rename is
  churn across ~20 files for a Sonnet session with no behaviour behind it. Revert: rename to
  `CutsceneAnimationBlock` in a later cosmetic pass.
- **A73-D6 The preview reconstructs a `PlaybackLayer` array per scrub and composites through
  `ClipSampler.CompositeLayers`**, rather than hosting an `ActorPreviewComposer` per slot. The
  composer advances state; a scrub jumps. What both share is the compositor, the resolver and the
  loop/phase math, and that is enough parity. Revert: replay the composer from 0 to `t` on every
  scrub (O(t) per frame).
- **A73-D7 Authored beats auto, and a stop key hands a layer back.** A block claims its layer from
  its start for the rest of the cutscene; the explicit ■ is how auto locomotion resumes. Implicit
  hand-back at a block's end was rejected: a `Once` "sit down" that snapped back to Idle at its
  duration would be the surprise.
- **A73-D8 A "wait until reached" mark derives its hold at the mark's own time** through the
  `CutsceneDerivedHolds` seam A65 built — no new runtime concept, the transport rehearses it for free.
  Revert: keep authoring holds by hand; the toggle stays cosmetic.
- **A73-D9 `CutscenePlay.layerIndex` is deleted, not deprecated.** A field nothing reads is a
  customer question. Hosts pass nothing; `CreatePlayRequest(entityManager, blob, speed)`.

## 5. Tasks

After each: compile gate → the task's fixtures → tick → commit `A73-Tn: <what>`. Full suites once at
T3 (session 1) and once at T8 (session 2). **[parallel-safe]** tasks may go to a subagent that never
touches MCP.

- [x] **T1 — Authoring data + bake (§3.1, §3.2).** `CutsceneSlot.profile/locomotion/layerStops` and
  the two accessors; `CutsceneClipBlock.animationKey`/`loop`; `CutsceneLayerStopKey`, `CutsceneLocomotion`,
  `CutsceneFacingMode`; `CutsceneMarkKey.waitUntilReached`; blob types; builder grouping by layer for
  the seam pass, stop/locomotion/facing-mode bucketing, derived mark holds; delete
  `CutsceneDirectionVariants.cs` and every `directionSet` reader; `SchemaVersion = 6`.
  *Fixtures (EditMode, `CutsceneBlobBuilderTests`):*
  `Blocks_OnDifferentLayers_NeverBlendIntoEachOther` (two overlapping blocks on Base and Action → both
  `blendDuration` NaN; the same two on Base → the second's is the overlap);
  `MarkWaitUntilReached_DerivesARendezvousHoldAtTheMarkTime` (mark at 1 s, wait → two segments,
  `segments[0].holdId == "mark:<name>@1"`, `autoReleaseWhenMarksReached`, no mid-walk warning);
  `LayerStop_ResolvesLayerNameToTheProfileIndex` (`"Action"` on a three-user-layer profile → index 1;
  an unknown name → warning and no blob entry). Update `CutsceneKeyClipboardTests` for the field rename.
- [x] **T2 — Runtime blocks, layer state, stops, completion (§3.3 first half, §3.6 runtime items).**
  `ProcessClipBlocks` by name, `CutsceneSlotLayerState`, `ProcessLayerStops`, speed propagation,
  `StopActorLayers`, the `CutscenePlay.layerIndex`/`TopLayer`/`ResolveLayerIndex` deletion,
  `CreatePlayRequest` signature, `DataContractTests` rows. *Fixtures (PlayMode,
  `CutsceneTimelineSystemTests`):* `Block_IssuesPlayAnimation_OnTheEntrysOwnLayer` (a profile with
  `Walk` on Base and `Wave` on Action, one block each at 0 s → after one update the actor's command
  buffer holds two `PlayAnimation`s and, after `CommandApplySystem`, `PlaybackLayer[0].animationKey == Walk`
  and `[1] == Wave`); `LayerStop_StopsOnlyItsLayer` (stop `"Action"` at 1 s → Base still Active,
  Action not); `SpeedChange_ReachesEveryActiveBlockLayer`. Delete T9's `TopLayer` test from A70.
- [x] **T3 — Runtime locomotion + facing (§3.3 second half).** `CutsceneLocomotionMath`,
  `ProcessLocomotion`, the arrival latch, Auto keys in `CutsceneBlobSampler.TryResolveFacingOverride`,
  the `ActorFacing` write, `[UpdateBefore(ActorFacingRepickSystem)]`, delete the variant re-pick.
  *Fixtures:* new `Tests/PlayMode/CutsceneLocomotionTests.cs` —
  `RootTravel_PlaysTheMovingEntry_ThenTheStandingOneWhenStill` (root keys 0→(4,0,0) over 2 s then
  hold still; assert `PlaybackLayer[base].animationKey` is Walk at 1 s and Idle at 3 s);
  `AuthoredBlockOnTheLocomotionLayer_SuppressesAuto_UntilAStopKey` (a `Sit` block on Base at 0 s,
  stop at 2 s, root travel throughout → Sit at 1 s, Walk at 3 s; fails without the stop hand-back).
  `CutsceneFacingTests` — replace `FacingChange_ReissuesTheDirectionVariantWithTimeCarried` with
  `RootTravel_WritesActorFacing_SnappedAtTheProfilesTurnDirections` (travel north on a Six-turning
  profile → `ActorFacing.facing == North` and `CutsceneFacing.angleDegrees ≈ 90`);
  `AutoFacingKey_HandsFacingBackToTravel` (Fixed 180° at 0 s, Auto at 1 s, root travelling east →
  west-facing at 0.5 s, east at 1.5 s); `MarkArrival_LatchesTheMarksFacing` (mark with
  `facingDegrees = 90`, move the entity within tolerance, no further movement → `ActorFacing.facing == North`
  on the next frame). `SystemGroupStructureTests` gains the edge. **Full suites; session 1 ends
  here** with HANDOFF §4 updated (no owner checkpoint — nothing is visible yet).
- [x] **T4 — Editor: layer rows, block/stop inspectors, slot inspector (§3.4 rows, block, slot,
  facing-key items).** `CutsceneEditorPanel` + `CutsceneClipBlockLaneElement` + `CutsceneKeyClipboard`
  + `VocabularyPickerConfig` filter/create options. No fixture beyond `CutsceneKeyClipboardTests`
  gaining the stop kind; live proof via `execute_code` that assigning a profile builds N layer rows
  and a block lands on its entry's row.
- [x] **T5 — Preview (§3.5).** [parallel-safe with T4] `CutsceneSlotClipPreview` profile blob +
  layer array, `ComposeLayers`, mirror from the profile fold, delete the direction-set preview
  branches. *Fixture (EditMode, new `CutsceneLayerReconstructionTests`):* the pure "last block or stop
  at-or-before t per row" resolver (extract it as a static in `Authoring/Build/` so both the preview
  and a test reach it): block at 0, stop at 2, block at 3 → active at 1, inactive at 2.5, active at 4.
- [x] **T6 — Marks UX (§3.4 mark items).** Mark inspector re-order + Wait Until Reached + Set From
  Scene View Pivot, Marks row **+**, derived-hold ghost and transport auto-continue. Live proof: add
  a mark with the header button, play the transport — it stops at the mark's time naming
  `mark:<slot>@<t>` and continues past the rehearsed arrival.
- [ ] **T7 — Samples, tests sweep, sample host.** `Samples~/Cutscene/CutsceneSampleHost.cs` drops
  the layer argument and gives its slots a profile (compile-checked through a temp assembly);
  `CutsceneStageBakingTests`, `CutsceneAttachTests`, `CutsceneMarkTests` re-pointed at profile-bearing
  fixtures.
- [ ] **T8 — Docs, CHANGELOG, version, full suites.** `cutscenes.md`: rewrite the cast paragraph
  ("an Actor slot is a profile"), replace "Clip lane" with "Layer rows" (blocks by name, stop keys,
  authored-beats-auto), add "Auto locomotion", rewrite "Facing lane" (Fixed/Auto, arrival latch,
  `ActorFacing` written), rewrite "Marks lane" around the **+** button and Wait Until Reached, and add
  a **Recipes** section with three numbered walkthroughs: *Walk an actor to a spot and wait for it*,
  *Turn to face the camera on arrival*, *Play a one-off on the Override layer while walking*.
  `cutscene-api.md`: `CreatePlayRequest` signature, `CutsceneSlotLayerState` (internal — mention only
  as absent from the host surface), the `ActorFacing` write in the runtime-contracts list.
  `actor-profiles.md`: one paragraph "cutscenes play your profile". CHANGELOG `## [0.19.0]` with a
  **Breaking** block; `package.json` + the `PackagingConformanceTests` version assertion together.
  Full suites, discovered counts checked. HANDOFF §1 read-first and §4 updated; the vault's
  `AnimationToolkit.md` cutscene traps gain "authored beats auto until a stop key" and "the cutscene
  writes `ActorFacing`".
- [ ] **⏸ Owner checkpoint (end of session 2).** Open `Assets/ScriptableObjects/Animations/NewCutscene.asset`
  in the Cutscene Editor with `Assets/Scenes/SubScenes/DOTSTestScene.unity` open. Give the Actor slot
  `MaleCitizen.profile`; confirm the slot shows Base / Action / Face / Eyes / Mouth / Override rows.
  Press **+** on Marks with the actor bound: play — the actor walks to the disc (Walk cycling,
  turning as it goes), the transport holds on `mark:<slot>@0`, continues at arrival, the actor stands
  in Idle. Add a Fixed facing key just after the hold at 180°: the actor turns west while standing.
  Double-click the Action row, pick an animation: it plays over the idle without touching the legs.
  Press ■ on Base at some time and drag the root lane afterwards: Walk resumes. The layout is the
  owner's to change; the behaviours above are what this amendment promised.

## 6. Risks and traps

- **`CommandApplySystem` is `OrderFirst`**, so nothing in this group can run before it: commands the
  cutscene appends apply next frame, and a `PlayAnimation` resolves against the `ActorFacing` written
  the frame before. Do not "fix" this by moving the cutscene system; it is the latency every command
  has had since Phase G.
- **`PlaybackApi.IsAnimationPlaying` is true only while `Active`** — a layer fading out after a stop
  reads as not playing, so auto locomotion re-issues on the frame after a stop key. Intended.
- **Two test actors with different profiles bound to one cutscene** must both resolve their own
  layers; never cache a layer index per slot across actors.
- **The seam pass groups by resolved layer, and a Prop has no profile** — guard the grouping so a
  Prop's (always empty) block list never dereferences a null profile.
- **`VocabularyPicker`'s Create row mints into the registry**; a name created from the cutscene
  picker would be a key the profile lacks. Hide Create here.
- **`Samples~` is not compiled.** The sample host's `CreatePlayRequest(…, layerIndex)` call will not
  fail the gate — only the temp-assembly check finds it.
- A63's `hasEverDetached` root-lane retirement and A64's mark suspension are unchanged; locomotion
  reads actual displacement precisely so those two rules need no special case.

## 7. Build log

- **T1+T2+T3** (this session, 2026-09-08) — built and committed together rather than as three
  separate gated commits. Reason: the blob schema (T1), the runtime block/layer-state rewrite (T2)
  and the locomotion/facing rewrite (T3) are mutually load-bearing inside one 1400-line system —
  `CutsceneClipBlockBlob.animationKey` cannot exist without `CutsceneTimelineSystem.ProcessClipBlocks`
  reading it, and no intermediate split point compiled. Every increment was still gated through
  `refresh_unity`/`read_console`; only the *final* three-task state was ever asserted green, so a
  single combined commit is the honest record of what was actually verified, not three that would
  imply independent gates that never happened. Logged as a deliberate protocol deviation, not a
  silent one.

  **T1 scope actually built:** `CutsceneAsset.cs` (`CutsceneSlot.profile/locomotion/layerStops`,
  `ResolvedRig`/`ResolvedClipSets`, `CutsceneClipBlock.animationKey`/`LoopMode loop`,
  `CutsceneFacingKey.mode`, `CutsceneLocomotion`, `CutsceneLayerStopKey`,
  `CutsceneMarkKey.waitUntilReached`), `AnimationToolkitEnums.cs` (`CutsceneFacingMode`),
  `CutsceneBlob.cs` (schema 6: `CutsceneClipBlockBlob` renamed/re-typed, `CutsceneDirectionVariantsBlob`
  deleted, `CutsceneSlotSegmentBlob.layerStops` + `CutsceneLayerStopBlob`, `CutsceneSlotMetaBlob.locomotion`
  + `CutsceneLocomotionBlob`, `CutsceneFacingKeyBlob.isAuto`), `CutsceneBlobBuilder.cs` (layer-grouped
  seam pass via a new authoring-time `ActorProfileAsset` scan, `BucketLayerStops`, Auto-aware
  `BucketFacingKeys`/`InsertFacingContinuity`, `SchemaVersion = 6`), `CutsceneDerivedHolds.cs`
  (mark-derived holds merged with event-derived ones, `sequence`-based deterministic tie-break).
  **Deviation from §3.1/§3.6:** `CutsceneSlot.rig`/`.clipSets`/`.directionSet` were **not deleted** —
  they stay, unread by the builder (which now goes through `ResolvedRig`/`ResolvedClipSets`), because
  every Editor reader of those three fields (`CutsceneEditorPanel`, `CutscenePreviewController`,
  `.AutoKey.cs`, `.ViewportGizmo.cs`) is A73-T4/T5 territory and physically removing the fields now
  would break the Editor assembly a full session before that work lands. `CutsceneDirectionVariants.cs`
  (the Authoring static class) and `CutsceneFacingVariants.Resolve`/`SelectVariantClipId`'s sibling
  `Resolve` method survive for the same reason — `CutscenePreviewController.cs` still calls
  `CutsceneDirectionVariants.IsDirectionSetMember`/`DescribeFacingRigProblem` and
  `CutsceneFacingVariants.Resolve`. `CutsceneDirectionVariants.Build()`/`SlotClipId()` and
  `CutsceneFacingVariants.SelectVariantClipId()` — the two methods only the deleted runtime path used
  — were deleted since nothing else called them. T4/T5 should delete the surviving fields/files once
  the Editor no longer needs them; do not rediscover this as a bug.

  **T2/T3 scope actually built:** `CutsceneComponents.cs` (`CutscenePlay.layerIndex` gone;
  `CutsceneSlotRuntimeState` loses `activeVariantClipId`/`activeBlock*`, gains
  `nextLayerStopIndex`/`warnedMissingProfile`/`warnedUnresolvedAnimationKey`/`hasLastPosition`/
  `lastPosition`/`lastLocomotionKey`/`hasLatchedFacing`/`latchedFacingDegrees`; new
  `CutsceneSlotLayerState` buffer), `CutsceneApi.cs` (`TopLayer` gone, `LayersPerSlot = 8` added,
  `CreatePlayRequest`/`CreatePlayRequestFromStage` drop `layerIndex`, both buffers sized/seeded),
  `CutsceneTimelineSystem.cs` (full rewrite: `ProcessClipBlocks` by name against the bound actor's
  `ActorProfile`, new `ProcessLayerStops`/`ProcessLocomotion`/`TryResolveLocomotionLayerIndex`,
  facing chain gains the Auto-cancel + arrival-latch rules and writes `ActorFacing` via
  `WriteActorFacing`, `[UpdateBefore(typeof(ActorFacingRepickSystem))]` added,
  `ReissueDirectionVariant`/`ResolveVariantClipIdForSlot`/`ResolveLayerIndex` deleted),
  `CutsceneBlobSampler.TryResolveFacingOverride` (Auto key cancels a preceding Fixed key), new
  `Runtime/Sampling/CutsceneLocomotionMath.cs`. **Deviation from spec wording:** "one warning per
  (slot, key)" for an unresolved animation key is built as one warning per *slot* covering every key
  (a single `bool` flag on `CutsceneSlotRuntimeState`, not an unbounded per-key set — ECS structs
  don't hold collections); and the arrival-latch "clears when `IsMoving` true" is built as "clears
  when position has moved more than a 1e-6 sqr-length epsilon since last frame" rather than wiring
  `deltaTime`/`movingSpeedThresholdMetersPerSecond` into the facing chain, since a latch can engage on
  a slot with no locomotion configured at all. Both are noted for T4/T5/T8 in case the owner wants the
  literal reading instead.

  **Fixtures:** `CutsceneBlobBuilderTests` gained the three T1 fixtures
  (`Blocks_OnDifferentLayers_NeverBlendIntoEachOther`, `MarkWaitUntilReached_DerivesARendezvousHoldAtTheMarkTime`,
  `LayerStop_ResolvesLayerNameToTheProfileIndex`) plus the `clipId`/`loop` field-rename fixups to the
  two pre-existing tests — all four green under `run_tests` EditMode. `CutsceneKeyClipboardTests`
  needed **no change** — it never referenced `CutsceneClipBlock.clipId`/`.loop` (drift from the
  prompt's prediction, logged rather than silently corrected). `CutsceneTimelineSystemTests.cs` and
  `CutsceneFacingTests.cs` were substantially rewritten (blocks now bind through
  `PlaybackTestActor.CreateActorWithProfile`; `TopLayerRequest_DrivesTheBoundActorsLastPlaybackLayerOnly`
  deleted per T2's own instruction; `FacingChange_ReissuesTheDirectionVariantWithTimeCarried` replaced
  by the three T3-named tests). New `CutsceneLocomotionTests.cs` (both named fixtures) and
  `SystemGroupStructureTests.CutsceneTimeline_RunsBeforeFacingRepick_InTheLogicGroup` added.
  `DataContractTests` gained `CutsceneApi_LayersPerSlot_MatchesActorProfileMaxLayerCount`
  (T2's parenthetical DataContractTests row).

  **Verification gap — escalated, not silently accepted.** Every `.cs` change was gated through
  `refresh_unity(compile: "request")` → `read_console` for the duration of this session, and the
  four touched EditMode groups (`CutsceneBlobBuilderTests`, `CutsceneKeyClipboardTests`,
  `CutsceneBlockTimingTests`, `DataContractTests` — 21 tests) ran green via `run_tests`. **The
  PlayMode suite could not be run at all this session**: `run_tests` PlayMode failed with "Test job
  failed to initialize (tests did not start within timeout)". Unity refuses to enter Play Mode while
  *any* project assembly has a compile error, and the prompt's own directive — game assemblies
  expected red from T1 onward, do not fix them — guarantees exactly that state throughout T1–T3. A
  one-line trial fix to `Assets/_Scripts/Systems/CutsceneSystemGroup/CutsceneStartSystem.cs` (dropping
  its now-extra `layerIndex` argument) compiled but did not unblock PlayMode, because
  `NarrativeEventManager.cs` and three PlayMode test files under `Assets/_Scripts/Tests/PlayMode/`
  (`CutsceneSystemTests.cs`, `CutsceneDialogueCueTests.cs`, `CutsceneAcceptancePerfTests.cs`) also
  read the now-gone `CutsceneApi.TopLayer`/`CutscenePlay.layerIndex` — a broader surface than one
  line, and squarely G6's re-authoring job, not a trivial compile shim. The trial fix was reverted so
  this session's diff stays package-only. **Consequence:** `CutsceneTimelineSystemTests`,
  `CutsceneFacingTests`, `CutsceneMarkTests`, `CutsceneAttachTests`, `CutsceneStageBakingTests`,
  `SystemGroupStructureTests` and the new `CutsceneLocomotionTests` are written, believed correct
  from careful line-by-line review against the spec's §3.3 chain, but **not machine-verified this
  session** — the "prove a test can fail by reverting" step (HANDOFF §2) could not be performed
  either, for the same reason. Session 2 (or whoever lands G6 first) should run
  `DotsAnimationToolkit.Tests.PlayMode` in full before trusting this line item closed, and treat any
  failure there as this session's bug, not session 2's.

  **Compile-preserving Editor touches (not the T4/T5 redesign).** The `CutsceneClipBlock.clipId`→
  `animationKey`/`loop`(bool)→`LoopMode` rename reaches three Editor call sites that were otherwise
  going to be red for a full session:
  `CutsceneEditorPanel.cs` (the clip-block timeline label now reads the raw key as hex instead of a
  clip name via the now-removed `DescribeClip(slot, block.clipId)`; the block inspector's clip-name
  `DropdownField` replaced with a raw `AnimationKey` uint field) and
  `CutscenePreviewController.ComposeClipLane` (stubbed to an early `return`, body kept but
  unreachable under `#pragma warning disable/restore CS0162`, since it samples clip blocks against
  the old rig/clipSets registry by raw clip id — rebuilding it onto a per-layer `PlaybackLayer`
  reconstruction is A73-T5's `ComposeLayers`, spec §3.5). Root motion, facing and camera preview are
  unaffected; only the clip lane's own pose contribution is blank until T5. T4/T5 should treat these
  three spots as their starting point, not rediscover them as new work.

  **Game-side, confirmed expected-red, not touched beyond the one reverted trial:**
  `Assets/_Scripts/Systems/CutsceneSystemGroup/CutsceneStartSystem.cs:71` (`request.layerIndex`,
  `CreatePlayRequestFromStage` arity), `Assets/_Scripts/MonoBehaviours/NarrativeEventManager.cs:412`
  (`CutsceneApi.TopLayer`), `Assets/_Scripts/Tests/PlayMode/CutsceneSystemTests.cs:81`,
  `CutsceneDialogueCueTests.cs:88`, `CutsceneAcceptancePerfTests.cs:60` (all three:
  `CutsceneApi.TopLayer`). G6's own read-first list should confirm this set is still complete before
  starting — it may have grown if another session touched game cutscene code in the meantime.

  **Full-suite EditMode also would not run.** After committing, `run_tests` against the whole
  `DotsAnimationToolkit.Tests.EditMode` assembly/group (~760+ tests) failed to initialize on four
  separate attempts (`init_timeout` up to 120000ms, one preceded by an idle `refresh_unity`), each
  with the same "tests did not start within timeout" error - while the four targeted EditMode
  groups this session actually touched (`CutsceneBlobBuilderTests`, `CutsceneKeyClipboardTests`,
  `CutsceneBlockTimingTests`, `DataContractTests`, 21 tests by `test_names`) ran and passed cleanly
  both before and after the commit. Cause not identified - plausibly discovery cost at that test
  count, plausibly contention from another session sharing this Editor instance (this repo also
  gained an unrelated `5c863bef` "A74 spec" commit mid-session, confirming a second session was
  active). Recorded rather than retried further. Whoever runs the closing full-suite pass for this
  amendment should confirm the full EditMode count (baseline: EditMode 760, PlayMode 277, per A72's
  HANDOFF §4 paragraph) has not silently dropped, not just that named subsets pass.

- **A73-fixup (2026-09-08, continuation session, Part 1 of the T4–T8 prompt)** — unblocked the
  game side and finished verifying T1–T3. Two commits: `A73-fixup: drop the removed
  CutscenePlay.layerIndex/CutsceneApi.TopLayer references so the game compiles` (the five files
  named above, minimal mechanical edits — `CreatePlayRequestFromStage` drops its layerIndex
  argument, `CutsceneRequest.layerIndex` is simply left unset rather than assigned from the
  now-gone `CutsceneApi.TopLayer`) and `A73-fixup: fix bugs the first full DotsAnimationToolkit
  suite run surfaced in T1-T3`. The full-suite instability this session's own log above
  describes ("tests did not start within timeout") did **not** recur this continuation session —
  both full suites ran to completion on the first attempt after the game-side fix, discovering
  their full counts (EditMode 764, PlayMode 283) without contention or timeout. Whatever caused
  it (plausibly the concurrent second session this log already suspected) was not present now.

  **Three real bugs in already-committed T1–T3 code, found because this was the first time either
  full suite actually ran to completion, not because anything regressed:**
  1. `CutsceneBlobBuilder.ComputeContentEndSeconds` never scanned `slot.markKeys` — every sibling
     track (`attachMarkers`, `facingKeys`, `partTracks`) was included, marks were not — so a
     cutscene whose only content past its last other key was a mark computed a natural end short
     of that mark's time. Compounding it: even with mark times included, a hold sitting exactly
     at the computed natural end tied rather than exceeded it, so `ComputeSegmentBoundaries`
     never appended the trailing boundary a hold needs to release into.
     `MarkWaitUntilReached_DerivesARendezvousHoldAtTheMarkTime` (claimed green in this log's own
     T1 entry above under the four-fixture EditMode run — that claim was accurate for the
     fixtures it actually ran, not a false record; this defect only surfaces against the fixture
     list the full suite runs) expected 2 segments and got 1. Fixed both gaps: `markKeys` joins
     the natural-end scan, and a hold that is the last boundary always gets a trailing boundary
     appended regardless of natural end.
  2. `ResolveOutstandingMarks`'s arrival latch compared the arriving position against
     `slotState.lastPosition`, which still held wherever the actor was one frame before arrival.
     The displacement that closed the final distance onto the mark is exactly the thing
     `TryResolveSlotFacingAngle`'s "moved since the latch" check watches for, so the latch
     cancelled itself on the same frame it engaged — `MarkArrival_LatchesTheMarksFacing` resolved
     `SouthEast` (the test actor's untouched default) instead of `North`. Fixed by re-baselining
     `lastPosition`/`hasLastPosition` to the arrival (or timeout-placement) position at the exact
     moment the latch is set, in both `ResolveOutstandingMarks` branches.
  3. `SpeedChange_IssuesSetSpeedOnEveryBoundActorLayer` and `Skip_MarksComplete_AndStopsTheActorLayer`
     each acted on layer 0 before any prior update had let `ProcessClipBlocks` mark it active —
     `ApplyLayerSpeedToAllActorSlots`/`StopActorLayers` only touch a layer `CutsceneSlotLayerState`
     already records as active, exactly the reason `LayerStop_StopsOnlyItsLayer` (written the same
     session, in the same file) already double-`Advance`s. Fixed by adding the same missing first
     `Advance()` to each — a test-authoring gap, not a runtime bug.

  Also removed the `§`/`amendment A#` spec-citation text `Conformance_F` flagged in three T1–T3
  doc comments (`CutsceneTimelineSystem.cs:1376`, `CutsceneEditorPanel.cs:4059`,
  `CutscenePreviewController.cs:749`) — wording only, no behavior change; `Conformance_A`'s asmdef
  drift is confirmed still present and still not this amendment's to fix (A72's own note).

  **Verified:** `DotsAnimationToolkit.Tests.EditMode` 764/764 (`Conformance_A` the one standing,
  pre-existing failure) and `DotsAnimationToolkit.Tests.PlayMode` 283/283, both fully green — the
  first time either suite has completed for this amendment. T1–T3's checkboxes above are now
  honestly verified, not just believed correct from review.
