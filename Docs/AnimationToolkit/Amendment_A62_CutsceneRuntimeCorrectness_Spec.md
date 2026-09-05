# Amendment A62 — Cutscene Runtime Correctness

> **Status:** ✅ spec, not built. Written 2026-09-04.
> **Roadmap:** `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` — read its §4 protocol first.
> **Depends on:** nothing. **Parallel-safe with:** A61.
> **Session budget:** one Sonnet session. Seven tasks; every one has a fixture that must fail on the old code.

## 1. The defects (verified by reading, 2026-09-04)

1. **Motion into a hold is lost.** `CutsceneBlobBuilder.AssignToSegment` is half-open: a key authored at a hold's exact time belongs to the *next* segment. `CutsceneBlobSampler.SampleTransform` sees only the current segment's keys and holds its last one. Keys A@0, B@2, hold@2 → the runtime sits at A for the whole first segment then snaps to B on release. The editor's `CutscenePoseSampler` samples the flat list and interpolates A→B, so preview and playback disagree — the exact defect the single-sampler rule (`TransformSampleSystem` remarks) exists to prevent. Same for part tracks, camera keys and facing keys.
2. **A slot with no root keys is teleported to the origin** every frame: `SampleTransform` returns `(0, identity, 1)` for an empty array and `CutsceneTimelineSystem.ApplyPose` writes it. `CutscenePoseSampler.Sample` + `CutscenePreviewController.ApplyPose` do the same in the editor.
3. **Crossfade across a hold is lost.** `ProcessClipBlocks` derives `blendDuration` from the previous block *in the same segment*; the first block after a hold is always a hard cut.
4. **Speed and pause never reach the actors.** Blocks are issued at `speed = 1f`; nothing issues `SetSpeed`. `CutsceneControl.paused`'s doc comment ("nothing advances") is false — the actor's layer keeps playing.
5. **Blocks at a segment's t=0 are issued one frame late** after a hold release: the hold branch returns before `ProcessClipBlocks`.
6. **`CutsceneCameraPose` has no "driven" flag.** A host cannot tell "this cutscene has no camera lane" from "the pose is live", and the singleton keeps stale data after a cutscene ends.
7. **The tests never built a two-segment cutscene**, which is why 1, 3 and 5 shipped green.

## 2. Read first

- `Authoring/Build/CutsceneBlobBuilder.cs` (all), `Runtime/Sampling/CutsceneBlobSampler.cs`, `Runtime/Sampling/CutsceneBlockTiming.cs`, `Runtime/Systems/CutsceneTimelineSystem.cs`, `Runtime/Blobs/CutsceneBlob.cs`, `Runtime/Components/CutsceneComponents.cs`.
- `Editor/ClipEditor/Cutscene/CutscenePoseSampler.cs` (it moves — §3.1), `CutscenePreviewController.cs` `ApplyPose` / `ApplyCameraPose` / `ApplyActorParts`, `CutsceneSlotClipPreview.cs`.
- `Runtime/Api/AnimationCommandUtil.cs` `SetSpeed`; `Runtime/Components/AnimationCommand.cs`.
- `Tests/PlayMode/CutsceneTimelineSystemTests.cs` (fixture conventions, hand-built blobs); `Tests/EditMode/CutsceneBlockTimingTests.cs`.
- `Authoring/AssemblyInfo.cs` — `InternalsVisibleTo` pairs (Editor and both test assemblies already see `Authoring` internals).

## 3. Design

### 3.1 One flat-list sampler, shared by the builder and the editor

Move `Editor/ClipEditor/Cutscene/CutscenePoseSampler.cs` to `Authoring/Build/CutsceneKeySampler.cs`, namespace `DotsAnimationToolkit.Authoring`, `internal static class CutsceneKeySampler`. Same methods, renamed for the new role: `TrySampleTransform(List<CutsceneTransformKey>, float, out float3, out float3 eulerDegrees, out float3) : bool` (false when the list is null/empty), `SampleCamera`, `SampleCameraWithCuts`, `TryResolveFacingAngle`. Keep `UnityEngine`-free where the editor does not need `Vector3`; the editor converts at its call sites. The Editor assembly already sees `Authoring` internals. Delete the editor copy; every former caller points at the new type.

### 3.2 Boundary continuity at bake (defect 1)

