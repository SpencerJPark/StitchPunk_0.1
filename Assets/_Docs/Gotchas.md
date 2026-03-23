# Gotchas — Non-Obvious Traps

Things that are not derivable from reading the code at a glance. Read this before debugging anything related to spawning, AI startup, or component lookups.

---

## Spawning

### AnimatorTarget buffer is not remapped by ECB.Instantiate
`DynamicBuffer<AnimatorTarget>` stores entity refs to quad child entities. `ECB.Instantiate` does NOT reliably remap these.

**Fix in place (two-part):**

1. `AnimatorAuthoring.Baker` now populates `AnimatorTarget` at bake time via `GetComponentsInChildren<AnimationTargetAuthoring>()` + `GetComponentsInChildren<AnimationTargetNoIndexAuthoring>()`. This gives scene entities a correct buffer from baking (they never get `NeedsAnimatorInit`).

2. `UnitSpawnerSystem` adds `NeedsAnimatorInit` to each new body. `AnimatorTargetInitSystem` (runs after in `SpawnSystemGroup`) clears and rebuilds the buffer using `DynamicBuffer<LinkedEntityGroup>` — NOT `BaseParent` matching. `LinkedEntityGroup` is the actual remapping table `ECB.Instantiate` uses, so it is guaranteed correct regardless of inspector setup or nested prefab depth.

**Why the old approach (BaseParent matching) was wrong:** It required every quad's `characterRoot` inspector field to be set to the exact body root GO. Any quad with `characterRoot` pointing to an intermediate parent (head, torso, etc.) would be silently skipped. This is why "only eyebrow animates" — only the eyebrow happened to have `characterRoot` set correctly.

**Consequence:** Any system that reads `AnimatorTarget` will see a potentially unreliable buffer on the exact spawn frame (AnimationSystemGroup runs before SpawnSystemGroup). From frame 2 onward the buffer is correct.

---

### BrainLink is not baked onto body prefabs
The body prefab has no `BrainLinkAuthoring`, so `BrainLink` does not exist on baked body entities. `UnitSpawnerSystem` adds it via ECB after instantiating both body and brain.

**Consequence:** Do not query body entities for `BrainLink` during the same ECB playback frame they were spawned. It will exist from the next frame onward.

---

### NeedsAction enabled bit is not reliably copied by ECB.Instantiate
`IEnableableComponent` enabled bits are not guaranteed to be copied correctly by `ECB.Instantiate` for entities that are not the root of a `LinkedEntityGroup`. `UnitSpawnerSystem` explicitly calls `ecb.SetComponentEnabled<NeedsAction>(newBrain, true)` to force it on.

**Consequence:** If a new brain type is added and its brain prefab instantiation doesn't explicitly enable `NeedsAction`, the AI scoring pipeline will never process it.

---

### Pool reclaim path re-enables NeedsAction too
When reclaiming a dormant pool unit (not instantiating), `UnitSpawnerSystem` also explicitly sets `NeedsAction` enabled on the brain. Do not assume it carries over from the previous activation.

---

## Component Lookups

### UnitPrefabEntry is not a true singleton — do not use GetSingleton<>
`UnitPrefabEntry` exists on the library entity AND on baked prefab entities. `SystemAPI.GetSingleton<UnitPrefabEntry>()` will throw because multiple entities match.

**Correct pattern:**
```csharp
Entity libraryEntity = SystemAPI.GetSingletonEntity<UnitDataLibrary>();
UnitPrefabEntry prefabs = SystemAPI.GetComponent<UnitPrefabEntry>(libraryEntity);
```

---

### Motivations are 9 separate components, not one
There is no single "Motivations" component. Each motivation is its own `IComponentData` (e.g. `HungerMotivation`, `EnergyMotivation`). Each scoring system queries exactly one motivation struct. If you need to read all 9, you must name all 9 in the query.

---

## Filenames

### FlowFeildSystem.cs has a typo
The file is `FlowFeildSystem.cs` (Feild, not Field). The system is correctly named `FlowFieldSystem` in code. Do not fix the filename without updating all `[UpdateInGroup]` / `[UpdateAfter]` references and the `.asmdef` if applicable.

### MotivationDegregationSystem.cs has a typo
The file and class are named `MotivationDegregationSystem` (Degregation, not Degradation). Match this spelling exactly when referencing it in `[UpdateAfter]` attributes.

---

## ECB Ordering

### ECB.Playback is deferred — structural changes are not visible in the same OnUpdate
Any components added via ECB in `OnUpdate` are not visible to `SystemAPI.Query<>` until the ECB is played back. Plan system ordering accordingly: systems that need to see newly spawned components must run in a later frame or later group.

---

## Brain / Body Cross-Reference Pattern

When an execution system needs data from the other side of the Brain/Body split:

```csharp
// From body → brain:
Entity brainEntity = SystemAPI.GetComponent<BrainLink>(bodyEntity).brain;

// From brain → body:
Entity bodyEntity = SystemAPI.GetComponent<BodyLink>(brainEntity).body;
```

Keep these lookups in execution systems only. Scoring systems should query brain entities directly and never touch the body.

---

## IEnableableComponent vs AddComponent/RemoveComponent

For components that toggle frequently (e.g. `NeedsAction`, `Attack`, `Alive`, `Dead`), use `SetComponentEnabled<T>()` — it avoids structural changes and is much cheaper. Only use `AddComponent`/`RemoveComponent` for components that are genuinely absent (e.g. adding `BrainLink` to a body that never had it).
