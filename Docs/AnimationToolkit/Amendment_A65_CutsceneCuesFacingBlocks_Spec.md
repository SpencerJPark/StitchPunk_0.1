# Amendment A65 — Holding Events, Runtime Facing, Block Playback Controls

> **Status:** ✅ **T1–T5 built and gated 2026-09-06**, stopped at the ⏸ owner checkpoint. Written 2026-09-04.
> **Roadmap:** `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` — read its §4 protocol first.
> **Depends on:** A64 (schema 4). **Parallel-safe with:** nothing package-side; G1 game work may run beside it.
> **Session budget:** one Sonnet session. Three independent features; commit each.

## 1. Owner product calls (2026-09-04)

1. **Dialogue is a first-class lane.** A cue starts a dialogue and holds the clock until it ends, without an author placing an event *and* a hold marker and matching ids by hand.
2. **Facing at runtime** was a recorded gap since Phase G. For a sprite game, an actor walking left on root keys must face left in play, not only in the preview.
3. Clip blocks need **speed** and a **start offset** ("play the second half of the swing, slowed").

## 2. Read first

- `Authoring/Assets/CutsceneAsset.cs` (`CutsceneEventMarker`, `CutsceneClipBlock`), `Authoring/Build/CutsceneBlobBuilder.cs` (`ComputeSegmentBoundaries`, `AssignToSegment`, `BucketEvents`, `BucketClipBlocks`), `Authoring/Assets/IVocabularyRegistry.cs` (`FindName(uint)`), `VocabularyRegistryProvider` (grep — `AnimEventKeys` accessor).
- `Runtime/Systems/CutsceneTimelineSystem.cs`, `Runtime/Sampling/CutsceneBlobSampler.cs`, `Runtime/Sampling/CutsceneBlockTiming.cs`, `Runtime/Sampling/FacingResolver.cs` (`FromMovement`, `Snap`, `ToAuthoredSide`), `Runtime/Components/PartComponents.cs` (`PartFacing` — read only, the toolkit does not write it here), `Runtime/Api/AnimationCommandUtil.cs` (`SetTime`), `Runtime/Components/PlaybackLayer.cs` (`time`).
- `Editor/ClipEditor/Cutscene/CutscenePreviewController.cs` lines ~470–560: `ResolveSlotFacing`, `ResolveFacingVariantClipId`, `IsDirectionSetMember` — the exact chain the runtime must reproduce. `Editor/ClipEditor/DirectionSets/DirectionSetsPanel.cs` `SetContextProvider` — the host-seam registration pattern to copy. `Editor/ClipEditor/Cutscene/CutsceneEditorPanel.cs` `BuildEventInspector`, `BuildClipBlockInspector`, `BuildHoldRows`, `FindFirstHoldCrossed`.
- `Authoring/Assets/DirectionSetAsset.cs` — the five east-side slots and `TryGetEffectiveDirections`.

## 3. Design

### 3.1 Holding events

`CutsceneEventMarker.holdUntilReleased : bool = false`.

**Bake:** every holding event contributes a segment boundary at its time whose `holdId` is the event key's registry name (`VocabularyRegistryProvider.AnimEventKeys.FindName(eventKey)`; unresolved → `"event:" + key.ToString("X8")` and a warning). The event marker itself is bucketed into the segment that **ends** at its time (`AssignToSegment` gains an `inclusiveEnd` parameter used only here) at `time == duration`, so `ProcessEvents` fires it on the frame the segment completes and the hold engages the same frame — the host sees the event, starts its thing, and later releases the hold by name. Two holding events at the same instant → one boundary, both events fire. A holding event and an authored hold at the same instant → one boundary; the authored hold's id wins and a warning says so.

**Runtime:** unchanged mechanics. Add `CutscenePlaybackApi.TryGetCurrentHoldId(EntityManager, Entity request, out FixedString64Bytes holdId)` (false when not paused on a hold) so a host does not reach into the blob.

**Editor:** the event inspector gains the toggle; a holding event's marker draws with the hold glyph overlaid (USS class `--holding`). `FindFirstHoldCrossed` and the hold-row rendering include derived holds (read-only ghost markers on the Holds row, not selectable — they are the event). The transport's hold banner names the derived id.

**Host inspector seam:** a holding "Dialogue" event's payload (`intParam` = sequence id in Stitch Punk) is meaningless as a raw int. Add in `Editor/ClipEditor/Cutscene/CutsceneEventInspectorProviders.cs`:

```csharp
public interface ICutsceneEventInspectorProvider
{
    /// Return true and fill container with fields bound to markerProperty ("intParam"/"floatParam")
    /// when this provider owns eventKey; false leaves the default int/float fields in place.
    bool TryBuildInspector(uint eventKey, SerializedProperty markerProperty, VisualElement container);
}
public static class CutsceneEventInspectorProviders { public static void Register(ICutsceneEventInspectorProvider provider); /* + Unregister, + internal TryBuild */ }
```

