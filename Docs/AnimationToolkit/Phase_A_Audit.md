# Phase A Audit — Existing Animation Tool, Pipeline & Shaders

**Repo:** `C:\Users\spenc\Documents\GitHub\Stitch_Punk` · **Audit date:** 2026-07-26 · **Author:** Auditor agent (Phase A of the DOTS animation package effort)

All paths are repo-relative. Line numbers refer to the files as of commit `c95a796` (working tree clean at audit time). Claims of *absence* state where I looked. Inferences are marked **[inference]**.

---

## 1. Environment & versions (detected)

Detected from `ProjectSettings/ProjectVersion.txt` and `Packages/manifest.json` / `Packages/packages-lock.json` (Burst/Collections/Mathematics are transitive deps of Entities and only pinned in the lock file):

| Component | Version | Evidence |
|---|---|---|
| Unity Editor | **6000.5.0f1** (rev 88b47c5e7076) | `ProjectSettings/ProjectVersion.txt:1-2` |
| com.unity.entities | **6.5.0** | `Packages/manifest.json:8`; `packages-lock.json` |
| com.unity.entities.graphics | **6.5.0** | `Packages/manifest.json:9` |
| com.unity.render-pipelines.universal (URP) | **17.5.0** | `Packages/manifest.json:15` |
| com.unity.burst | **1.8.29** | `Packages/packages-lock.json` (transitive) |
| com.unity.collections | **6.5.0** | `Packages/packages-lock.json` (transitive) |
| com.unity.mathematics | **1.4.0** | `Packages/packages-lock.json` (transitive) |
| com.unity.physics | 6.5.0 | `Packages/manifest.json:14` |
| com.unity.inputsystem | 1.19.0 | `Packages/manifest.json:12` |
| com.unity.cinemachine | 3.1.7 | `Packages/manifest.json:7` |
| com.unity.test-framework | 1.7.0 | `Packages/manifest.json:16` |
| UniTask | git (Cysharp main) | `Packages/manifest.json:3` |

Render pipeline: URP, Linear color space (`Gotchas.md:153` cites `m_ActiveColorSpace: 1`), custom cel-shaded lighting via Shader Graph + reflection-API HLSL nodes (§5). Rendering of animated units is Entities Graphics (hybrid renderer) with DOTS-instanced per-instance material properties.

Assembly layout relevant to animation (each folder has a `StitchPunk.*.asmdef`): `Components`, `Data`, `Systems`, `Authoring`, `Utils`, `Editor`. **Notable:** `Assets/_Scripts/Editor/StitchPunk.Editor.asmdef` has `"includePlatforms": []` — it is **not** an Editor-only assembly (verified by reading every asmdef under `Assets/_Scripts/`; only `Tests/StitchPunk.Tests.asmdef` restricts to `['Editor']`). Consequences in §4/§7.

---

## 2. Techniques implemented today

### 2.1 Transform/positional keyframe animation — YES (the core technique)

Characters are **layered flat quads** ("paper-doll" rigs): one entity per body part, parented under a rig root; animation writes each part's `LocalTransform` every frame. No Unity Animator, no skinned meshes (`_Vault/Memories/Code/Systems_Animation.md:8-16`).

- Clip data: `AnimationClipSO` holds `List<PartTrack>`; each `PartTrack` targets one `AnimationTarget` (body-part enum) and holds `List<Keyframe>` with `normalizedTime`, `position` (Vector3 — x/y = local offset, **z = draw-layer order**), `rotation` (Z degrees), `scale` (Vector2, −1 = flip), `imageIndex`, optional per-key interpolation override (`Assets/_Scripts/Data/SOs/AnimationClipSO.cs:31-56`).
- Runtime sampling: `SampleLayeredAnimationJob` walks each rig's `BodyPart` buffer, composites layers highest-priority-first with an `AnimatedProperties` claim mask, lerps between bracketing keyframes with easing (`AnimationSamplingSystem.cs:74-201`), and writes an `AnimationTargetPose` per part.
- Apply: `ApplyPoseJob` writes `LocalTransform.Position`, Z `Rotation` — and **forces `Scale = 1f`** (`ApplyAnimatedPoseSystem.cs:29-37`).
- **Scale is sampled but never rendered.** `AnimationTargetPose.scale` (`AnimationComponents.cs:48-54`) is computed by sampling (`AnimationSamplingSystem.cs:237-240`) but `ApplyPoseJob` ignores it and no system writes the part's `PostTransformMatrix` (baked identity at `BodyPartAuthoring.cs:104`; the only `PostTransformMatrix` writer in the codebase is `HealthBarSystem.cs:71`, which targets health-bar visuals). So scale/flip keyframes — including the Mirror Clip utility's `scale.x = -1` flips (`AnimationClipUtilities.cs:84`) — are currently dead on screen. **[inference:** this is an unfinished feature or regression, not a design choice — the authoring bakes non-uniform-scale transform flags (`BodyPartAuthoring.cs:54-56`) precisely to support it.**]**

Interpolation: Linear / Step / EaseIn / EaseOut / EaseInOut (`AnimationEnums.cs:104-111`), quadratic easings in `AnimationSamplingSystem.cs:203-216`. Blend modes per track: Additive (adds position/rotation, multiplies scale onto the accumulated value) or Override (`AnimationEnums.cs:113-117`, `AnimationSamplingSystem.cs:218-243`).

### 2.2 Flipbook / texture-array UV animation — YES (slice-index, not UV-offset)

Frames live in **`Texture2DArray` assets**; the animated value is the **array slice index**, not a UV rect:

- Keyframes carry `imageIndex` (−1 = no change) (`AnimationClipSO.cs:51`).
- Sampling folds it into `AnimationTargetPose.imageIndex` (nearest keyframe by t<0.5 rule, `AnimationSamplingSystem.cs:198`).
- `ApplyAnimatedImageIndexJob` copies it into `ImageIndex` (`ApplyAnimatedPoseSystem.cs:39-50`); `UpdateImageIndexSystem` copies `ImageIndex.index` → `ImageIndexOverride.Value` (`UpdateImageIndexSystem.cs:19-30`), which is `[MaterialProperty("_ImageIndex")]` (`AnimationComponents.cs:62-66`) — a DOTS-instanced float consumed by the array shaders' `Sample Texture 2D Array` node (§5).
- There is **no time-based flipbook in the shader** — frame selection is entirely CPU/ECS-side. No UV-offset atlas animation exists either (no atlas UV math in any graph property list, §5; the old `int2 matGridPosition` idea survives only in the orphaned `KeyframeSO`, see §2.6).

### 2.3 Billboarding — YES, CPU-side (system, not shader)

`BillboardSystem` + `BillboardJob` (`Systems/AnimationSystemGroup/AnimationExecutionSystemGroup/BillboardSystem.cs`):

