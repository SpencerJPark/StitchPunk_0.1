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

## A `ComponentLookup` cached before a structural change throws `ObjectDisposedException` after it — silently

A non-Burst `ISystem` that calls a managed API doing structural changes (e.g.
`CutscenePlaybackApi.CreatePlayRequestFromStage` — `CreateEntity`/`AddComponentData`/`AddBuffer`) and then
reuses a `ComponentLookup` obtained *before* that call throws `ObjectDisposedException: ... has been
invalidated by a structural change` the moment the lookup is touched. No compile error, and the throw
aborts the rest of `OnUpdate` — including any cleanup after the loop (a `DestroyEntity` call three lines
later silently never runs). The exception lands in the Editor console, not as a test assertion, so a
failing assert two calls downstream ("entity X still exists") is the only symptom in the test output itself.

**Fix:** keep the lookups as `OnCreate` fields and call `.Update(ref state)` on each right after the
structural change, before the next use — same shape as every Bursted job-scheduling system already does,
it just also applies to plain managed code that intermixes lookups with structural changes in one method.
Found 2026-09-04 in `CutsceneStartSystem` (G1): lookups for `CutsceneActor`/`ActionInterruptRequest`/
`PathRequest`/`Movement`/`LocalTransform` were built once at the top of `OnUpdate`, then used after
`CreatePlayRequestFromStage`'s structural change inside the same call.

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

## Ragdoll — Gotchas (2026-08-29: adopted the toolkit's ragdoll, replacing Ragdoll2D)

Ragdoll is now `com.dotsanimationtoolkit`'s own real-physics ragdoll (`RagdollActor`/`RagdollLaunch`/
`RagdollState`), driven game-side by `RagdollLaunchInitSystem`/`RagdollReviveSystem`
(`HealthSystemGroup`). The 2026-07 Ragdoll2D system (procedural pendulum flail, per-joint landing
zones, a scripted Z-tilt) is deleted — its `ApplyPoseJob`-stomp ordering gotcha and the visual-child-Y
open bug went with it; the toolkit is real Unity Physics box colliders with authored hinge limits, a
different mechanism entirely. See `Assets/_Vault/Tasks/NewPlans/AnimationToolkitMigration_System.md`
§5 and `Packages/com.dotsanimationtoolkit/Documentation~/ragdoll.md` for how it actually works now.

### The CorpseCells map bypasses ECS dependency tracking
`CorpseCells` hands a `NativeParallelMultiHashMap` through a singleton. `CorpseCellSystem` clears
it each frame on the main thread — any job reading it MUST register via
`CorpseCellSystem.AddJobHandleForReader`, or the clear races the read. Same pattern and reason as
`DamageBusSystem.AddJobHandleForProducer`. (No current reader registers — the artificial corpse-stack
landing-height hack that used to read it was dropped with Ragdoll2D; this stays live for whatever
reads `CorpseCells` next.)

### Corpse-stacking landing-height hack was dropped, not ported — verify stacking in play-test
The old system needed an artificial per-cell landing-height raise because it wasn't real physics. The
toolkit's ragdoll is real Unity Physics box colliders, which *should* stack corpses correctly through
actual body-vs-body collision once self-collision groups are authored on the rig — but this is
unverified (no rig exists yet). If corpses visibly clip into each other in play-test, that's the first
place to look, not a regression to chase in the command seam.

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

---

## `SystemPlacementConformanceTests`' folder↔group check is a regex on the raw attribute text

`SystemFileFolderMatchesItsUpdateGroup` matches `[UpdateInGroup\(typeof\((\w+)\)` against each
file's source text and compares the captured identifier to the parent folder name. Writing
`[UpdateInGroup(typeof(SomeNamespace.SomeGroup))]` (a fully-qualified type) doesn't match the
regex at all — `\w+` stops at the `.`, so the whole `typeof\((\w+)\)` fails and the file is
silently counted as having **zero** `[UpdateInGroup]` attributes, which trips the *other*
test (`EverySystemFileDeclaresAnUpdateGroup`) instead, with a confusing "declares 1 system but 0
attributes" message. A game file plugging into a package group (e.g.
`LocomotionStanceSystem.cs` using `DotsMovementToolkit.MovementExecutionSystemGroup`) must add
`using DotsMovementToolkit;` and write the bare `typeof(MovementExecutionSystemGroup)` — never
the qualified form — to keep both tests reading it correctly.

## `.gitignore` blanket-ignores every `/Packages/*/` — a new embedded package needs its own un-ignore line

