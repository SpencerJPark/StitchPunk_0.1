---
tags: [memory, code, gotchas, bugs, patterns]
related: "[[Authoring]], [[Systems]], [[Components]], [[Systems_AI]]"
---

# Gotchas — Non-Obvious Traps

Things that are not derivable from reading the code at a glance. Read this before debugging anything related to spawning, AI startup, or component lookups.

---

## Spawning

### Entity refs inside dynamic buffers are not remapped by ECB.Instantiate
Entity references stored inside a `DynamicBuffer` (today: the root `BodyPart` buffer's child-part
refs) are NOT reliably remapped by `ECB.Instantiate`. This killed the pre-rig `AnimatorTarget`
buffer and the rig commit inherited the same physics.

**Fix in place:** `BodyPartInitSystem` (SpawnInitSystemGroup) rebuilds the `BodyPart` buffer on
every `NewlySpawned` unit from `BodyPartInfo` + `BaseParent`, walking `DynamicBuffer<LinkedEntityGroup>`
— the actual remap table `ECB.Instantiate` uses, so it is correct regardless of prefab nesting.
(The pre-rig `AnimatorAuthoring`/`AnimatorTargetInitSystem` version of this fix was deleted with
the CharacterRig commit; the lesson stands.)

**Consequence:** never trust baked entity refs inside buffers on the exact spawn frame; rebuild
from `LinkedEntityGroup` in `SpawnInitSystemGroup` (see the Spawn Init Pattern in [[Systems]]).

---

### BrainLink is not baked onto body prefabs
The body prefab has no `BrainLinkAuthoring`, so `BrainLink` does not exist on baked body entities. `UnitSpawnerSystem` adds it via ECB after instantiating both body and brain. See [[Components]] for the `BrainLink`/`BodyLink` component definitions.

**Consequence:** Do not query body entities for `BrainLink` during the same ECB playback frame they were spawned. It will exist from the next frame onward.

---

### Enableable bits are not reliably copied by ECB.Instantiate (spawn AND pool reclaim)
`IEnableableComponent` enabled bits are not guaranteed to be copied correctly by `ECB.Instantiate`
for entities that are not the root of a `LinkedEntityGroup`, and a reclaimed pool unit keeps
whatever bits its previous life left behind. `SpawnStateInitSystem` (SpawnInitSystemGroup) is the
single reset point: it forces every root-entity enableable to its spawn-frame default
(`Dead` off, `UtilityBrain` on, requests off — see its table in [[Systems]]).

**Consequence:** a new enableable component with a required spawn default gets a line in
`SpawnStateInitSystem`, not an ad-hoc set in the spawner. (The pre-rig `NeedsAction` version of
this trap is gone — `NeedsAction` only survives in `Core/Unused/`.)

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

### Motivations are ONE buffer, keyed by NeedType
Motivations live in a single `DynamicBuffer<Motivation>` on the brain, keyed by `NeedType` —
the old one-component-per-motivation model (`HungerMotivation` etc.) is gone. Mutate them via the
`MotivationChangeRequest` buffer (consumed by `MotivationChangeRequestSystem`), never by writing
the buffer from another feature. See [[Systems_AI]] for the needs-based scoring pipeline.

---

## ECB Ordering

### ECB.Playback is deferred — structural changes are not visible in the same OnUpdate
Any components added via ECB in `OnUpdate` are not visible to `SystemAPI.Query<>` until the ECB is played back. Plan system ordering accordingly: systems that need to see newly spawned components must run in a later frame or later group. See [[Systems]] for the full execution order.

---

## Brain / Body Cross-Reference Pattern

When an execution system needs data from the other side of the Brain/Body split:

```csharp
// From body → brain:
Entity brainEntity = SystemAPI.GetComponent<BrainLink>(bodyEntity).brain;

// From brain → body:
Entity bodyEntity = SystemAPI.GetComponent<BodyLink>(brainEntity).body;
```

Keep these lookups in execution systems only. Scoring systems should query brain entities directly and never touch the body. See [[Systems_AI]] for Brain/Body split architecture.

---

## IEnableableComponent vs AddComponent/RemoveComponent

For components that toggle frequently (e.g. `AttackRequest`, `PathRequest`, `Dead`), use `SetComponentEnabled<T>()` — it avoids structural changes and is much cheaper. Only use `AddComponent`/`RemoveComponent` for components that are genuinely absent (e.g. adding `BrainLink` to a body that never had it). Full list of enableable components is in [[Components]].

---

## Baking

### Baker can only AddComponent on its own GO's entity
A `Baker<T>` may only call `AddComponent` / `AddBuffer` on entities obtained from `GetEntity()` for the **Baker's own GameObject**. Calling these on a different GO's entity (e.g. a child or drag-in reference) throws:
```
InvalidOperationException: Entity doesn't belong to the current authoring component
```

**Fix:** Write only config + entity refs to the root entity in the Baker. Use a `[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]` system in `PostBakingSystemGroup` to distribute components to child entities after bake. See [[Authoring]] for the cross-entity baking pattern.

See `Ragdoll2DRootAuthoring` + `Ragdoll2DBakingSystem` as the reference implementation.

### Structural changes are not allowed during SystemAPI.Query iteration
Calling `em.AddComponentData` or `em.AddComponent` inside a `foreach (... in SystemAPI.Query<>())` loop throws:
```
InvalidOperationException: Structural changes are not allowed while iterating over entities
```

**Fix:** Collect entities and component values into a `NativeList<(Entity, ComponentData)>` during the query, then apply all adds after the loop. Dispose the list when done.

---

## Fake Ragdoll — Known Issues (WIP)

Ragdoll components are documented in [[Components]]. Issues below are open bugs.

### Ground clamp must compare JOINT world Y, not root world Y
The ground clamp logic in `Ragdoll2DSystem` needs to compare the **joint entity's** `LocalToWorld.Position.y` against `rootWorldY + groundBuffer`. An earlier version compared `root.LocalToWorld.y` against the same expression — since both sides derived from the root, the condition was always true and velocity was zeroed every frame (joints never moved).

**Always use `.WithEntityAccess()` on the joint query and check `localToWorldLookup[jointEntity].Position.y`.**

### Visual child Y offset is not accounted for — clips through ground (OPEN BUG)
When the visual root child tilts on Z, its top swings downward toward the ground. Because the visual child's Y position above the root is not factored into the tilt, the top of the character (and its limbs) can intersect the ground plane during the fall.

**Root cause:** The Z-tilt pivot is at the visual child's local origin (which is at the root/feet level), but the character geometry extends upward from there. A 90° tilt rotates the top of the character down to ground level and below.

**Possible fix direction:** Translate the visual child upward by half its height when tilting, so the rotation pivot is at the character's center of mass. Or raise the root entity's Y by the same amount on death.

### Joints return to rest pose after settling (OPEN BUG)
Once `zAngularVelocity` decays near zero and the ground clamp nudge-back fires (`nextAngle *= 0.5f`), repeated small halving drives `currentZAngle` toward 0 — which visually looks like the joints are snapping back to their rest rotation.

**Possible fix direction:** Once velocity is fully damped and no ground contact is happening, freeze `currentZAngle` at its current value instead of continuing to nudge it. Add a "settled" flag or a minimum velocity threshold below which no further angle updates are applied.