- Runs after `ApplyAnimatedPoseSystem` (line 8-9). `OnUpdate` is **not Burst-compiled** — the `[BurstCompile]` on it is commented out (line 24) because it reads `Camera.main.transform.forward` (lines 27-29); the job itself is Bursted (line 47).
- `Billboard { Entity parentEntity }` (`AnimationComponents.cs:27-30`) is baked by `BillboardAuthoring` (`Authoring/Animation/BillboardAuthoring.cs:11-17`). The job sets the quad's local rotation to face the camera via `parentTransform.InverseTransformRotation(LookRotation(cameraForward))` (lines 67-77).
- Dead units freeze yaw but keep camera pitch (yaw/pitch decomposition, lines 79-99); gated by the parent rig's `CameraVisible` enableable (lines 60-63).
- **No billboard math in any shader** — verified by enumerating every property/node reference in the production graphs (§5); billboarding is purely this system writing `LocalTransform`.

### 2.4 VAT (vertex animation textures) — ABSENT

No VAT anywhere. Where I looked: repo-wide grep for `VAT`, `VertexAnimation`, `vertex animation` under `Assets/` — the only hits are an art-ideas note (`Assets/_Vault/Spencer/Art_Assets.md`), an Obsidian theme CSS, and TextMeshPro's stock `TMPro_Surface.cginc`. No bake tooling, no VAT sampler nodes under `Assets/Shaders/Nodes/`, no vertex-stage displacement in any production graph (property enumeration in §5 shows no position/VAT textures), and the entire runtime writes poses via `LocalTransform` on the CPU (§2.1).

### 2.5 2.5D multiplane — YES, via per-part Z layer order

Depth layering inside a rig is authored as keyframe `position.z` ("layer order offset", `AnimationClipSO.cs:48`; `AnimatedProperties.PositionZ = 1 << 2 // Layer order`, `AnimationEnums.cs:126`). The clip editor labels it "(X, Y = offset, Z = layer order)" (`AnimationClipEditorWindow.cs:729`). Parts therefore z-fight-order by tiny Z offsets in front of the billboarded root quad, with the world itself being 3D URP with a mostly fixed camera (mip-chain decision note, `Shaders.md:160-169`). There is no multi-plane parallax *system*; 2.5D = flat rigs + Z ordering + CPU billboard + 3D environment.

### 2.6 Legacy/orphaned: `KeyframeSO` / `DOTSKeyframe`

`Assets/_Scripts/Data/SOs/KeyframeSO.cs` defines a `KeyframeSO` ScriptableObject (per-frame asset with `matGridPosition`, `onFlipBookChange`) and a `DOTSKeyframe` struct. Grep across `Assets/_Scripts/` shows **zero references outside the defining file** — it is a dead remnant of an earlier per-keyframe-asset design superseded by inline `AnimationClipSO.Keyframe` classes.

---

## 3. Data flow: authoring → bake → runtime

### 3.1 Authoring-side data

- **`AnimationClipSO`** (`Data/SOs/AnimationClipSO.cs`) — one clip. Fields: `animationType` (enum key), `duration` (seconds), `looping`, `allowBlendIn`, `allowBlendOut`, `partTracks` (inline `[Serializable]` classes, **not sub-assets**), `soundMarkers` (`SoundType` + normalized time, lines 18-28). ~21 clip assets live under `Assets/ScriptableObjects/Animations/` (subfolders `Action/`, `Base/`, `Direction/`, `Eyes/`, `Mouth/` + `None.asset`), registered in `_AnimationLibrary.asset`.
- **`AnimationLibrarySO`** (`Data/SOs/AnimationLibrarySO.cs`) — flat `List<AnimationClipSO>` with a linear-scan `GetClip(AnimationType)` (lines 7-17). No dedup or validation of duplicate `animationType` entries.
- **Enums** (`Data/Enums/AnimationEnums.cs`): `AnimationType : ushort` (~48 values incl. game-specific blink sets, lines 1-50), `AnimationLayerType` (7 layers, lines 52-61), `AnimationTarget : byte` (35 humanoid part slots, lines 63-101), `InterpolationMode`, `BlendMode`, `[Flags] AnimatedProperties : byte` (lines 120-136). **None of the enums use explicit numeric values** — inserting a value mid-enum silently renumbers every serialized `animationType`/`animationTarget` in existing assets (determinism/data-integrity risk; the `AnimationTarget` comment "keep under 256" at line 63 is the only guard).

### 3.2 SO → Blob bake

`AnimationLibraryBakingSystem` (`Systems/PostBakingSystemGroup/AnimationLibraryBakingSystem.cs`), `[WorldSystemFilter(BakingSystem)]`, `[UpdateInGroup(PostBakingSystemGroup)]` (lines 9-10):

1. `AnimationLibraryAuthoring` bakes `AnimationLibraryReference { UnityObjectRef<AnimationLibrarySO> }` + an empty `AnimationLibrary` holder onto a library entity (`Authoring/EntityLibraries/AnimationLibraryAuthoring.cs:10-24`). Both components defined in `Components/EntityLibraries/EntityLibraries.cs:4-10`.
2. The baking system takes the **first** `AnimationLibraryReference` it finds (lines 20-25 — multiple libraries are silently ignored), pre-allocates an **enum-indexed slot per `AnimationType` value** (`Enum.GetValues(typeof(AnimationType)).Length`, lines 32-41) with `duration = 0` placeholders, then fills slots from the SO list (lines 43-96). Keyframe interpolation overrides are resolved at bake into a single `interpolation` per keyframe (line 92).
3. Blob root `AnimationLibraryBlob { BlobArray<AnimationClipBlob> clips }`; per clip: `BlobArray<AnimationTargetTrackBlob>` + `BlobArray<SoundMarkerBlob>`; per track: `BlobArray<KeyframeBlob>` (`Data/Structs/AnimationBlobs.cs:1-43`).
4. The blob reference is written into **every** `AnimationLibrary` holder, disposing any prior blob (lines 101-109); `OnDestroy` disposes per holder (lines 112-121) — with >1 holder this would double-dispose the shared reference **[inference:** latent bug, benign today because exactly one library entity exists**]**.

**Dedup:** none — two clips with the same `animationType` = last-writer-wins on the slot, silently. **Determinism:** the bake itself is deterministic (list order + enum order); the risks are enum renumbering (§3.1) and no sorting/validation of keyframe times at bake (runtime assumes ascending order; the editor keeps lists sorted on edit, `AnimationClipEditorWindow.cs:770,882,1037,1060`, but hand-edited assets are unvalidated).

**Runtime clip addressing:** systems index `library.Value.clips[(int)layer.animation]` directly (`AnimationTimeSystem.cs:47`, `AnimationSamplingSystem.cs:102`, `AnimationSoundMarkerSystem.cs:52`) — O(1), no bounds check beyond the enum domain. Unregistered clips are duration-0 placeholders; `AnimationTimeSystem` marks a non-looping duration≤0 layer instantly complete (`time = float.MaxValue; active = false`) so downstream attack logic still fires (`AnimationTimeSystem.cs:49-56`). The comment there names `AttackHitFrameSystem`, which **no longer exists** — grep shows hit timing is now delta-time-driven inside `AttackRequestSystem` (`CombatSystemGroup/CombatExecutionSystemGroup/AttackRequestSystem.cs:98-104`); stale comment.