`.gitignore` has `/[Pp]ackages/*/` followed by an explicit `!/Packages/com.dotsanimationtoolkit/`
allowlist entry. Creating a second embedded package (e.g. `com.dotsmovementtoolkit`) without
adding its own `!/Packages/<name>/` line means every file in it silently never gets staged by
`git add Packages/<name>` — no error, no warning, `git status` just shows nothing. Add the
un-ignore line in the same commit that scaffolds the package, before doing anything else.

## DOTS Animation Toolkit — ragdoll (Packages/com.dotsanimationtoolkit)

### A ragdoll must re-write its pose every frame, including while asleep
`TransformApplySystem` stomps **every** visible part's `LocalTransform` unconditionally, every
frame. Anything that poses a part outside the sampler has to run *after* it and win — a settled,
sleeping ragdoll that stops writing gets overwritten by the animation and snaps back. Symptom:
"the ragdoll works for one frame and then reverts."

**The obvious fix is wrong.** Adding `[WithDisabled(typeof(RagdollActor))]` to `TransformSampleSystem`
excludes every actor that *never asked for a ragdoll*, because `RagdollActor` is opt-in and a
`WithDisabled` on an **absent** component matches nothing. Same shape as the `PartFacing` trap.
Keep the stomp-and-rewrite; the ragdoll group is ordered after apply for this reason.

### An `EnabledRefRW/RO<T>` job parameter silently filters the query to *enabled* only
It enrols `T` as an `All` match. A job that wants **disabled** entities — a release/restore system,
anything reacting to a switch-off — matches nothing at all, every frame, with no error. Fix with
`[WithPresent(typeof(T))]` and an explicit `ValueRO` check in the body. Pre-existing precedent:
`CommandApplySystem` does this for `BoundsDirty`.

### A solver that re-applies a pre-measured penetration injects energy
Contact `distance` measured once by a probe, then re-applied on each of N position-solve
iterations, pushes out N× per substep. The body accelerates instead of settling (observed: −0.16 →
+89 units/s over ~200 frames, bouncing higher each time). Re-derive penetration against *current*
positions every iteration.

**Why this survives a green test suite:** determinism, joint limits and the planar invariant all
hold perfectly while energy is being pumped in. Only a runtime *settle* test catches it. When
adding a constraint solver, write the settle test.

### `[ReadOnly]` and `[NativeDisableParallelForRestriction]` live in `Unity.Collections`
A new job file without `using Unity.Collections;` fails with `CS0246: ReadOnlyAttribute could not be
found`, which reads like a missing assembly reference rather than a missing using.

### A string literal becomes a `FixedString` only OUTSIDE Burst
`new FixedString32Bytes("Skin")` — and the implicit `string` → `FixedString32Bytes` conversion that
hides the same constructor — compiles fine in C# and then fails at Burst compile time with
`BC1016: The managed function System.String.get_Length is not supported` (via
`FixedStringMethods.CopyFromTruncated`). The failure names the *Collections* file, not yours; the
call site is further down the stack trace. `EnumLogNames`'s comment claims the conversion is legal
inside Burst — treat that claim as unverified, it is the same constructor.

**Fix:** materialise the value where managed code is allowed and hand it to the job as data —
`ZombifySystem` builds its two palette names in a deliberately non-`[BurstCompile]` `OnCreate` and
passes them as `FixedString32Bytes` job fields. Per-method `[BurstCompile]` is what makes this
possible: dropping it on `OnCreate` leaves `OnUpdate` and the job fully Bursted.

### A mutable `NativeArray<T>` crossing a `[BurstCompile]` static entry point needs `ref`
`in` and a bare value parameter both fail (BC1063/BC1067) even though `NativeArray` is only a
pointer handle. `ClipSampler`'s `in NativeArray<PlaybackLayer>` works *only* because it is read-only
— copying that shape for a write target breaks the whole assembly.

### Unity's UI Builder deletes XML comments from `.uxml` on save
Opening `ClipEditorWindow.uxml` in the visual editor and saving strips every comment in the file.
If an explanatory comment has vanished from a `.uxml`, this is why.

### Check the amendment number before claiming one
`Phase_B_Architecture.md`'s amendments run past A49. The ragdoll spec was drafted as "A45", which was
already taken (reflection nodes), and the wrong number reached ~55 places across shipped source
comments before it was caught. `grep -nE "^## Amendment A[0-9]+"` the architecture doc first — and
note that some **event-window** source comments cite "amendment A45" for something else again, a
pre-existing inconsistency that predates this.