`BuildEventInspector` asks the registered providers first. Registration is the host's `[InitializeOnLoadMethod]` (same as `DirectionSetsPanel.SetContextProvider`).

### 3.2 Runtime facing

Move `TryResolveFacingAngle` from `CutsceneKeySampler` (Authoring) into `CutsceneBlobSampler` (Runtime) as the blob twin: last facing key at-or-before `t`, else finite difference of the root lane at `t` and `t − 1/60` (clamped to the segment; A62's boundary keys make the segment self-contained), else false.

New component:

```csharp
/// Written every frame on a bound Actor root while a cutscene drives it. The host maps it onto
/// whatever its own facing model is (mirror, view offset, direction enum). Disabled at completion.
public struct CutsceneFacing : IComponentData, IEnableableComponent { public float angleDegrees; }
```

`CutsceneTimelineSystem`: on the first frame a slot is processed, queue a pending op to add `CutsceneFacing` (disabled) if absent; each frame write the resolved angle and enable; on completion/skip disable. Slots without facing keys *and* without root keys write nothing (component stays disabled).

**Direction-variant re-pick.** `CutsceneClipBlockBlob.directionVariants : CutsceneDirectionVariantsBlob { bool hasVariants; ulong east, northEast, north, southEast, south; AnimationDirections effectiveDirections; }` baked from the slot's `DirectionSetAsset` when the block's clip is a member of the set (move `IsDirectionSetMember`/the variant table logic from the preview controller into `Authoring/Build/CutsceneDirectionVariants.cs` and call it from both). Runtime: per slot, `CutsceneSlotRuntimeState.activeVariantClipId`; each frame, with a resolved angle and an active block that has variants, run the preview's exact chain (`FacingResolver.FromMovement` at the set's granularity → `Snap` to `effectiveDirections` → `ToAuthoredSide`) to a variant clip id; if it differs from `activeVariantClipId`: append `Play { clip = variant, blendDuration = 0, loop, speed }` then `SetTime { time = PlaybackLayer[layerIndex].time }` (read the layer buffer first; `CommandApplySystem` drains in order) and store. `mirrorX` from `ToAuthoredSide` is **not** applied by the toolkit (A65-D2).

### 3.3 Block speed and start offset

`CutsceneClipBlock.speed = 1f`, `CutsceneClipBlock.clipStartOffsetSeconds = 0f`; blob fields `speed`, `clipStartOffset`; `SchemaVersion = 5`. Runtime `Play` uses `speed = block.speed * effectiveLayerSpeed` (A62 §3.5) followed by `SetTime { time = clipStartOffset }` when the offset is non-zero. `CutsceneBlockTiming.ClipTimeInBlock(blockStart, timeSeconds, speed, clipStartOffset)` = `offset + max(0, t − start) * speed` — the one copy; the preview's phase comes from it. A62's `SetSpeed` propagation must multiply: on a speed change, each slot's active block speed × new effective speed (track the active block's speed in slot state).

Editor: block inspector gains Speed and Start Offset fields.

## 4. Decisions