### 3.3 Runtime component set (complete)

Per **rig root** (baked by `CharacterRigAuthoring.Baker`, `Authoring/Units/CharacterRigAuthoring.cs:75-159`):

| Component | Kind | Purpose / evidence |
|---|---|---|
| `AnimationLayer` (buffer, `[InternalBufferCapacity(8)]`) | `IBufferElementData` | The playback state machine: `{layer, animation, time, speed, active, looping}` (`AnimationComponents.cs:15-24`). Seeded from authoring `startingLayers`, kept sorted by layer enum (`CharacterRigAuthoring.cs:82-95,161-176`) |
| `SetAnimation` (buffer) | `IBufferElementData` | Request queue: `{layer, animation, speed, looping}` (`AnimationComponents.cs:6-12`) |
| `AnimationRequest` | enableable tag | "Requests pending" flag (`AnimationComponents.cs:5`); baked disabled (`CharacterRigAuthoring.cs:98-99`) |
| `BodyPart` (buffer, cap 32) | `IBufferElementData` | Rig registry: `{entity, target, unitPart, flags}` (`Components/Units/BodyPartComponents.cs:12-19`). Assembled at bake by `CharacterRigBakingSystem` (`Systems/PostBakingSystemGroup/CharacterRigBakingSystem.cs:24-41`) and re-assembled at spawn from `LinkedEntityGroup` by `BodyPartInitSystem` (`Systems/SpawnInitSystemGroup/BodyPartInitSystem.cs`) because ECB-instantiate does not remap buffer entity refs (`BodyPartComponents.cs:4-9`) |
| `CameraVisible` | enableable tag | Presentation-only culling gate, flipped by `CameraVisibilitySystem` (GameManagerSystemGroup) (`Components/Units/CameraVisibilityComponents.cs:1-15`) |

Per **part child** (baked by `BodyPartAuthoring.Baker`, `Authoring/Units/BodyPartAuthoring.cs:47-135`):

| Component | Kind | Purpose / evidence |
|---|---|---|
| `BodyPartInfo` | data | Self-description `{target, unitPart, flags}` (`BodyPartComponents.cs:24-29`) |
| `BaseParent` | data | Root back-pointer (`AnimationComponents.cs:35-38`) |
| `AnimationTargetRestPose` | data | Bind pose incl. `baseImageIndex` (`AnimationComponents.cs:40-46`; baked from the GameObject transform, `BodyPartAuthoring.cs:86-92`) |
| `AnimationTargetPose` | data | Sampled output pose; pre-seeded to rest to avoid spawn-frame collapse (`BodyPartAuthoring.cs:94-102`) |
| `PostTransformMatrix` | data | Baked identity, **never written by animation** (§2.1) |
| `ImageIndex` | data | `{index, onUpdate}` staging value (`AnimationComponents.cs:56-60`) |
| `ImageIndexOverride` | **`[MaterialProperty("_ImageIndex")]`**, float | GPU-visible slice index (`AnimationComponents.cs:62-66`) |
| `BodyPartTint` | **`[MaterialProperty("_BaseColor")]`**, float4 | Per-instance multiply tint; baked as **linear** color (`AnimationComponents.cs:75-79`, `BodyPartAuthoring.cs:118-124`) |
| `BodyPartSecondaryTint` / `BodyPartTertiaryTint` | **`[MaterialProperty("_SecondaryColor"/"_TertiaryColor")]`**, float4 | Packed-mask layer tints; alpha = layer blend strength (`AnimationComponents.cs:86-98`, `BodyPartAuthoring.cs:125-133`) |
| `CameraVisible` | enableable tag | Propagated from root (`BodyPartAuthoring.cs:106-109`) |

Related material-property components outside animation: `_IsInteractable` (`Components/AI/UtilityAiComponents.cs:113`), `_SelectionColor` (`Components/Units/UnitComponents.cs:47,85`). Standalone non-rig quads get `ImageIndex`/`ImageIndexOverride`/`CameraVisible` via `ImageIndexAuthoring` (`Authoring/Animation/ImageIndexAuthoring.cs:11-31`). Billboard quads get `Billboard` via `BillboardAuthoring` (§2.3).

Library singletons: `AnimationLibrary { BlobAssetReference<AnimationLibraryBlob> }` and, for assignment, `UnitDataLibrary`/`UnitLibraryBlob` with per-unit `idleAnimation`, `movingAnimation`, `actionAnimations` (`ActionType→AnimationType`), `stanceAnimations` (`Data/Structs/UnitBlob.cs:17-54`). Global frame-rate setting: `GameSettings.animationFrameRate` (`Components/Save/GameDataComponents.cs:26-28`), default 24, baked by `GameDataAuthoring` (`Authoring/Save/GameDataAuthoring.cs:17,49`).

### 3.4 Per-frame system pipeline (execution order)

Group topology (`Systems/SystemGroups.cs`): `AnimationSystemGroup : GameSceneSystemGroup` in `SimulationSystemGroup` (line 148-149), running after `HealthSystemGroup` → `DesignSystemGroup` (lines 137-146; Design runs before Animation so re-skins land the same frame). Children: `AnimationAssignmentSystemGroup` (`OrderFirst`, line 151-152) then `AnimationExecutionSystemGroup` (`OrderLast`, line 154-155). The whole feature is gated on a `GameSceneTag` entity existing (`SystemGroups.cs:17-24`; tag defined `Components/Tags/SceneTags.cs:4`).

Upstream writers: `BehaviorExecutionSystem` (AI commands PlayAnimation / PlayActionAnimation / StopAnimation append `SetAnimation` on the **Action** layer and enable `AnimationRequest`, `StateMachineSystemGroup/ActionExecutionSystemGroup/BehaviorExecutionSystem.cs:338-392`), `BehaviorInterruptSystem`, and `PlayerAttackSystem` (`Contracts.md:28`).