### A cutscene needs a `NarrativeEventAuthoring` in the scene or every request is silently dropped
`CutsceneStartSystem` tracks the running cutscene on the `NarrativeEventTag` singleton (that entity
carries `ActiveCutscene`). With no such entity it consumes the `CutsceneRequest` signal, logs one
warning, and drops it — playback never starts, nothing else complains, and the debug trigger looks
broken. Found 2026-09-05 building the G1 checkpoint: **no scene in the project had a
`NarrativeEventAuthoring` at all**, so cutscenes could never have played anywhere. Any scene that
plays a cutscene (or runs narrative events, dialogue, or the pickup/attack cutscene gates) needs one
`NarrativeEventAuthoring` GameObject in its **subscene**, not the host scene — it bakes.

### `CutsceneStage` root transform keys are ABSOLUTE world positions
`CutsceneTimelineSystem.ApplyPose` assigns `LocalTransform.Position = position` outright, so a slot's
keys stage the actor wherever they say. Two slots sharing one key track walk the actors through each
other, and a wandering unit *teleports* to the first key when the cutscene starts. That snap is the
toolkit working as designed (a cutscene stages its cast); author each slot its own lane.

### An enableable component needs `EntityQueryOptions.IgnoreComponentEnabledState` to be counted
`CreateEntityQuery(typeof(CutsceneActor)).CalculateEntityCount()` returns only entities whose
`CutsceneActor` is *enabled*. Debugging "the component vanished" when a cutscene ends is this: the
entity still has it, the bit is off.

## A dead script GUID that "greps clean" may be a live package script

`grep -rl "guid: <g>" Assets Packages --include=*.meta` misses every script inside a **resolved
package** (`com.unity.entities`, Cinemachine, uGUI, Input System — they live in `Library/PackageCache`
or are resolved by the package manager, not by a `.meta` under `Assets/`). A guid that greps clean is
therefore not proof the script is gone. `AssetDatabase.GUIDToAssetPath(guid)` is the check that does
not lie. Found 2026-09-05 doing G0-T2: `c16549610bfe4458aa9389201d072bb6`, listed in a spec as one of
seven "unrecoverable dead GUIDs" to strip from every unit prefab, is
`Unity.Entities.Hybrid`'s `LinkedEntityGroupAuthoring` — hand YAML surgery would have deleted a
working baker component from `BaseUnit` and `MaleCitizen`. Prefer
`GameObjectUtility.RemoveMonoBehavioursWithMissingScript`, which only ever removes what Unity itself
cannot resolve, and **strip base prefabs before variants** or each removal lands as a variant override
instead of clearing.

## DOTS Animation Toolkit — authoring a clip from code

### Clip keys are OFFSETS from the part's rest pose, not absolute local transforms
`ClipSampler` anchors an `Override` track on `TargetRestPose` (`restPose.localPosition.xy`,
`restPose.rotation`), so an all-zero key means "leave this part alone", and a channel left out of the
track's `channels` mask stays at rest entirely. Authoring absolute local values instead collapses
every part onto the actor origin.

### `TransformKey.rotationZ` is legacy; author `float3 rotation` in degrees
`ClipAsset.OnAfterDeserialize` migrates `rotationZ` into `rotation` **only when `rotation` is
all-zero**, so writing both is safe but writing `rotationZ` alone is the pre-3D path.
`QuickStartActorBuilder.MakeSwingTrack` in `Samples~` still writes `rotationZ` — one more instance of
the "`Samples~` is excluded from compilation and rots silently" trap above.

### On a cutout rig, Z is the only axis that can swing a part
Every `MaleCitizen` body part is a flat quad — measured mesh bounds have `size.z == 0.000` — pivoted
at its joint with the art hanging below it (`center.y ≈ −0.19` on a limb). Rotating about X or Y
foreshortens a cutout to a line. That pivot offset is also why `RigTargetDefinition.boundsExtents`
must be measured as `|mesh.center| + mesh.extents` **about the pivot**: measuring about the mesh
centre under-covers every limb by roughly half its length, and the error is invisible until something
culls wrong.

### `ActorBakeFailed` cannot be asserted from a Play-mode world
It is `internal` *and* `[BakingType]`, so it never reaches the built entity scene. "Assert no
`ActorBakeFailed` entity exists" is unassertable from the game world. What actually proves an actor
baked is a created `ClipRegistry` blob on the root plus a populated `RigPartRef` buffer — only
`RigBindingBakingSystem` writes that buffer, and only for an actor that did not bail.

