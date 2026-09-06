# Amendment A65 — Holding Events, Runtime Facing, Block Playback Controls

> **Status:** ✅ spec, not built. Written 2026-09-04.
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
- [ ] **T3 — Runtime facing + variants (§3.2).** Tests (PlayMode, new `CutsceneFacingTests.cs`): `RootTravel_WritesCutsceneFacingAngle` (root x: 0→10 over 2 s → angle ≈ east per this package's convention — read `FacingResolver` to get the exact degrees); `FacingChange_ReissuesTheDirectionVariantWithTimeCarried` (hand-built blob with variants, root travelling east then west; assert a second Play with the west/mirror variant id and a `SetTime` after it).
- [ ] **T4 — Block speed/offset (§3.3).** Test (PlayMode): `BlockSpeedAndOffset_ReachThePlayCommand` (speed 0.5, offset 0.25 → Play speed 0.5, SetTime 0.25). Extend `CutsceneBlockTimingTests` for the new `ClipTimeInBlock` arithmetic.
- [ ] **T5 — Docs.** `cutscenes.md`: "Events that hold", "Facing at runtime" (delete the Known-gaps line), "Block speed and offset". CHANGELOG, HANDOFF §4.
- [ ] **⏸ Owner checkpoint.** Editor: a walk block on a slot with a direction set, root keys east then west, an event marked *hold* at 3 s. Play in the transport: the actor mirrors at the turn, the transport stops at 3 s naming the event, Continue resumes.

## 6. Risks and traps

- `SetTime` after `Play` in the same frame depends on `CommandApplySystem` processing the buffer in order — verify in that system before relying on it; if it sorts or dedups, issue `SetTime` on the next frame from slot state.
- The finite-difference facing at `t − 1/60` is undefined at `t == 0`; use `t + 1/60` there (forward difference).
- `FindName` on the registry is editor-time only (Authoring reads `ProjectSettings/`); the runtime never resolves names — the baked `FixedString64Bytes` is the contract.

## 7. Build log