| # | System | Reads → Writes | Burst / scheduling |
|---|---|---|---|
| 1 | `AnimationRequestSystem` (Assignment, OrderFirst) | `SetAnimation` buffer → `AnimationLayer` buffer via `AnimationUtils.SetLayer`; clears buffer, disables `AnimationRequest` | `[BurstCompile]`; `ScheduleParallel(state.Dependency)` (`AnimationRequestSystem.cs:5-46`). Setting `AnimationType.None` deactivates the layer (`Utils/AnimationUtils.cs:22-28`) |
| 2 | `UnitAnimationAssignmentSystem` (Assignment) | `UnitData`, `Movement.isMoving`, `UnitAction.current`, `LocomotionStance` + `UnitLibraryBlob` → Base layer (locomotion/stance) and Action layer (action→clip mapping); never clobbers an active non-looping Action clip | `[BurstCompile]`; `ScheduleParallel()` implicit dependency (`UnitAnimationAssignmentSystem.cs:5-113`) |
| — | `UnitFaceDirectionSystem` (Assignment) | **Entirely commented out** — the file is a 17-line comment block (`UnitFaceDirectionSystem.cs:1-17`). `AnimationLayerType.Direction` is referenced **nowhere** in `Assets/_Scripts` (grep: zero hits), so the Direction layer and the 8 directional `AnimationType` values (`AnimationEnums.cs:14-21`) are dead data | n/a |
| 3 | `AnimationTimeSystem` (Execution) | `AnimationLayer` + library blob → advances `layer.time`, loops via `fmod` or clamps + deactivates; duration-0 non-looping completion hack (§3.2) | `[BurstCompile]`; `ScheduleParallel(state.Dependency)` (`AnimationTimeSystem.cs:9-76`). **Deliberately NOT CameraVisible-gated** so off-screen units resume at the correct time (`AnimationSamplingSystem.cs:63-65`, `Systems_Animation.md:73-88`) |
| 4 | `AnimationSoundMarkerSystem` (Execution, after Time) | layers + blob sound markers → emits `PlaySound` entities via `EndSimulationEntityCommandBufferSystem` ECB when playback crosses a marker; reconstructs pre-advance time and handles loop wrap correctly (`AnimationSoundMarkerSystem.cs:45-84`) | `[BurstCompile]`; `ScheduleParallel(state.Dependency)` |
| 5 | `AnimationSamplingSystem` (Execution, after Time) | layers + `BodyPart` + rest poses + blob → `AnimationTargetPose` per part. Frame-rate limited: samples only when a global `accumulatedTime * animationFrameRate` frame counter advances (`AnimationSamplingSystem.cs:35-47`) — one shared phase for all entities, not per-entity. `[WithAll(CameraVisible)]` on rig roots (line 66-67) | struct + `OnCreate` Bursted, `OnUpdate` **not** `[BurstCompile]`-attributed (line 35); job Bursted, `ScheduleParallel()`; part writes via `[NativeDisableParallelForRestriction] ComponentLookup<AnimationTargetPose>` (line 72) |
| 6 | `ApplyAnimatedPoseSystem` (Execution, after Sampling) | `AnimationTargetPose` → `LocalTransform` (`ApplyPoseJob`) and → `ImageIndex` (`ApplyAnimatedImageIndexJob`), both `[WithAll(CameraVisible)]` | `[BurstCompile]`; two `ScheduleParallel()` jobs (`ApplyAnimatedPoseSystem.cs:10-50`) |
| 7 | `BillboardSystem` (Execution, after ApplyPose) | §2.3 | struct not Bursted at `OnUpdate`; job Bursted, `ScheduleParallel()` |
| 8 | `UpdateImageIndexSystem` (Execution, OrderLast) | `ImageIndex` → `ImageIndexOverride` (the `_ImageIndex` upload), `[WithAll(CameraVisible)]` | `[BurstCompile]`; `ScheduleParallel()` (`UpdateImageIndexSystem.cs:4-30`) |

**Dirty-flag rot:** `ImageIndex.onUpdate` is set `true` at bake and by `ApplyAnimatedImageIndexJob` every frame, but is **never set back to false** anywhere live (the only reset is commented out in `Core/Unused/ResetEventsSystem.cs:87`), so the `if (imageIndex.onUpdate)` check in `UpdateImageIndexJob` (`UpdateImageIndexSystem.cs:25`) is always true — the two-hop `ImageIndex → ImageIndexOverride` indirection currently buys nothing over writing `ImageIndexOverride` directly.

**Additive semantics vs docs:** layers are processed highest-first and each track *claims* its `AnimatedProperties` bits (`AnimationSamplingSystem.cs:94-116,146-153`), so a lower layer never contributes to a property an upper layer touched — an Additive upper-layer track adds to the **rest pose**, not to the lower layer's output. The vault doc says "Later layers can override or additively blend on top" (`Systems_Animation.md:40-41`) — **[inference]** the documented intent (additive over the base animation) does not match the implemented claim-mask behavior.

**Runtime vs editor sampler divergence:** the runtime sampler applies at most **one** track per (clip, target) — `break` after the first match (`AnimationSamplingSystem.cs:155`); the editor's SO sampler loops all tracks without breaking (`Editor/AnimationEditor/EditorAnimationSystem.cs:213-229`). A clip authored with two tracks on the same target previews differently than it plays. The editor also quantizes sample time per frame-rate (`EditorAnimationSystem.cs:135,192-199`) while the runtime limits sampling frequency but does **not** quantize `normalizedTime` (`AnimationSamplingSystem.cs:105`).

---

## 4. Editor tooling today

All in `Assets/_Scripts/Editor/AnimationEditor/` (9 files). **Pure IMGUI** (`EditorWindow` + `Handles`/`GUILayout`; no UI Toolkit anywhere in the folder).

### What exists