### Sampling a slowed cutscene over MCP: do not let the clip period match the round trip
`CutsceneControl.speed` reaches the actors' clip layers (`PlaybackLayer.speed` reads back as the
requested value), so slowing a cutscene slows the walk cycle with it. At `speed = 0.1` a 1 s clip's
period becomes 10 s — almost exactly one `execute_code` round trip — and three consecutive samples
land within 0.07 of the same phase, making a working clip look frozen. Pick a speed whose stretched
period is a non-multiple of the round trip (`0.05` gives 20 s) before concluding anything is broken.

### A detached cutscene slot only "stays where it was let go" if its root lane is empty
Amendment A63 §3.1 promises both "Detach leaves the slot at its world pose" and "the root lane
resumes from the next key". Those are the same sentence only for a slot with no key after the
detach — and `CutsceneKeySampler`/`CutsceneBlobSampler` **clamp to the last key**, so *any* authored
root key wins the transform back on the very first frame after the attachment ends and snaps the
slot to its lane. A prop that should stay where it was thrown gets an entirely empty root lane; its
scene transform (or its `LocalTransform` at the moment of detach) is then its home, because A62
defect 2's rule leaves a keyless slot alone. Found 2026-09-05 authoring A63's checkpoint: the crate
kept teleporting back to its starting spot the instant the hand released it, which reads as a broken
detach and is in fact a working root lane.

### `RigAsset.sockets` was empty project-wide until 2026-09-05
Nothing could ride an actor's hand, and `PlayerUnit.prefab`'s bare `HandSocket` GameObject is inert
— `PlayerUnit` is not a toolkit actor, so no `SocketRegistry` is ever baked from it. `NewRig.asset`
now declares one `RigTarget` socket, "RightHand" (id `1287933773`, target `3934483903`, dense target
index 15), and both `CutsceneG1Checkpoint` minions bake it. **`ActorBaker` only adds `SocketRegistry`
when `SocketRegistryBuilder.HasSockets(rig)`**, so a rig with no socket rows produces an actor that
silently ignores every `SocketAttachment` pointed at it — there is no warning anywhere.

### Rebuilding a UI Toolkit inspector from a bound field's change event flickers it at frame rate
Unity's `SerializedDefaultEnumBinding` sends a `ChangeEvent<string>` from `OnFieldAttached`, carrying
the value it has just bound. A callback that rebuilds the inspector from that event rebuilds,
re-binds, and is called again — forever. Symptom: fields visibly flashing in and out with no input,
and every element a fresh instance every frame. Measured 2026-09-05 in the Cutscene Editor's attach
inspector at **600 `RebuildInspector` calls in a few idle seconds**; the clip-block and
transform-key inspectors beside it were byte-stable, which is what localised it.

`SerializedPropertyChangeEvent` has the same problem, so swapping between the two events fixes
nothing — both fire on bind. **A re-entrancy flag around the rebuild is also not enough**: the echo
arrives *after* `RebuildInspector` returns (measured `rebuilding=False`), because binding is
deferred to a later panel update. What actually works is comparing the event's `previousValue`
against its `newValue` and ignoring the ones where nothing changed. `CutsceneEditorPanel.
ShouldIgnoreBindingEcho` does both, and the equality half is the half that fires.

To tell a flicker like this from a stable panel without watching it: sample the inspector's child
`GetHashCode()`s from two separate `execute_code` calls seconds apart. Same instances = stable; all
different = rebuilding every frame.

### The toolkit package's Editor sources may not use `Handles` at all
`PackagingConformanceTests.Conformance_E_NoImguiApis_InEditorSources` greps every
`Packages/com.dotsanimationtoolkit/Editor/**/*.cs` (comments stripped) for
`\bOnGUI\b|\bGUILayout\b|\bHandles\.` and fails on any hit. There is no exemption list. So
`Handles.DrawWireDisc`, `Handles.PositionHandle` and `Handles.Label` are all off the table for
Scene-view work in that package, and a spec that prescribes them (A64 §3.4 did) is prescribing a
build break. What works instead, and is already the package's own idiom: a line-topology `Mesh`
drawn with `Graphics.DrawMeshNow` after `material.SetPass(0)` inside a `SceneView.duringSceneGui`
handler on `EventType.Repaint`, picked with `HandleUtility.GUIPointToWorldRay` against a `Plane`.
`HandleUtility` does **not** match the regex — only `Handles.` does. See
`CutsceneMarkSceneOverlay.cs` for a worked example, and `PreviewSceneGizmos.cs`, which explains the
same constraint from the other side.

