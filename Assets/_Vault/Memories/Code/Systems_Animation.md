---
tags: [memory, code, systems, animation]
related: "[[Systems]], [[Components]], [[Data]]"
---

# AnimationSystemGroup — Context

There is **no Unity Animator**. All animation is driven by keyframe data baked from ScriptableObjects. Part of the larger [[Systems]] execution pipeline.

---

## Unit Visual Structure

Units are **layered quads** — flat meshes parented in a hierarchy, each representing a body part (`AnimationTarget` enum). Animation moves, rotates (Z-axis), or changes the texture index of these quads.

Textures are **texture arrays** — multiple frames packed into one array asset. This lets the GPU instance the same material across hundreds of characters while varying per-character appearance via `MaterialPropertyBlock`.

---

## Animation Data Pipeline

```
AnimationClipSO
  └── PartTrack[]  (one per AnimationTarget)
        └── Keyframe[]  (time, value, interpolation type, blend mode)

AnimationLibrarySO
  └── maps AnimationType enum → AnimationClipSO

AnimationLibraryBakingSystem  (PostBakingSystemGroup)
  └── converts SO data → BlobAsset<AnimationLibraryBlob>
      stored as singleton component on a library entity
```

SOs and blob structs are documented in [[Data]]. Systems never touch SOs at runtime — always use the blob.

---

## Animation Layers (AnimationLayerType enum)

7 layers evaluated in order. Later layers can override or additively blend on top:

| Layer | Purpose |
|---|---|
| `Base` | Idle/walk/run cycle |
| `Direction` | 8-directional facing offset |
| `Action` | Attack, interact, etc. |
| `Face` | Overall facial expression |
| `Eyes` | Eye state / blink |
| `Mouth` | Mouth shape / talking |
| `Override` | Force-overrides everything (cutscenes, death) |

Each entity has one `AnimationLayer` buffer element per active layer. Buffer capacity is 8. Full component definitions in [[Components]].

---

## Blend Modes & Interpolation

Per-keyframe settings:
- **Blend mode**: `Override` (replaces) or `Additive` (adds on top of lower layers)
- **Interpolation**: 5 types (Linear, Step, EaseIn, EaseOut, EaseInOut)

---

## Execution Pipeline

```
AnimationAssignmentSystemGroup
  UnitAnimationAssignmentSystem  — decides which AnimationType to play per layer
  UnitFaceDirectionSystem        — sets Direction layer based on velocity

AnimationExecutionSystemGroup
  AnimationTimeSystem            — advances elapsed time, handles loop/clamp
  AnimationSamplingSystem        — samples keyframes at current time
  ApplyAnimatedPoseSystem        — writes position/rotation/scale to quad transforms
  UpdateImageIndexSystem         — writes texture array index to MaterialPropertyBlock
  BillboardSystem                — rotates root quad to always face camera
```

### File Paths (relative to `_Scripts/Systems/AnimationSystemGroup/`)

| System | File |
|---|---|
| `UnitAnimationAssignmentSystem` | `AnimationAssignmentSystemGroup/UnitAnimationAssignmentSystem.cs` |
| `UnitFaceDirectionSystem` | `AnimationAssignmentSystemGroup/UnitFaceDirectionSystem.cs` |
| `AnimationTimeSystem` | `AnimationExecutionSystemGroup/AnimationTimeSystem.cs` |
| `AnimationSamplingSystem` | `AnimationExecutionSystemGroup/AnimationSamplingSystem.cs` |
| `ApplyAnimatedPoseSystem` | `AnimationExecutionSystemGroup/ApplyAnimatedPoseSystem.cs` |
| `UpdateImageIndexSystem` | `AnimationExecutionSystemGroup/UpdateImageIndexSystem.cs` |
| `BillboardSystem` | `AnimationExecutionSystemGroup/BillboardSystem.cs` |

---

## AnimatorTarget Buffer — Spawn Gotcha

`DynamicBuffer<AnimatorTarget>` holds entity refs to the quad child entities. **These entity refs are NOT reliably remapped by `ECB.Instantiate`.** See [[Gotchas]] for the full history and root cause.

**Two-part fix:**

1. `AnimatorAuthoring.Baker` populates `AnimatorTarget` at bake time via `GetComponentsInChildren`. This gives scene entities a correct buffer permanently (they never get `NeedsAnimatorInit`).

2. For spawned (prefab) entities: `UnitSpawnerSystem` adds `NeedsAnimatorInit`. `AnimatorTargetInitSystem` clears and rebuilds the buffer using `DynamicBuffer<LinkedEntityGroup>` — the exact remapping table `ECB.Instantiate` produces. This is guaranteed correct regardless of how `characterRoot` is set in the inspector or how deep the prefab is nested.

`AnimationSystemGroup` runs before `SpawnSystemGroup`, so spawned entities have an unreliable `AnimatorTarget` on their spawn frame. From frame 2 onward the buffer is correct.

---

## Adding a New Animation

1. Create an `AnimationClipSO` under `Assets/ScriptableObjects/Animations/`.
2. Add `PartTrack` entries for each `AnimationTarget` quad you want to move.
3. Add keyframes with timing, target values, interpolation, and blend mode.
4. Open `AnimationLibrarySO` and register the new clip against an `AnimationType` enum value (add a new enum value if needed). Enums are in [[Data]].
5. The baking system will pick it up automatically on next bake.

Use the custom **Animation Editor** (`Editor/AnimationEditor/`) to preview clips without entering play mode.