In `CutsceneBlobBuilder.FillSegments`, after bucketing and before blob assembly, for every interior boundary time `B` (every hold), for every keyed lane (root transform per slot, each part track per slot, camera lane, facing lane per slot) that has at least one key anywhere on the flat timeline:

- If no authored key lies within `BoundaryEpsilon` of `B`: sample the flat lane at `B` (through `CutsceneKeySampler`; camera through `SampleCameraWithCuts`) and insert **two synthetic keys**: one at `time = duration` of the segment ending at `B`, one at `time = 0` of the segment starting at `B`, both carrying the sampled value. The synthetic key's `interpolation` and Bézier handles copy the authored key that precedes `B` (so easing continues into the next piece).
- If an authored key does lie at `B`: it stays in the next segment (unchanged rule) and only the *ending* segment gets a synthetic copy at `time = duration`.
- Facing lane: the synthetic key at `0` of the next segment carries the last authored override at-or-before `B`, if any (hold semantics).

Record the one known limitation in the builder's class remark: a Bézier span crossing a hold is split into two spans that each ease independently — a hold inside a Bézier ease changes its shape slightly. Linear and the preset eases are exact.

### 3.3 Empty lanes leave transforms alone (defect 2)

`CutsceneBlobSampler.SampleTransform` → `TrySampleTransform(...) : bool` (false on `Length == 0`; keep the old name as a thin wrapper only if a caller outside the cutscene files uses it — grep first). `CutsceneTimelineSystem.ApplyPose` skips the write on `false`. Editor: `CutscenePreviewController.ApplyPose` skips the root write on `false`; part tracks and camera already guard on count.

### 3.4 Baked seam blend (defect 3)

`CutsceneClipBlockBlob.blendDuration : float`. `BucketClipBlocks` sorts the slot's blocks by `start` first, then computes each block's blend from its predecessor on the **flat** lane with `CutsceneBlockTiming.SeamBlendDuration` and stores it. `ProcessClipBlocks` reads `block.blendDuration`; the in-segment derivation is deleted. `CutsceneBlobBuilder.SchemaVersion = 2`. The editor preview already derives from the flat list through the same `CutsceneBlockTiming` call — unchanged.

### 3.5 Speed and pause propagate (defect 4)

`CutscenePlaybackState.appliedLayerSpeed : float` (initialised to `-1f` in `CreatePlayRequest`, meaning "never applied"). Each frame in `ProcessCutscene`, before advancing: `float effectiveLayerSpeed = control.paused ? 0f : math.max(0f, control.speed)`. If it differs from `appliedLayerSpeed`: for every bound Actor slot with an `AnimationCommand` buffer, append `CommandKind.SetSpeed { layerIndex, speed = effectiveLayerSpeed }` and enable `AnimationCommandPending`; store. Newly issued blocks use `speed = effectiveLayerSpeed` (not `1f`). **A hold does not change layer speed** — looping clips keep cycling under a hold by owner call (Phase G §2); pause is the host saying "freeze everything", hold is the cutscene saying "wait here". Fix the `paused` doc comment to say exactly that. Completion/skip still issues `Stop`.

### 3.6 Release-frame issue (defect 5)

Restructure the hold branch: when the release matches, call `AdvanceToNextSegment` and **fall through** to the normal path with `deltaTime = 0f` for this frame (so `ProcessClipBlocks`/`ProcessEvents` fire everything at `timeInSegment == 0`), instead of returning. When still held: apply pose/camera and return, as today.

### 3.7 `CutsceneCameraPose.isDriven` (defect 6)

Add `public bool isDriven;`. `CutsceneTimelineSystem.OnUpdate` clears it at the start of the frame; `ApplyCameraPose` sets it `true` only when it wrote a pose (segment has camera keys and the cutscene is not complete). A host applies the pose only while `isDriven`. On completion the flag stays false, so a host's exit transition triggers exactly once.

## 4. Decisions

- **A62-D1** Continuity is solved at **bake** (synthetic boundary keys), not by teaching the runtime sampler about neighbouring segments. The runtime stays a per-segment array walk that Burst can inline; the segment split's whole point (Phase G §5) was that lookups never reach across a boundary.
- **A62-D2** The flat-list sampler lives in `Authoring`, not `Runtime`: it works on `List<T>` authoring types, which the runtime assembly must never see.
- **A62-D3** Blend is baked, not derived at play time; the derivation formula stays the one copy in `CutsceneBlockTiming` (A58-D1).
- **A62-D4** Pause ≠ hold (§3.5).

