---
tags: [memory, code, authoring, baking]
related: "[[RULES]], [[Components]], [[Data]], [[Systems]]"
---

# Authoring — Context

Authoring scripts are **MonoBehaviours with nested Baker classes**. They exist only to convert scene/prefab data into ECS entities at bake time. No authoring script runs at runtime. See [[RULES]] for the underlying ECS/DOTS conventions.

---

## Baker Pattern

```csharp
public class FooAuthoring : MonoBehaviour {
    public float someValue;

    public class Baker : Baker<FooAuthoring> {
        public override void Bake(FooAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new FooComponent { Value = authoring.someValue });
        }
    }
}
```

- Use `GetEntity(TransformUsageFlags.None)` for static/data-only entities.
- Use `GetEntity(TransformUsageFlags.Dynamic)` for entities that move.
- Call `AddBuffer<T>(entity)` for `IBufferElementData` — see [[Components]] for buffer types.
- Use `DependsOn(so)` when baking from a ScriptableObject reference so incremental baking works correctly. See [[Data]] for the full SO → BlobAsset pipeline.

---

## Unit Prefab Structure

A unit is **two linked prefabs**:

- **Body prefab** — contains the visual hierarchy (layered quads), `UnitAuthoring`, `CharacterRigAuthoring` (root) with a `BodyPartAuthoring` on each part GO, `UnitMoverAuthoring`, `HealthAuthoring`, `AttackAuthoring`, etc. This is what moves and gets animated.
- **Brain prefab** — contains `CitizenBrainAuthoring` (or equivalent), motivation components, action buffer. This is what makes decisions.

They are linked at bake time via `BrainLinkAuthoring` / `BodyLinkAuthoring`, which store a cross-reference entity on each side. See [[Systems_AI]] for Brain/Body runtime behaviour.

**When adding a new unit type:**
1. Duplicate an existing brain prefab and swap `CitizenBrainAuthoring` for the appropriate brain authoring.
2. Duplicate an existing body prefab and assign the correct `UnitSO` on `UnitAuthoring`.
3. Add the new `UnitType` enum value — see [[Data]] for enum conventions.
4. Add a `UnitSO` asset under `Assets/ScriptableObjects/Units/`.
5. Register it in `UnitLibraryAuthoring` so it gets baked into the `UnitLibraryBlob`.
6. Add an entry to the `AnimationLibrarySO` for any new animation clips — see [[Systems_Animation]] for the animation data pipeline.

---

## Key Authoring Files