- **`AnimationClipEditorWindow`** (`AnimationClipEditorWindow.cs`, 1125 lines) — dockable timeline editor (menu `Window ▸ Stitch Punk ▸ Animation Clip Editor`, lines 54-60). Clip selector, transport controls, zoomable timeline with playhead scrub, per-track rows with draggable diamond keyframes, double-click to add keys, and a right-hand inspector that switches clip/track/keyframe context (lines 587-611). Keyboard: Delete, Ctrl+D duplicate, Space play/pause, arrows frame-step, Home/End, Ctrl+scroll zoom (lines 891-975). Copy/paste of keyframe values via a static clipboard (lines 1084-1123). Edits write **directly into the `AnimationClipSO` fields** with `EditorUtility.SetDirty`; `Undo.RecordObject` covers add/delete/duplicate/paste (lines 982, 1019, 1046, 1075, 1110) but **not** inspector value edits or drag-moves (lines 619-632, 845-889 — SetDirty only), so scrubbing a keyframe's time or position is not undoable.
- **`AnimationPreviewController`** (`AnimationPreviewController.cs`, plain MonoBehaviour, **not** `#if UNITY_EDITOR`) — bridges the window to ECS. Two modes: `ClipEdit` (single clip) and `ClipPreview` (layer stack) (lines 9-24). It queries `World.DefaultGameObjectInjectionWorld` for entities with `AnimationLayer` + `BodyPart`, rewrites their layer buffers to the previewed clips (`RebuildLayers`, lines 159-251), and syncs scrub time through the `EditorAnimationTimeControl` singleton (lines 133-156). Play-mode-only (`Update` early-outs otherwise, line 92).
- **`AnimationPreviewControllerEditor`** (`#if UNITY_EDITOR`) — custom inspector with transport buttons + live layer readout (`AnimationPreviewControllerEditor.cs:10-118`).
- **`EditorAnimationTimeControlAuthoring`** — bakes the `EditorAnimationTimeControl` singleton `{isPaused, normalizedTime, playbackSpeed, forceLoop, soloLayerIndex}` and a **managed** `EditorAnimationLibraryManaged { AnimationLibrarySO library }` class-IComponentData (`EditorAnimationTimeControlAuthoring.cs:18-61`).
- **`EditorAnimationSystem`** — the live-preview sampler: an `ISystem` in `SimulationSystemGroup` (no Burst — it touches managed SOs and try/catches everything, lines 25-190) that samples the **`AnimationClipSO` directly**, bypassing the blob, and writes `AnimationTargetPose`. Gated on `AnimationEditorActive` + `EditorAnimationTimeControl` + `GameSettings` (lines 17-19). This is the key design win: **edits preview live with zero rebake** (`Systems_Animation.md:134-136`).
- **`EditorApplyAnimatedPoseSystem`** — main-thread copy of pose `imageIndex` into `ImageIndex` + `ImageIndexOverride` (`EditorApplyAnimatedPoseSystem.cs:15-25`); transforms are applied by the *runtime* `ApplyAnimatedPoseSystem`, which also runs in the preview world.
- **`AnimationEditorScene` / `AnimationEditorSceneTagAuthoring`** — scene marker MonoBehaviour + baker for the `AnimationEditorActive` tag (`AnimationEditorSceneTagAuthoring.cs:8-22`). Dedicated scenes exist: `Assets/Scenes/AnimationEditorScene.unity` + `Assets/Scenes/SubScenes/AnimationEditorSubScene.unity` (the subscene must contain the `GameSceneTag` prefab + `GameSettings` so the runtime apply systems run, `Systems_Animation.md:136`).
- **`AnimationClipUtilities`** — asset-menu bulk ops: Duplicate Clip, **Mirror Clip (Flip X)** (deep-copies tracks, swaps left/right `AnimationTarget`s, negates x/rotation/scale.x, `AnimationClipUtilities.cs:57-110`), Create Clip menu item.

### How live preview works

Play mode in the editor scene → the DOTS world bakes the subscene (library + rig + `GameSceneTag` + `GameSettings`) → `EditorAnimationSystem` samples the SO at the scrubbed/playing time → runtime `ApplyAnimatedPoseSystem`/`BillboardSystem` present it. The runtime samplers stay inert because no `AnimationLibrary` blob exists in that scene (`Systems_Animation.md:136`). The window finds the controller on play-mode-enter and repaints at 20 Hz (throttle comment at `AnimationClipEditorWindow.cs:75-90`; a past perf incident — extra `QueuePlayerLoopUpdate` ticks and per-track logging at 14 FPS — is documented at `Systems_Animation.md:138-141`).

### Strengths and jank

Strengths: zero-rebake SO-direct preview; layer-stack preview mode; mirror/duplicate utilities; solid keyboard workflow; the time-control singleton is a clean editor↔ECS seam.

Jank (each verified): play-mode-only preview (no edit-mode world, `AnimationClipEditorWindow.cs:205-207`); IMGUI with hand-rolled hit-testing and cached rect math (lines 46-52, 289-294, 857-863 — drag deltas recompute layout independently of the draw pass, a classic drift source); incomplete Undo (above); editor/runtime sampler divergence (§3.4); `SampleKeyframesSO` allocates a managed `Keyframe` per sample (garbage per tick, `EditorAnimationSystem.cs:266-275`); preview mutates real `AnimationLayer` buffers of whatever rig it finds (any scene contamination if used outside the editor scene); and — the big one — **none of the ECS-side editor files are `#if UNITY_EDITOR` and the `StitchPunk.Editor` assembly is not platform-restricted (§1)**, so `EditorAnimationSystem`, `EditorApplyAnimatedPoseSystem`, `AnimationEditorActive`, the managed library component, and `AnimationPreviewController` all compile into player builds (dormant without the tag, but shipped).

---

## 5. Shader conventions

### Graph inventory & rendering setup

Production graphs in `Assets/Shaders/Graphs/` (parked experiments in `Assets/Shaders/Legacy/`): `2DShader`, `2DArrayShader`, `2DPackedArrayShader`, `2DViewSwitchingPackedArrayShader`, `PainterlyShader`, `PainterlyPaletteShader`, `3DShader`. All URP `UniversalTarget`; the unit graphs are **Lit subtarget, Opaque + AlphaClip, CastShadows on** (`2DArrayShader.shadergraph:1506-1519`; `2DPackedArrayShader.shadergraph:1671-1683,4485` — `UniversalLitSubTarget`, `m_ReceiveShadows: true`). Lighting is a custom **Cel Shaded Lighting** reflection-API node used by every production graph (`Shaders.md:9,32-37`).

Material→graph mapping for units (verified by GUID grep against `Assets/Materials/Units/*.mat`): body-part materials (`Head`, `Ear`, `Eyebrows`, `FacialHair`, `MaleEyes`, `Mouth`, `Nose`, …) use **`2DArrayShader`**; `PackedRecolorTest.mat` uses **`2DPackedArrayShader`**; `MaleHair.mat` uses **`2DViewSwitchingPackedArrayShader`**.

### Per-instance (DOTS-instanced) properties

Shader Graph "Hybrid Per Instance" = `"hlslDeclarationOverride": 3` in graph JSON. Enumerated per graph (property line ≈ declaration line − 8):

| Graph | Per-instance properties | Evidence (lines) |
|---|---|---|
| `2DShader` | `_BaseColor`, `_IsInteractable` | decl 1742, 2431 |
| `2DArrayShader` | `_BaseColor`, `_IsInteractable`, `_ImageIndex` | decl 1866, 2285, 3906 |
| `2DPackedArrayShader` | `_SecondaryColor`, `_BaseColor`, `_IsInteractable`, `_TertiaryColor`, `_ImageIndex` | decl 1065, 2022, 2441, 3179, 4210 |
| `2DViewSwitchingPackedArrayShader` | `_BaseColor`, `_IsInteractable`, `_UseAltShape`, `_ImageIndex` | decl 1854, 2273, 3289, 3935 |

C#-side mapping via `[MaterialProperty]` components (§3.3): `_ImageIndex` ← `ImageIndexOverride`, `_BaseColor` ← `BodyPartTint`, `_SecondaryColor`/`_TertiaryColor` ← `BodyPartSecondary/TertiaryTint`, `_IsInteractable` ← `UtilityAiComponents.cs:113`, `_SelectionColor` ← `UnitComponents.cs:47,85`. **Gap:** `_UseAltShape` (hair-under-hat switch) has **no** `[MaterialProperty]` component yet — the repo-wide `[MaterialProperty]` grep shows none, and `Shaders.md:65-68` confirms it's planned, not wired.

### Texture arrays vs atlases