## 5. Tasks

- [x] **T1 — Sampler move (§3.1).** Pure refactor, no behaviour change. Gate: compile; run `DotsAnimationToolkit.Tests.EditMode` group `CutsceneBlockTimingTests` and any EditMode fixture that referenced the old type (grep).
- [x] **T2 — Boundary continuity (§3.2).** Test (EditMode, new `Tests/EditMode/CutsceneBlobBuilderTests.cs`): `HoldBoundary_BakesTheSampledPoseIntoBothSegments` — in-memory `CutsceneAsset` (`ScriptableObject.CreateInstance`, no disk), one Prop slot with keys A(0s, x=0) and B(4s, x=8), one hold at 2s; `Build`; assert segment 0's last key is at `time == 2f` with `x == 4f`, segment 1's first key is at `time == 0f` with `x == 4f`; dispose the blob in `finally`. Revert the fix, watch it fail (segment 0 will have one key). Camera variant of the same test is optional — only if it costs under ten lines.
- [x] **T3 — Empty lanes (§3.3).** Test (PlayMode): `EmptyRootLane_LeavesTheBoundTransformAlone` — a Prop slot with zero transform keys, entity placed at `(3, 0, 0)`, advance one step, assert unchanged.
- [ ] **T4 — Baked blend (§3.4).** Test (EditMode, same builder fixture): `SeamAcrossAHold_KeepsItsBlendDuration` — blocks `[0, 3)` and `[2.5, 5)` with a hold at 2.7 → the second block (segment 1) has `blendDuration == 0.5f`. Bump `SchemaVersion`.
- [ ] **T5 — Speed/pause (§3.5).** Test (PlayMode): `SpeedChange_IssuesSetSpeedOnEveryBoundActorLayer` — set `CutsceneControl.speed = 0.5f`, advance, assert the actor's `AnimationCommand` buffer contains a `SetSpeed` with `speed == 0.5f` on the request's layer; set `paused = true`, advance, assert a `SetSpeed 0`. (The fixture's actor has no `CommandApplySystem` running, so the buffer keeps the commands — that is what makes the assertion possible.)
- [ ] **T6 — Release frame (§3.6) + `isDriven` (§3.7).** Test (PlayMode): `BlockAtSegmentStart_IsIssuedOnTheReleaseFrame` — two-segment hand-built blob (add a `BuildTwoSegmentCutsceneBlob` helper to the fixture file: hold at 1s, a block at segment-1 time 0), run to the hold, enable `CutsceneHoldRelease` with the id, advance **once**, assert the Play command is already in the buffer. `isDriven` needs no fixture; assert it inside `SkippedAndPlayedThrough_…` (false after completion) since that test already exists.
- [ ] **T7 — Docs.** `Documentation~/cutscenes.md`: a short "Holds, pause and speed" subsection (hold keeps clips cycling; pause freezes layers; speed scales layers), and remove "Facing … no runtime-side application" from Known gaps only when A65 lands — leave it for now. `CHANGELOG.md` `[Unreleased]` → "Fixed — cutscene runtime" with the six defects in one sentence each. HANDOFF §4 paragraph.
- [ ] **Full suites once** (EditMode + PlayMode), counts must not drop below 712 / 243 plus the fixtures added here.

## 6. Risks and traps

- The synthetic keys change `ComputeContentEndSeconds`? No — they never exceed an authored time. But they *do* change `segment.transformKeys.Length`; any test asserting exact key counts on a multi-hold bake must be updated, not weakened.
- `AssignToSegment`'s closed final interval must stay: a synthetic key at `time == duration` of the last segment is legal.
- `SetSpeed` on a layer that is not active is harmless (`CommandApplySystem` applies it to the layer state); do not gate it on `PlaybackFlags.Active`, or a block issued later would inherit speed 1.
- `CutsceneKeySampler` must not pull `UnityEditor` into `Authoring` — `Conformance_C` reads raw text; the moved file's comments must not mention the editor namespace by name either.

## 7. Build log