### `Authoring/` may not even *mention* the editor assembly, comments included
`PackagingConformanceTests.Conformance_C_NoUnityEditorReference_OutsideEditorOrTests` greps the raw
text of every file outside `Editor/` and `Tests/` for `UnityEditor` — a doc comment explaining *why*
a seam exists ("`Authoring/` may not name `UnityEditor`") fails the same test it is describing.
Found 2026-09-06 building A65's hold-name seam. Say "the editor assembly" in prose there.

### A mirrored part inside a mirrored parent cancels — facing only works on a FLAT rig
`TransformSampleSystem` (and the cutscene preview's twin) mirrors a part by negating its local
`scale.x`, `localPosition.x` and its y/z rotations. On a **nested** rig that multiplies down the
chain: with `facesDirection` ticked on every target, `MaleCitizen` measured Pelvis −1, Torso +1,
Neck −1, **BaseHead +1** — the head and its whole face never flipped and the pose mirrored on every
second level. It reads as "the images flipped but the animation didn't". `MaleCitizen` and the
`MaleUnitVisual`-based units have 13 of 16 parts nested under another part.

**Tick `facesDirection` on the top-most part of each chain only** — for `NewRig` that is `Pelvis`,
`UpperLeftLeg`, `UpperRightLeg` — and the subtree inherits the reflection exactly once (a part's
non-uniform scale rides `PostTransformMatrix`, which composes into children's `LocalToWorld`).
And note the flag is per *rig* while correctness depends on the *prefab* hierarchy, so a flat actor
and a nested one sharing a rig cannot both be right. `CutsceneDirectionVariants.DescribeFacingRigProblem`
now names both this and the "nothing opted in" case, at bake time and in the slot inspector.

### `facesDirection` is the opt-in that makes facing do anything at all, and it defaults to false
A rig whose targets all leave it false resolves facings, picks direction-set variants and turns
nothing, with no error anywhere — `PartFacing` is never even baked. `NewRig.asset` was in that state
project-wide until 2026-09-06, which is why A65's checkpoint appeared not to turn. Same family as
the empty `RigAsset.sockets` above: check the content declares the thing before debugging the code.

### Two facing-angle conventions live in the cutscene code, and they were silently mixed
A facing angle in this toolkit is measured **from +X toward +Z** — 0 east, 90 north — because
`FacingResolver.FromMovement` reads a vector whose x is east and y is north, and every consumer
builds that vector with `(cos, sin)`. A `LocalTransform` Y euler measures from +Z instead, so the two
are a reflection about 45°. `CutsceneKeySampler.TryResolveFacingAngle` derived travel facing with
`atan2(delta.x, delta.z)` — the euler convention — so an actor walking **east** resolved as facing
**north**, and with a Two-coverage direction set an east→west turn produced no mirror at all. Fixed
in A65-T3 (both twins now use `CutsceneFacingVariants.AngleDegreesFromTravel`). A mark's
`facingDegrees` really *is* a world Y rotation (`PlaceAtMark` feeds it to `quaternion.RotateY`) and
is deliberately not the same model — check which of the two an angle is before comparing them.

### A `[BurstCompile]` static cannot take a blob struct that contains a `bool`
BC1063: a struct crossing a Burst entry point must be blittable, and `bool` is not. A blob struct is
the usual way to hit this — `CutsceneDirectionVariantsBlob.hasVariants` broke the whole Runtime
assembly's Burst compile the moment a `[BurstCompile]` static took it by `in`. Either
`[MarshalAs(UnmanagedType.U1)]` the field or, when every caller is managed anyway, drop the attribute
from that one method (the class attribute is harmless).

### An unfocused Editor never repaints its Scene view, so `duringSceneGui` probes never fire
Driving the Editor over MCP with the window in the background, `SceneView.RepaintAll()`,
`sceneView.Repaint()` and even the internal `EditorWindow.RepaintImmediately()` all leave a
`SceneView.duringSceneGui` handler uncalled — the repaint is queued and never serviced. Anything
that only runs inside scene GUI (`HandleUtility.GUIPointToWorldRay`, `HandleUtility.WorldToGUIPoint`,
click picking, gizmo drags) therefore **cannot be machine-verified from a background Editor**; say
so rather than reporting it as proven. And a probe delegate subscribed to `duringSceneGui` from
`execute_code` outlives the call that added it, so a self-unsubscribing handler that never runs
stays attached and fires later, mutating assets long after the session thought it was done. Remove
it explicitly: reflect the static `duringSceneGui` field, walk `GetInvocationList()`, and drop the
entries whose `Method.DeclaringType.Assembly` is the dynamic compile assembly.