Unit sprites are `Texture2DArray` assets sampled by a `SampleTexture2DArray` node driven by `_ImageIndex` (one occurrence in each of the three array graphs; `_Texture2D_Array` property at `2DArrayShader.shadergraph:4111` etc.). Arrays are built by custom tooling with **hand-authored mip chains** (author down to 32px only; `TextureArrayBuilder.cs` behavior documented `Shaders.md:148-169`). No UV atlas flipbooks in production. `2DShader` is the single-texture (`_MainTex`) variant with no image index.

### Reflection-API HLSL node approach

Unity 6.5 Shader Function Reflection API: one exported function per `.hlsl` under `Assets/Shaders/Nodes/`, marked `UNITY_EXPORT_REFLECTION` with `///<funchints>` XML (ProviderKeys `StitchPunk.*`) (`Shaders.md:15-30`). Animation-relevant sprite nodes:

- `PackedChannelRecolor.hlsl` — channel-packed mask → recolorable sprite; R = base fill (× `_BaseColor`), G/B = overlay layers whose **color alpha is the layer blend strength**, A = output alpha (`Nodes/Sprite/PackedChannelRecolor.hlsl:37-52`).
- `PackedChannelSwitch.hlsl` — two-variant slice (normal = R/G pair, alt = B/A pair) cross-faded by `useAltShape` (`Nodes/Sprite/PackedChannelSwitch.hlsl:33-48`).
- `SpriteTint.hlsl` / `SpriteTintMasked.hlsl` — outline-safe multiply tints (library nodes; production graphs do the equivalent via `_BaseColor`, `Shaders.md:49-56,86-102`).

### Passes, batching, color space

- **Shadows:** `m_CastShadows: true` on the unit graphs (above). **[inference]** the generated ShadowCaster pass inherits the graph's alpha-clip, so slice-driven silhouettes should shadow correctly; not verified on-screen.
- **Motion vectors:** no explicit motion-vector configuration found anywhere in the graphs or C# (searched graph JSON and `Assets/_Scripts` for motion-vector terms — nothing). Per-instance transform motion is whatever Entities Graphics 6.5 does by default; **status: unhandled/unverified**.
- **Batching:** the design intent is one shared material per part-class with all variation per-instance (`BodyPartAuthoring.cs:125-133` — tint components kept on every rendering part "so one batchable archetype"; `AnimationComponents.cs:68-79`). Batching killers to avoid: writing `Color` values without sRGB→linear conversion breaks *appearance* not batching (`Gotchas.md:151-155`), but any per-part **material** swap (rather than `_ImageIndex`/tint writes) would split batches — the current pipeline never swaps materials at runtime (design changes write `restPose.baseImageIndex` + tints, `UpdateImageIndexSystem.cs:16-18`).
- All DOTS-uploaded colors must be pre-converted to linear (`Gotchas.md:151-155`, `ColorPaletteLibraryBakingSystem.cs:12`).

---

## 6. Assumptions & couplings (what a standalone package must decouple)

1. **`GameSceneTag` gating** — the whole `AnimationSystemGroup` requires a game-scene tag entity via the `GameSceneSystemGroup` base (`SystemGroups.cs:13-24,148-155`); several systems *additionally* re-declare it (`AnimationRequestSystem.cs:13`, `AnimationTimeSystem.cs:17`, `AnimationSamplingSystem.cs:25`, `AnimationSoundMarkerSystem.cs:16`). A package needs its own opt-in gate (or none).
2. **`GameSettings` singleton from the save system** — sampling frame rate lives on the persisted settings component (`GameDataComponents.cs:26-28`); `AnimationSamplingSystem` refuses to run without it (`AnimationSamplingSystem.cs:26,37`). Must become package config.
3. **Game enums as identity** — `AnimationType` (with game-specific members like `FleshAutomatonBlinkAngry`, `MailDelivery`), `AnimationTarget` (a fixed humanoid part list incl. `JacketLeftSide`, `Mustache`), `AnimationLayerType` (`AnimationEnums.cs`). The blob is *indexed by* `AnimationType`, so clip identity = enum ordinal. A package needs open-ended IDs (hashes/registries) and user-definable target/layer sets.
4. **`UnitAnimationAssignmentSystem` is pure game logic** — depends on `UnitData`/`UnitType`, `Movement`, `UnitAction`/`ActionType`, `LocomotionStance`/`StanceType`, and the `UnitLibraryBlob` animation mappings (`UnitAnimationAssignmentSystem.cs:32-66`, `UnitBlob.cs:30-34`). The package boundary should be the `SetAnimation`/`AnimationLayer` request API; state→clip mapping stays game-side.
5. **Sound markers couple to the game's audio stack** — `SoundType` enum, `SoundUtil.PlayOn`, `PlaySound` entities culled by `VoiceSelectionSystem` (`AnimationClipSO.cs:18-28`, `AnimationSoundMarkerSystem.cs:1-8,70`). Should generalize to typed animation events with a game-side consumer.
6. **Combat timing entanglement** — `AnimationTimeSystem`'s duration-0 completion hack exists so `AttackRequest` disarms (`AnimationTimeSystem.cs:49-56`), and `Contracts.md:17` documents combat reading swing timing off animation. The completion signal should be a first-class "clip finished" event, not a comment-mediated contract.
7. **`CameraVisible` culling contract** — presentation gating depends on the game's `CameraVisibilitySystem` (GameManagerSystemGroup) and its propagation rules, with a project HARD RULE that only presentation systems may use it (`CameraVisibilityComponents.cs:1-15`, `RULES.md:29`, `Contracts.md:39`). Package equivalent must be a pluggable visibility provider.
8. **`BillboardSystem` couplings** — `UnityEngine.Camera.main` on the main thread and the game's `Dead` enableable for the corpse-yaw-freeze behavior (`BillboardSystem.cs:25-29,65`).
9. **Rig registry & spawn remap** — `BodyPart`/`BodyPartInfo` fold design (`UnitPartId`), ragdoll, and socket flags into the same registry the animation reads (`BodyPartComponents.cs:12-29`), assembled by game systems (`CharacterRigBakingSystem`, `BodyPartInitSystem`). The package needs its own minimal (entity, target) registry + the LinkedEntityGroup rebuild trick.
10. **`CharacterRigAuthoring` bundles animation with design + ragdoll baking** (`CharacterRigAuthoring.cs:75-159`) — the animation slice (starting layers, `SetAnimation`, `AnimationRequest`, part registry) must be extracted into a standalone authoring component.
11. **Hard-coded group ordering** — Design-before-Animation same-frame image-index contract (`SystemGroups.cs:141-146`), Animation-after-Health, ragdoll overwrite ordering (`Gotchas.md:128-133`: `ApplyPoseJob` stomps every posed entity each frame and the ragdoll driver *must* run later and re-assert). Any package must expose explicit sync points instead of relying on this frame layout.
12. **Editor preview requires the game's scene furniture** — `GameSceneTag` prefab + `GameSettings` baked inside `AnimationEditorSubScene` (`Systems_Animation.md:136`), and play mode.