| File | Purpose |
|---|---|
| `UnitAuthoring.cs` | Core unit identity — links to UnitSO, sets UnitType |
| `CharacterRigAuthoring.cs` | **Root of a character rig** (CharacterRig refactor — replaces `AnimatorAuthoring` + `Ragdoll2DAuthoring` + `DesignAuthoring`). Bakes starting `AnimationLayer` buffer + `SetAnimation` + `AnimationRequest`, ragdoll root config (`Ragdoll2DConfig` + `Ragdoll2DLaunch`), design state (`RandomizeDesign` + `PersistedDesign` + `CharacterPalette` + `ChangeDesignRequest`), empty `BodyPart` buffer, `CharacterRigConfig`, and `DesignReloadOnBake` when `reloadDesign` is set |
| `RagdollSimConfigAuthoring.cs` | Bakes the `RagdollSimConfig` singleton from a `RagdollConfigSO` (flat flatten — no blob). ONE instance in the game subscene; systems fall back to identical built-in defaults when it's absent, so it's a tuning seam, not a functional gate |
| `BodyPartAuthoring.cs` | **One per rig part GO** (replaces `AnimationTargetAuthoring` + `AnimationTargetNoIndexAuthoring` + `BaseParentAuthoring` on parts). Bakes `BodyPartInfo` (target + `PartDefinitionSO`.id + role flags), `BaseParent`, the animation pose set, `ImageIndex`/`ImageIndexOverride` **only when the GO renders**, and `RagdollJointBakeData` when `isRagdollJoint`. ⚠ remove the old `BaseParentAuthoring` from parts to avoid a duplicate `BaseParent` bake error |
| `CitizenBrainAuthoring.cs` | Bakes motivation defaults and brain identity |
| `InteractionAuthoring.cs` | Bakes `Interaction { action = actionType }` (+ optional `PlayerInteractable`) — the action keys into the enum-indexed `InteractionLibrary` blob; spatial hash registers the entity under the blob's `satisfiedNeed` |
| `UnitSpawnerAuthoring.cs` | Configures the spawner with prefab references |
| `UnitLibraryAuthoring.cs` | Bakes all UnitSOs into a unified BlobAsset |
| `PartLibraryAuthoring.cs` | Place on one scene GO. `DependsOn(library)`, bakes `PartLibrary` + `PartLibraryReference` (→ `PartLibraryBakingSystem` builds the blob). Mirrors `ItemLibraryAuthoring` |
| ~~`Ragdoll2DAuthoring.cs`~~ / ~~`DesignAuthoring.cs`~~ / ~~`AnimatorAuthoring.cs`~~ / ~~`AnimationTargetAuthoring.cs`~~ / ~~`AnimationTargetNoIndexAuthoring.cs`~~ | **Deleted** in the CharacterRig refactor — replaced by `CharacterRigAuthoring` (root) + `BodyPartAuthoring` (parts) + `PartLibraryAuthoring` (scene). Existing prefabs/subscenes must be re-authored (see `Tasks/Verification/verify-characterrig.md`) |
| `ItemAuthoring.cs` | Bakes item identity + `ThrownItem` with per-item `throwSpeed`, `throwArc`, `throwDamage` |
| `Hazards/HazardAuthoring.cs` | Proximity damage zone (spike-trap example, v2 environmental damage). Plain authored fields `damageAmount`/`radius`/`retriggerInterval` (+ kill-knockback feel); `TransformUsageFlags.Dynamic` for position; bakes `HazardZone` (`damageSource = Hazard`, `lastTriggerTime = -inf`). Read by `HazardZoneSystem` |
| `PlayerAuthoring.cs` | Bakes player entity; assign `aimIndicator` child GO for the aim arrow visual |
| `GameDataAuthoring.cs` | Bakes the GameData singleton entity — place one in every game scene. Inspector exposes `autoSaveIntervalSeconds` and `animationFrameRate`. Adds `GameDataTag`, `SaveRequest`, `LoadRequest`, `AutoSaveTimer`, `PlayTimeTracker`, `GameSettings`, `PlayedDialogue` buffer, `DialogueFlag` buffer |
| `DialogueManagerAuthoring.cs` | Place ONE per scene that uses dialogue. Bakes the DialogueManager singleton entity with `DialogueManagerTag`, `ActiveDialogue` (disabled), `OnDialogueEvent` (disabled) |
| `DialogueProviderAuthoring.cs` | Add to an NPC GO to give it player-triggerable dialogue. Assign a `DialogueSequenceSO`. Bakes `DialogueProvider` (enabled) + `PlayerInteractable` (unless `InteractionAuthoring` with `playerInteractable=true` is also present) |
| `SoundLibraryAuthoring.cs` | Place on one scene GO. `DependsOn(library)`, bakes `SoundLibrary` + `SoundLibraryReference` (→ `SoundLibraryBakingSystem` builds the blob). Mirrors `ItemLibraryAuthoring` |
| `AmbientSoundAuthoring.cs` | Place on a world emitter (machine/fire/wind). Bakes `LoopingSound { type, volumeMul, pitchMul }` (enabled by `startEnabled`). AudioManager maps the entity→voice and stops it when disabled/destroyed |

---

## Cross-Entity Baking Pattern

A Baker can **only** call `AddComponent` / `AddBuffer` on the entity returned by `GetEntity()` for its **own** GameObject. Calling these on a different GO's entity throws `InvalidOperationException: Entity doesn't belong to the current authoring component`.

**Pattern for distributing components to child entities at bake time:**

1. Baker writes only to its own root entity (config + entity refs).
2. A `[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]` system in `PostBakingSystemGroup` reads the config, iterates child entities, and calls `em.AddComponentData` on them. See [[Systems]] for PostBakingSystemGroup placement.
3. Collect adds into a `NativeList` during the query — **do not call `em.AddComponentData` inside `SystemAPI.Query` iteration** (structural change during query = exception). See [[Gotchas]] for the full trap.

`Ragdoll2DRootAuthoring` + `Ragdoll2DBakingSystem` is the reference implementation of this pattern.
