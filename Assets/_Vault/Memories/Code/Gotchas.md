---
tags: [memory, code, gotchas, bugs, patterns]
related: "[[Authoring]], [[Systems]], [[Components]], [[Systems_AI]]"
---

# Gotchas — Non-Obvious Traps

Things that are not derivable from reading the code at a glance. Read this before debugging anything related to spawning, AI startup, or component lookups.

---

## Shaders

### An open Shader Graph window silently overwrites scripted .shadergraph edits
Scripted graph surgery (the `shader-edit` skill's `shadergraph_lib.py`) writes the `.shadergraph`
file directly. If that graph is **open in the Shader Graph editor**, the editor's in-memory copy
wins the next time anything flushes it — your edits vanish or, worse, partially survive.

Observed on `PainterlyShader` (2026-07-28): an edge into `Luminance Ramp UV` was re-sourced,
`SurfaceDescription.Alpha` lost its input entirely, `_IsInteractable` flipped exposed→hidden, and a
stray PropertyNode disappeared — none of which the surgery script did (replaying it on the same
backup produced the correct result).

**Close the Shader Graph window before scripted surgery; reopen after.**

### A disconnected master-stack block still reports VALIDATION: ALL CLEAN
`validate_shadergraph.py` checks referential integrity, not that the shader is *wired sensibly*. A
`SurfaceDescription.Alpha` block with **zero inputs** validates clean and silently renders everything
opaque. After any graph edit, explicitly check each `BlockNode`'s incoming edges — see [[Shaders]].

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

## IEnableableComponent vs AddComponent/RemoveComponent

For components that toggle frequently (e.g. `AttackRequest`, `PathRequest`, `Dead`), use `SetComponentEnabled<T>()` — it avoids structural changes and is much cheaper. Only use `AddComponent`/`RemoveComponent` for components that are genuinely absent (a component the entity genuinely never had). Full list of enableable components is in [[Components]].

### An `EnabledRefRW<T>` parameter silently makes the job **skip entities where T is disabled**

An `EnabledRefRW<T>` / `EnabledRefRO<T>` parameter on an `IJobEntity.Execute` (or in
`SystemAPI.Query<>`) does **not** merely give you write access to the enabled bit — it also enrols
`T` in the generated query as an **All** component, which for an enableable type means
*enabled-only*. So a job that takes `EnabledRefRW<Dead>` to **set** `Dead` runs only on entities that
are already dead, and does nothing at all.

There is no compile error and no warning. The job just quietly matches a subset — often almost
nothing — and every symptom points somewhere else.

Fix: name the component in `[WithPresent(typeof(T))]` (or `.WithPresent<T>()`), which is the only
option meaning "present, enabled or not". `[WithDisabled]` and `[WithAll]` are both filters.

```csharp
[BurstCompile]
[WithAll(typeof(AnimationCommandPending))]            // deliberately enabled-only: the work gate
[WithPresent(typeof(BoundsDirty))]                    // required — we WRITE this bit, both ways
internal partial struct ApplyAnimationCommandsJob : IJobEntity
{
    private void Execute(
        EnabledRefRW<AnimationCommandPending> animationCommandPendingEnabled,
        EnabledRefRW<BoundsDirty> boundsDirtyEnabled) { /* ... */ }
}
```

Rule of thumb: **if the job only ever turns the bit off, `[WithAll]` is right (and is the default).
If it ever turns the bit on, you need `[WithPresent]`.** Source: the Entities source generator treats
every iterable enableable type as an `All` component unless a `WithAny`/`WithNone`/`WithDisabled`/
`WithPresent` names it (`SystemGenerator.SystemAPI.Query/IfeDescription.cs`).

Found 2026-08-02 in the DOTS Animation Toolkit's `CommandApplySystem` / `PlaybackTimeSystem`, both of
which write `BoundsDirty` in both directions.

---

## Baking

### Baker can only AddComponent on its own GO's entity
A `Baker<T>` may only call `AddComponent` / `AddBuffer` on entities obtained from `GetEntity()` for the **Baker's own GameObject**. Calling these on a different GO's entity (e.g. a child or drag-in reference) throws:
```
InvalidOperationException: Entity doesn't belong to the current authoring component
```

**Fix:** Write only config + entity refs to the root entity in the Baker. Use a `[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]` system in `PostBakingSystemGroup` to distribute components to child entities after bake. See [[Authoring]] for the cross-entity baking pattern.

See `RagdollJointAuthoring` + `CharacterRigBakingSystem` as the reference implementation.

### Structural changes are not allowed during SystemAPI.Query iteration
Calling `em.AddComponentData` or `em.AddComponent` inside a `foreach (... in SystemAPI.Query<>())` loop throws:
```
InvalidOperationException: Structural changes are not allowed while iterating over entities
```

**Fix:** Collect entities and component values into a `NativeList<(Entity, ComponentData)>` during the query, then apply all adds after the loop. Dispose the list when done.

---

## Ragdoll — Gotchas (2026-07 rework)

Ragdoll components are documented in [[Components]]; the system lives in `RagdollSystemGroup`
(`Tasks/Verification/Ragdoll2D_System.md`). The pre-rework "known issues" (joint ground clamp,
settle-to-rest drift) are gone with the old per-joint velocity/clamp code.

### ApplyPoseJob stomps corpse poses — sleeping corpses must still re-write rotations
`ApplyAnimatedPoseSystem.ApplyPoseJob` writes EVERY entity with `AnimationTargetPose` +
`LocalTransform` each frame — including all parts of dead units. The ragdoll driver runs later in
the frame and overwrites, which is why it must keep re-asserting rotations even when a corpse is
`sleeping` (dynamics skipped, pose write kept). If corpses ever snap back to their animated pose,
something broke this ordering.

### The CorpseCells map bypasses ECS dependency tracking
`CorpseCells` hands a `NativeParallelMultiHashMap` through a singleton. `CorpseCellSystem` clears
it each frame on the main thread — any job reading it MUST register via
`CorpseCellSystem.AddJobHandleForReader` (Ragdoll2DSystem does), or the clear races the read.
Same pattern and reason as `DamageBusSystem.AddJobHandleForProducer`.

### Visual child Y offset is not accounted for — clips through ground (OPEN BUG, pre-existing)
When the visual root child tilts on Z, its top swings downward toward the ground. The Z-tilt pivot
is at the visual child's local origin (root/feet level) but the geometry extends upward, so a 90°
tilt rotates the top of the character to ground level and below.

**Possible fix direction:** Translate the visual child upward by half its height when tilting, so
the rotation pivot is at the character's centre of mass. Or raise the root entity's Y on death.

---

## DOTS `[MaterialProperty]` colours skip sRGB→linear — convert with `.linear`

A `Color` written into a `[MaterialProperty("_BaseColor")]` component (e.g. `BodyPartTint`) is uploaded to the GPU as a **raw `float4`**. Unlike the material inspector — which auto-converts Shader Graph colour properties from sRGB to linear — the DOTS instancing path does **no conversion**. In a Linear-colour-space project (this one: `m_ActiveColorSpace: 1`) that makes every tint too bright / washed-out (a mid-tone sRGB `0.5` should be linear `~0.21`), so a multiply-tint looks weak and "whiter than expected."

**Fix:** bake `authoring.tintColor.linear` (not the raw `.r/.g/.b`) into the `float4`. Any future runtime system that writes a `Color` into a material-property component must do the same `.linear` conversion. See `BodyPartAuthoring.Baker`.

---

## UI Toolkit — filler elements and unlaid-out sizes

Both bit the clip editor's ghost rows (`GhostLaneStripElement`, 2026-08-22) and neither throws.

### An element sized from the container it lives in grows without bound
A filler element that measures "the space left over" from its own parent is asking a question it is
part of the answer to. Inside a `ScrollView` the parent is content-sized, so each pass hands the
filler the room it just made for itself, the content outgrows the viewport, a scrollbar appears, and
it keeps going. **Measure `ScrollView.contentViewport` instead** — the pane fixes that, so the sum
settles on the first pass. Exclude the filler from the subtraction (make it a *sibling* of the
content stack, not a child).

### `resolvedStyle.height` is NaN before an element's first layout, and NaN loses every comparison
`if (height < 1f)` and `if (height <= 1f)` are both **false** for NaN, so an unmeasured element sails
past the guard and poisons whatever arithmetic follows — `Mathf.Max(0f, NaN)` returns NaN, and a NaN
written into `style.height` is not a size layout recovers from. Write the guard negated —
`if (!(height >= 1f))` — so NaN takes the safe branch. A size that must come from USS can only be
read off an element that already exists, so expect a two-pass settle: build one, let it lay out, then
read it in the `GeometryChangedEvent`.

### A panel that must cover something has to be parented into it
UI Toolkit paints siblings in document order, so a dropdown built inside a `Toolbar` overflows down
out of the bar and is painted **behind** the body element that follows it — invisible, not floating.
`position: absolute` does not change that; absolute positions against an ancestor, it does not raise
above a later sibling. To hang a panel over the thing below, parent it into *that* subtree, after the
element it should cover. The clip editor's validation list does this: an absolute, `picking-mode`
Ignore slot fills the viewport frame after the preview `Image`, and the panel sits inside it under
ordinary flex rules (`align-items: flex-end`, percentage `max-width`/`max-height`) so it stays a
corner of the 3D area at any pane size. `picking-mode` Ignore on the slot is not inherited — the
panel's own buttons still take clicks while a drag anywhere else reaches the viewport.

### One rule set, two renderers, and the un-dismissable one wins
`ClipRegistryBuilder` throws a `ClipValidationException` whose `.Message` is every offending rule on
its own line. Anything that puts an exception message straight into a wrapping status label has
built a second, permanent error list beside the real one — different wording, different order, and
no way to switch it off. Catch the validation exception *separately* from `Exception`: name the
problem in one sentence and point at the surface that lists it, and keep the full `.Message` only
for the unexpected throws that have nowhere else to report.