---

## 7. Strengths / Weaknesses

### Genuinely good (worth carrying forward)

- **Data model** — normalized-time keyframes, per-target tracks, property claim masks, per-track blend mode + interpolation with per-key override: compact, blob-friendly, and proven on screen (`AnimationBlobs.cs`, `AnimationSamplingSystem.cs`).
- **Enum-indexed blob with pre-filled placeholder slots** — O(1) clip fetch, missing clips degrade gracefully instead of crashing (`AnimationLibraryBakingSystem.cs:32-41`, `AnimationTimeSystem.cs:49-56`).
- **The layer-buffer state machine + request queue** (`AnimationLayer` / `SetAnimation` / enableable `AnimationRequest`) is a clean, Burst-friendly public API; `AnimationUtils.SetLayer`'s `None`-clears-layer convention is tidy (`AnimationUtils.cs:22-28`).
- **Visibility-gated presentation with ungated timers** — a deliberate, documented design that avoids off-screen pose snapping (`AnimationSamplingSystem.cs:63-65`, `Systems_Animation.md:80-93`) and keeps simulation camera-independent (`RULES.md:29`).
- **Sound-marker wrap handling** is actually correct across loop boundaries (`AnimationSoundMarkerSystem.cs:62-81`) — easy to get wrong, done right.
- **Live SO-sampled preview** (no bake loop while authoring) is the single best idea in the editor tooling (`EditorAnimationSystem.cs`, `Systems_Animation.md:134-136`).
- **Per-instance material property discipline** — every varying visual is a Hybrid-Per-Instance property with a `[MaterialProperty]` component, uniform archetypes, documented sRGB→linear rule (§5). Crowds batch.
- Mirror/duplicate clip utilities encode real animator workflow (`AnimationClipUtilities.cs:57-110`).

### Fragile / missing / won't scale

- **Blending does not exist.** `allowBlendIn/Out` are authored and baked but never read by any runtime system (grep: only the bake copies at `AnimationLibraryBakingSystem.cs:53-54`); `SetLayer` hard-resets `time = 0` (`AnimationUtils.cs:36-41`). Every transition pops.
- **Scale/flip animation is dead at the apply stage** (§2.1) — sampled, then discarded (`ApplyAnimatedPoseSystem.cs:36`); `PostTransformMatrix` baked but unused.
- **Direction layer is dead** — `UnitFaceDirectionSystem` is a commented-out husk; 8 enum values and a layer slot carry no behavior (§3.4).
- **Additive semantics diverge from documented intent** (claim mask blocks lower layers; §3.4) and **editor sampler ≠ runtime sampler** (multi-track-per-target, time quantization; §3.4) — both silently change what animators see vs ship.
- **No animation events beyond sounds**, no root motion, no IK, no per-entity playback phase (global frame-limit counter at `AnimationSamplingSystem.cs:17-47` makes every visible rig sample on the same tick — spiky), no clip validation at bake (unsorted keys, duplicate `animationType`, out-of-range `imageIndex` all pass silently).
- **Enum-ordinal identity everywhere** — clip list, blob index, serialized assets; a mid-enum insert corrupts data silently (§3.1). Doesn't scale to a package or to content teams.
- **`ImageIndex` dirty flag is broken-by-rot** (never cleared; §3.4) — the extra component + system exist only to service a flag that is always true.
- **Editor assembly ships in builds** (§1, §4) — managed preview systems and SO references compile into players.
- **Undo coverage is partial** in the clip editor; IMGUI implementation carries duplicated layout math (§4).
- **No tests for any animation math** — `Assets/_Scripts/Tests/` covers group ordering/placement only (`SystemGroupOrderTests.cs:31,64-65`); sampling, easing, wrap, and marker logic are untested.
- Minor latent bugs: multi-holder blob double-dispose (§3.2), stale `AttackHitFrameSystem` comment (§3.2), `AnimationSamplingSystem.OnUpdate` missing `[BurstCompile]` (§3.4 — cheap main-thread cost, but inconsistent with project rules `RULES.md:24`).
- **Bounds/LOD:** no animation-aware `RenderBounds` management and no LOD anywhere in the animation path (searched the animation systems and components for bounds writes — none). Quads move small distances so baked bounds mostly hold **[inference]**, but big keyframe offsets could cull visibly.

---

## 8. Preserve / Replace / Absorb verdicts

**Preserve** = usable as-is · **Absorb** = concept/design survives, reimplemented in the package · **Replace** = superseded outright.

| Existing element | Verdict | Rationale |
|---|---|---|
| `AnimationClipSO` track/keyframe schema (`AnimationClipSO.cs`) | **Absorb** | Right shape (normalized time, per-target tracks, property flags); needs stable IDs instead of enum keys + bake validation |
| `AnimationLibrarySO` (flat list, linear `GetClip`) | **Replace** | Enum-ordinal registry with no dedup; package needs ID/hash-keyed registry with duplicate detection |
| `KeyframeSO` / `DOTSKeyframe` (`KeyframeSO.cs`) | **Replace** (delete) | Orphaned legacy; zero references (§2.6) |
| `AnimationBlobs.cs` blob layout | **Absorb** | Solid Burst layout incl. bake-resolved per-key interpolation; re-key by clip ID, add validated/sorted guarantees |
| `AnimationLibraryBakingSystem` | **Absorb** | Pre-filled-slot pattern is good; add dedup, validation, deterministic multi-library handling, fix multi-holder dispose |
| `AnimationLayer` buffer + `SetAnimation`/`AnimationRequest` API + `AnimationUtils` | **Absorb** | The right public surface; extend with blend params (the `allowBlend*` data already exists) and generalize layer set |
| `AnimationTimeSystem` (ungated timers, loop/clamp) | **Absorb** | Keep the ungated-timer design; replace the duration-0/`float.MaxValue` completion hack with a real clip-finished event |
| `AnimationSamplingSystem` composition (claim mask, reverse layer walk) | **Absorb** | Core algorithm keeps; fix additive-over-lower-layer semantics, per-entity sample phase, apply-stage parity with editor |
| `ApplyAnimatedPoseSystem` | **Absorb** | Trivial but must gain scale/flip via `PostTransformMatrix` (currently dropped, §2.1) |
| `AnimationSoundMarkerSystem` (wrap-correct marker crossing) | **Absorb** | Generalize to typed animation events; keep the wrap math verbatim |
| `ImageIndex` + `UpdateImageIndexSystem` two-hop indirection | **Replace** | Dirty flag never clears; collapse to direct `ImageIndexOverride` writes (or fix and justify the staging hop) |
| `ImageIndexOverride`/`BodyPartTint`/`Secondary`/`Tertiary` `[MaterialProperty]` components | **Preserve** | Names and semantics match shipped shaders; keep the exact property contract (`_ImageIndex`, `_BaseColor`, …) |
| `BillboardSystem` | **Absorb** | Behavior (incl. dead-yaw-freeze) is wanted; inject camera forward + life-state predicate instead of `Camera.main`/`Dead` |
| `UnitAnimationAssignmentSystem` | **Replace** (stays game-side) | Pure game logic; the package exposes the request API, the game keeps state→clip mapping |
| `UnitFaceDirectionSystem` + Direction layer + 8 direction `AnimationType`s | **Replace** (delete/redesign) | Dead code; direction handling should be a package feature (per-direction clip sets) or explicit game logic |
| `BodyPart`/`BodyPartInfo` rig registry + `BodyPartInitSystem` spawn remap | **Absorb** | The LinkedEntityGroup rebuild is the proven fix for ECB remap (§3.3); package version drops design/ragdoll flags |
| `CharacterRigAuthoring` / `BodyPartAuthoring` (animation slice) | **Absorb** | Split the animation concerns out of the design/ragdoll mega-bakers |
| `CameraVisible` presentation gating pattern | **Absorb** | Keep gated-presentation/ungated-timer split as an optional pluggable visibility hook |
| `AnimationClipEditorWindow` (IMGUI timeline) | **Replace** | Feature list is the spec; rebuild in UI Toolkit with full Undo and edit-mode preview |
| `AnimationPreviewController` + `EditorAnimationTimeControl` seam | **Absorb** | The MonoBehaviour↔singleton bridge works; move into an editor-only asmdef, drop play-mode requirement |
| `EditorAnimationSystem` (live SO sampling, no rebake) | **Absorb** | Best-in-class authoring loop; unify sampler code with runtime (single shared sampler, two data sources) to kill divergence |
| `AnimationClipUtilities` (mirror/duplicate) | **Absorb** | Keep workflow; the left/right mirror table must come from user config, not a hard-coded enum switch |
| 2D array/packed shader graphs + sprite HLSL nodes | **Preserve** | Shipped, batching-friendly, per-instance contract stable; package consumes the property names rather than owning the graphs |
| `GameSettings.animationFrameRate` global | **Replace** | Becomes package config (per-world or per-rig), not a save-file field |