- **A65-D1** A holding event is baked as a hold whose id is the event's *name*. No new runtime concept; `CutsceneHoldRelease` by name is the whole host contract, and the editor's Continue rehearses it.
- **A65-D2** The toolkit writes `CutsceneFacing` (an angle) and re-picks direction variants; it never writes `PartFacing`. Hosts already own a facing system that writes `PartFacing` on every part (Stitch Punk's `UnitFacingSystem`); two writers would fight.
- **A65-D3** Variant re-pick issues `Play(blend 0)` + `SetTime` rather than a new "swap clip" command kind — no new command semantics, and the preview already samples this way.

## 5. Tasks

- [x] **T1 — Holding events: data, bake, API, editor toggle and ghost markers (§3.1).** Test (EditMode, `CutsceneBlobBuilderTests.cs`): `HoldingEvent_BakesABoundaryNamedAfterTheEvent_AndFiresBeforeIt` — one holding event at 2 s → two segments, `segments[0].holdId == <name>`, `segments[0].events[0].time == 2f`, `segments[1].events.Length == 0`. Test (PlayMode): `HoldingEvent_FiresThenPausesUntilReleasedByName`.
- [x] **T2 — Event inspector provider seam.** No fixture. Live proof: register a dummy provider from `execute_code`, select an event with its key, the container holds the provider's field.
- [x] **T3 — Runtime facing + variants (§3.2).** Tests (PlayMode, new `CutsceneFacingTests.cs`): `RootTravel_WritesCutsceneFacingAngle` (root x: 0→10 over 2 s → angle ≈ east per this package's convention — read `FacingResolver` to get the exact degrees); `FacingChange_ReissuesTheDirectionVariantWithTimeCarried` (hand-built blob with variants, root travelling east then west; assert a second Play with the west/mirror variant id and a `SetTime` after it).
- [x] **T4 — Block speed/offset (§3.3).** Test (PlayMode): `BlockSpeedAndOffset_ReachThePlayCommand` (speed 0.5, offset 0.25 → Play speed 0.5, SetTime 0.25). Extend `CutsceneBlockTimingTests` for the new `ClipTimeInBlock` arithmetic.
- [x] **T5 — Docs.** `cutscenes.md`: "Events that hold", "Facing at runtime" (delete the Known-gaps line), "Block speed and offset". CHANGELOG, HANDOFF §4.
- [ ] **⏸ Owner checkpoint.** Editor: a walk block on a slot with a direction set, root keys east then west, an event marked *hold* at 3 s. Play in the transport: the actor mirrors at the turn, the transport stops at 3 s naming the event, Continue resumes.

## 6. Risks and traps

- `SetTime` after `Play` in the same frame depends on `CommandApplySystem` processing the buffer in order — verify in that system before relying on it; if it sorts or dedups, issue `SetTime` on the next frame from slot state.
- The finite-difference facing at `t − 1/60` is undefined at `t == 0`; use `t + 1/60` there (forward difference).
- `FindName` on the registry is editor-time only (Authoring reads `ProjectSettings/`); the runtime never resolves names — the baked `FixedString64Bytes` is the contract.

## 7. Build log

**Built 2026-09-06.** T1–T5 in order, one commit each, every new fixture watched failing with its
fix reverted. Blob schema is **5**, stamped in T3 (the first task that changed the layout), not T4.

**A65-D4 — facing while a mark is outstanding.** §3.2 resolves facing by finite-differencing the
root lane, but A64 *suspends* that lane while a slot is walking to a mark (the host owns the
transform), so the lane says where the rehearsal would have put the actor rather than where it is
going — stale exactly when the actor is walking and most needs to face right. Decided: while the
order is outstanding, facing derives from the vector from the entity to the mark. It is what the
actor is actually doing, it costs no new state (the position and the order are already read a few
lines away), and an explicit facing override key still wins over both branches.

**Defect found: the derived facing angle was in the wrong convention.** `TryResolveFacingAngle`
returned `atan2(delta.x, delta.z)` — a compass bearing measured from +Z, the `LocalTransform`
Y-euler convention — while every consumer (`DirectionSetsPanel`, `CutscenePreviewController`) turns
that angle back into a vector with `(cos, sin)`, i.e. measured from +X. The two are a reflection
about 45°, so an actor walking **east** resolved as facing **north**, and with a Two-coverage set an
east→west turn produced no mirror at all — the exact failure §1.2 exists to fix, and it would have
made this amendment's own checkpoint look broken. Both twins now measure from +X toward +Z;
`CutsceneFacing.angleDegrees` documents that model against the Y-euler one it is not. Authored
facing keys and mark facings were unaffected: a mark's `facingDegrees` really is a world Y rotation
(`PlaceAtMark` feeds it to `quaternion.RotateY`) and is left alone.

**§3.2's "move" was an add, and parity is stronger than the spec asked for.**
`CutsceneKeySampler.TryResolveFacingAngle` stays (the preview calls it, with A64's merged root
lane); `CutsceneBlobSampler` gained the blob twin. Rather than copy the resolve *chain* into the
runtime, the chain itself moved into `Runtime/Sampling/CutsceneFacingVariants.Resolve`, which the
preview controller now calls too — the variant *table* is built by `Authoring/Build/
CutsceneDirectionVariants` for the bake and the preview alike, the `CutsceneMarkMerge` shape. Two
twin signatures were also reconciled: the authoring `TryResolveFacingAngle` used to return
`true` only for an override key, which read as "resolved" at the runtime twin's call sites; it now
means "resolved by either path", and the panel's override readout asks the new
`TryResolveFacingOverride` instead.

**`CutsceneDirectionVariantsBlob` carries `targetDirections` as well as `effectiveDirections`.** The
chain the spec names quantizes at the actor's own turn granularity *before* folding onto the set's
coverage, so reproducing it needs both counts; the struct in §3.2 lists only the second.

**The bake cannot call `VocabularyRegistryProvider.AnimEventKeys.FindName` (§3.1).** That type is in
the Editor assembly and `Authoring/` may not name `UnityEditor` — Conformance_C scans raw file text.
The registry reaches the builder through `CutsceneDerivedHolds.EventNameRegistrySource`, a lazy
accessor the Editor assembly publishes from an `[InitializeOnLoadMethod]`, the same host-seam shape
as `DirectionSetsPanel.SetContextProvider`. Unresolved keys still fall back to `event:XXXXXXXX` with
a warning.

**T3's variant fixture turns east→north, not east→west.** Every direction set is mirror-closed, so a
west-side facing is served by the *same* clip with `mirrorX` — an east→west turn changes the mirror
flag, never the clip id, and cannot assert a second `Play`. The test turns onto a facing the set
serves with a different row instead, which is what a re-pick actually is.

**`CutsceneFacingVariants.SelectVariantClipId` is not `[BurstCompile]`.** The blob it reads carries a
`bool`, which is not blittable across a Burst entry point (BC1063), and both callers are managed.
`Resolve` and `AngleDegreesFromTravel` take primitives and stay compiled.

**`SeamBlendWeight` no longer routes through `ClipTimeInBlock` (T4).** A crossfade window is timeline
geometry; scaling it by a block's speed would stretch the fade past the overlap it was derived from.
The elapsed-seconds half is now `ElapsedInBlock`, and `ClipTimeInBlock` is offset + elapsed × speed.
A baked `speed` of 0 means "a bake older than schema 5", never a frozen clip: the authored field is
`[Min(0.01f)]`, and `EffectiveBlockSpeed` reads 0 as 1 so pre-A65 blobs and this suite's hand-built
ones keep playing.

**T1's PlayMode fixture bakes its blob**, against this suite's hand-build convention, and says so in
its own remark: the feature *is* the pairing of a bake-time boundary with the runtime's existing
hold mechanics, and a hand-built blob would assert that pairing by writing it out itself. The
EditMode fixture isolates the bake half.

**Owner checkpoint, first pass (2026-09-06): the actor did not turn, and nobody saw the cue.** Two
findings, both outside the code A65 wrote and both now fixed:

1. **`NewRig.asset` had `facesDirection` false on all 16 targets.** That flag is what bakes
   `PartFacing` and what gates the editor's own mirror, so the facing resolved correctly (SouthEast,
   mirrored), the variant was picked, and *nothing turned* — the A37 remark predicted exactly this
   ("the case the owner tested first and found inert") and it happened again. Same shape as A63-T0's
   empty `RigAsset.sockets`: the content declared nothing for the feature to act on.
2. **Opting every part in was still wrong, because this rig is nested.** The mirror negates a part's
   local x scale, so a mirrored part inside a mirrored parent multiplies back to +1. Measured with
   all 16 opted in: Pelvis −1, Torso +1, Neck −1, **BaseHead +1** — the head and its whole face never
   flipped, and the pose was mirrored on every second level only, which is what the owner saw. The
   per-part mirror in `TransformSampleSystem` (and its preview twin) is only correct on a **flat**
   rig, where every part hangs off the actor root. `MaleCitizen` and the checkpoint walker both have
   13 of 16 parts nested under another part.

   Fixed by opting in the top-most part of each chain only — `Pelvis`, `UpperLeftLeg`,
   `UpperRightLeg` — whose subtrees then inherit the reflection exactly once. Verified: at t=1 every
   part reads world scale.x +1, at t=4 every part reads −1 and every world x offset negates, face
   included.

   **Owner rule, same day:** *a mirror point placed on a parent flips that parent and all its
   children, animations included.* The code now enforces it rather than trusting the content:
   `RigTargetBaker` tags a facing part with a facing ancestor `PartMirrorFromAncestor` (decided once,
   where the hierarchy is known), and `TransformSampleSystem` and the preview both skip that part's
   own mirror. `PartFacing` is still added to it, because `viewOffset` — alt-view frames — is a
   separate job a nested part still does for itself, and the suppression is a tag rather than a field
   on `PartFacing` because hosts overwrite that component wholesale every frame. Proven by ticking
   all 16 targets again: 16/16 mirrored walking west, 0/16 walking east, no cancellation anywhere.
   PlayMode fixture `MirroringAPartUnderAMirrorPoint_LeavesItToTheAncestor`, watched failing with the
   gate reverted.

   Both failure modes are now named at bake time and in the slot inspector by one shared check,
   `CutsceneDirectionVariants.DescribeFacingRigProblem`.

3. **The cue's banner was on screen but unreadable in practice.** The transport's status label sat
   at the far right of the row, past Speed / Loop / Skip Holds — 350px from the Continue button it
   describes, in the same grey as everything else. It now sits immediately beside Continue and turns
   bold hold-yellow while the clock is stopped: `⏸ Holding on 'Dialogue' — the event cue fired.`

**Editor flicker check (T1).** The event inspector's `holdUntilReleased` toggle routes through
`CutsceneEditorPanel.ShouldIgnoreBindingEcho`. Sampled the inspector's child `GetHashCode()`s from
two `execute_code` calls seconds apart: identical instances, so the panel is stable rather than
rebuilding every frame.