---

## 9. Open questions for the Architect

1. **Clip identity:** enum ordinals are load-bearing today (blob index, serialized assets). Migrate to string-hash IDs (e.g. `TypeHash`-style) with a baked remap table, or keep a user-supplied enum via generics? What is the migration story for the ~21 existing clip assets?
2. **Blend-in/out:** the data fields exist but no runtime. Is cross-fade per layer (two active clips per layer slot with weights) in scope for v1, or is the pop acceptable and the fields should be cut?
3. **Additive semantics:** confirm intended behavior — additive over the *composited lower layers* (docs) vs additive over *rest pose* (code). This changes the sampler inner loop and every existing clip's on-screen result.
4. **Scale/flip:** apply via `PostTransformMatrix` (keeps `LocalTransform` uniform-scale clean, matches what's already baked) or via `LocalTransform.Scale` + separate flip handling? Mirror Clip currently produces visually-identical output to the original because scale is dropped — is any shipped content depending on that accident?
5. **Completion signaling:** what replaces the `AttackRequest`-via-comment contract — an enableable `ClipFinished` per layer, an event buffer, or a queryable normalized-time API?
6. **Event system scope:** generalize `SoundMarker` to typed markers (sound today; hit-frames, VFX, footsteps tomorrow)? If combat moves to hit-frame markers, the delta-time hit timing in `AttackRequestSystem` changes owners.
7. **Direction/8-way facing:** dead today. Does the package take responsibility for directional clip selection (per-direction clip sets, auto-flip via mirror), or stay agnostic?
8. **Visibility/culling boundary:** does the package define its own visibility tag + provider interface, or accept an externally-owned enableable (as `CameraVisible` is today) with the timer-ungated contract documented?
9. **Frame-rate quantization:** keep the retro fixed-rate sampling look? If yes — per-rig rate + per-entity phase offset, or global? (Current global counter makes all rigs sample the same tick, §3.4.)
10. **Editor preview world:** rebuild preview on a dedicated editor `World` (no play mode, no `GameSceneTag` furniture) — is depending on baking-in-edit-mode acceptable, or should preview sample SOs into a purely procedural rig?
11. **Shader ownership:** does the package ship reference shaders (subgraph nodes for slice-sampling + per-instance contract) or only document the property contract (`_ImageIndex` float, per-instance) that user shaders must satisfy? Motion-vector handling for slice-flips is currently unverified (§5) — in scope?
12. **`_UseAltShape`:** the view-switching hair shader's per-instance switch has no ECS component yet (§5). Package concern (generic per-instance float channel) or game concern?
13. **Bounds:** should the package compute conservative `RenderBounds` from clip data at bake (max keyframe offsets per part) to make large-offset animations cull-safe?
14. **Tests:** the sampler/easing/wrap math is currently untested (§7). Agree the package treats the sampler as pure functions with EditMode coverage from day one (project `dots-test` conventions)?

---

### Files audited (primary evidence set)

Editor tool: all 9 files in `Assets/_Scripts/Editor/AnimationEditor/`. Data: `Data/SOs/KeyframeSO.cs`, `AnimationClipSO.cs`, `AnimationLibrarySO.cs`; `Data/Structs/AnimationBlobs.cs`, `UnitBlob.cs`; `Data/Enums/AnimationEnums.cs`. Systems: all files in `Systems/AnimationSystemGroup/` (both subgroups), `Systems/PostBakingSystemGroup/AnimationLibraryBakingSystem.cs`, `CharacterRigBakingSystem.cs`, `Systems/SystemGroups.cs`, `CombatSystemGroup/CombatExecutionSystemGroup/AttackRequestSystem.cs` (excerpt), `StateMachineSystemGroup/ActionExecutionSystemGroup/BehaviorExecutionSystem.cs` (excerpt). Components: `Components/Animation/AnimationComponents.cs`, `Components/EntityLibraries/EntityLibraries.cs`, `Components/Units/BodyPartComponents.cs`, `CameraVisibilityComponents.cs`, `Components/Save/GameDataComponents.cs`, `Components/Tags/SceneTags.cs`. Authoring: `Authoring/Units/CharacterRigAuthoring.cs`, `BodyPartAuthoring.cs`, `Authoring/Animation/BillboardAuthoring.cs`, `ImageIndexAuthoring.cs`, `Authoring/EntityLibraries/AnimationLibraryAuthoring.cs`, `Authoring/Save/GameDataAuthoring.cs` (excerpt). Utils: `Utils/AnimationUtils.cs`. Shaders: all 7 production `.shadergraph` files (property/target JSON), `Nodes/Sprite/*.hlsl`. Docs verified against code: `Assets/CLAUDE.md`, `_Vault/Memories/Code/Systems_Animation.md`, `Data.md`, `Shaders.md`, `Editor.md`, `RULES.md`, `Contracts.md`, `Gotchas.md`. Versions: `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`, `Packages/packages-lock.json`.
