---
tags: [memory, code, systems, animation]
related: "[[Systems]], [[Components]], [[Data]]"
---

# AnimationSystemGroup — Context

Animation is driven by the `com.dotsanimationtoolkit` package now — there is no game-owned keyframe
pipeline any more. This note covers the **game↔toolkit seam**: what the game still owns, what it
hands to the package, and where the two meet in the frame. Full toolkit behavior lives in the
package's own `Documentation~/` (start at `Packages/com.dotsanimationtoolkit/Documentation~/index.md`,
and `Documentation~/actor-profiles.md` for the profile model this note builds on).

See `Assets/_Vault/Tasks/NewPlans/AnimationToolkitMigration_System.md` for the migration history and
`Assets/_Vault/Tasks/NewPlans/ActorProfileCutover_System.md` (G5) for the cutover onto
`ActorProfileAsset` (locked 2026-09-07) — everything below is G5's outcome. Content lives in
`MaleCitizen.profile.asset`, authored by
`Assets/_Scripts/Editor/ContentAuthoring/MaleCitizenContentAuthoring.cs`.

---

## What the game still owns

- **`AnimationSystemGroup`** (`SystemGroups.cs`): two systems in `AnimationAssignmentSystemGroup`,
  `UnitFacingSystem` then `UnitAnimationAssignmentSystem` (`[UpdateBefore]` edge, in that order —
  facing must resolve before clip selection reads it). `UnitAnimationAssignmentSystem` is thin now:
  it resolves `idleKey`/`walkKey` from `UnitLibraryBlob` via `AIUtils.GetLocomotionKeys` and issues
  `PlaybackApi.PlayAnimation(idle|walk key)` only when `!PlaybackApi.IsAnimationPlaying(playbackLayers,
  key)` — commands are requests, not state, so re-issuing every frame would restart the clip's
  crossfade for no reason. Ordered `[UpdateBefore(typeof(AnimationToolkitSystemGroup))]` so commands
  issued this frame apply this frame.
- **Name-convention binding at bake** (`AnimationNameConvention`, G5 D1) — the animation name equals
  the enum name: `Idle`/`Walk` (bare consts), `ForStanceIdle`/`ForStanceWalk` produce
  `<Stance>Idle`/`<Stance>Walk`, `ForAction` returns the `ActionType`'s own name. `UnitLibraryBakingSystem`
  resolves every one of these strings through the project's `AnimationNameRegistry` into a `uint` key;
  anything that doesn't resolve bakes key `0` and is collected into **one consolidated warning per
  unit** (`unresolvedAnimationNames`), not one warning per missing name.
- **Behaviour commands and the player swing play by action key** — `AnimationCommands.cs`'s
  `PlayActionAnimation` resolves `AIUtils.GetAnimationKeyByAction(ref unitBlob, stateMachine.action)`
  and calls `PlaybackApi.PlayAnimation` with that key; key `0` is a silent no-op by design (nothing to
  play). `PlayerAttackSystem`'s swing and `BehaviorExecutionSystem`/`BehaviorInterruptSystem`'s
  `PlayAnimation`/`StopAnimation` behavior commands go through the same `PlaybackApi` wrappers.
- **`AttackRequestSystem`** (`CombatExecutionSystemGroup`) matches `AnimEventOutput.animationKey`
  against this attack's own resolved key, not just the `AnimEvents.Attack` event id, before treating
  a hit-confirm event as this attack's; `attackBlob.hitTime` is still the fallback/timeout when the
  event never arrives.
- **`UnitFacingSystem`** writes `ActorFacing.facing` (in addition to the game's own `UnitFacing`)
  whenever the resolved direction changes, and reads turn granularity from the actor's baked
  `ActorProfile`/`ActorProfileBlob.turnDirections` instead of `UnitDataBlob` — a unit with no baked
  profile yet falls back to the finest (`Six`) granularity. `PartFacing { viewOffset, mirrorX }` is
  still pushed game-side onto every `BodyPart` that carries one, view offset read from
  `PartLibraryBlob.PartDef.GetViewOffset` — this stays host-owned because the toolkit has no notion of
  the game's sprite-part rig.
- **Cutscene facing (G2)**, unchanged by G5 — `UnitFacingJob` includes cutscene actors rather than
  excluding them: an enabled `CutsceneFacing` supplies the facing vector, and an actor the cutscene
  has no answer for keeps the facing it had. The angle is measured **from +X toward +Z** (0 east, 90
  north), so `(cos, sin)` lands in facing space directly — it is *not* a `LocalTransform` Y euler, and
  the two are a reflection about 45° (`UnitFacingJob.CutsceneAngleToFacingSpace`, pinned by
  `FacingSpaceTests`).
- **Cutscenes request `CutsceneApi.TopLayer`** — `NarrativeEventManager`'s `PlayCutsceneAction` writes
  `CutsceneRequest.layerIndex = CutsceneApi.TopLayer` instead of a hardcoded layer index, so a
  cutscene always lands on whatever layer a profile's bookend `Override` actually is.
- **The command seam** — every write site issues `PlaybackApi.PlayAnimation`/`StopAnimation` against
  `DynamicBuffer<AnimationCommand>` + `EnabledRefRW<AnimationCommandPending>`, never touches
  `PlaybackLayer` directly: `BehaviorExecutionSystem`/`BehaviorInterruptSystem`, `PlayerAttackSystem`
  (swing clip), `NarrativeEventManager` (managed, via `EntityManager.GetBuffer<AnimationCommand>` +
  `SetComponentEnabled<AnimationCommandPending>` directly — no lookup available outside a system).
- **The read seam** — `PlaybackApi.IsAnimationPlaying` against the toolkit's own `PlaybackLayer`
  buffer answers "what's actually playing". Never track a shadow copy of playback state game-side.
- **Design → `TargetRestPose.restSliceIndex`** — `DesignApplyUtil.ApplyDesign` writes the toolkit's
  per-part rest slice instead of a legacy pose/image-index pair; sprite tracks authored in
  `RelativeToRest` slice space retarget to whatever variant a character rolled automatically.
- **`AnimEventSoundSystem`** (`SoundSystemGroup`) — the first real `AnimEventOutput` consumer: maps
  event keys to `SoundType` via `AnimSoundEventMappingSO` → `AnimSoundEventMappingBlob` and fires
  `SoundUtil.PlayOn`. Empty table until real clips author event markers — this is the template the
  animation-event-timing plan's consumers will follow.
- **Visibility** — `CameraVisibilitySystem` (`GameManagerSystemGroup`) is still the one visibility
  authority: it drives its own `CameraVisible` as before, and additionally mirrors that decision onto
  the toolkit's `AnimVisible` for actors that carry it (`AnimVisibleMirrorJob`). The toolkit's own
  `AnimLodDistanceSystem` is not used — two visibility authorities would just risk disagreeing.
- **Billboard** — the toolkit's `BillboardResolveSystem`, Y-axis upright mode, authored per-actor on
  `ActorAuthoring.billboardMode`. No game code.
- **Ragdoll** — every unit bakes a (disabled) `RagdollLaunch` via `UnitBakingUtil` regardless of
  whether its profile uses one, so pooled units don't need it added on death. `RagdollLaunchInitSystem`
  (`HealthSystemGroup`, after `DeathSystem`) reads `Health.kill*` (captured by `DamageEventSystem` on
  the lethal `DamageEvent`) to build the `worldImpulse`/`worldTorque`, writes it into `RagdollLaunch`,
  enables it, and enables `RagdollActor` to start the drop — the toolkit's own solver takes it from
  there. This sits alongside, not instead of, the profile's own per-entry `ragdollTrigger`
  (`None`/`Start`/`Stop`, `Documentation~/actor-profiles.md` §"Ragdoll triggers"): a death animation
  entry can also fire `Start` on play or on a named event, honoured only where the rig has ragdoll
  bodies (`RagdollActor` present) — `RagdollReviveSystem` disabling `RagdollActor` on revive is the
  toolkit's own job (it restores the pose captured on enable exactly).
  `CorpseCellSystem` (`GameManagerSystemGroup`) rebuilds its spatial hash from `RagdollActor` +
  `RagdollState.flags & RagdollStateFlags.Sleeping` — position registry only; verify actual
  body-vs-body stacking in play-test before reintroducing any artificial landing-height hack.

## What left

`AnimationToolkitLayer`, `DirectionSetBlob`, `DirectionSetBakeUtil`, the unit SO's direct clip fields
(`idleAnimation`/`movingAnimation`/`actionAnimations`/`stanceAnimations`), and
`UnitAnimationAssignmentJob`'s old per-`AnimationToolkitLayer` Action branch are all gone
(`ActorProfileCutover` P1–P4) — a profile's `layers`/entries replace all of it. Comments referencing
their removal remain in `BehaviorBlobs.cs` and `SpawnStateInitSystem.cs` for anyone tracing history;
there is nothing left to call.

## Where the toolkit's own pipeline lives

`AnimationToolkitSystemGroup` runs **inside `SimulationSystemGroup`** (not `LateSimulationSystemGroup`
— verified against the package source; an earlier draft of the migration spec assumed otherwise).
It declares no ordering edges of its own; the game orders against it. Internally: binding → logic/events
→ presentation (sampling, transform/sprite apply, billboard, then the ragdoll sub-group nested inside
presentation, then sockets). See the package's `Runtime/Systems/AnimationToolkitSystemGroups.cs` and
`Documentation~/` for the full internal pipeline — this note does not duplicate it.

## Spawn-frame gotcha (unchanged from the legacy system)

`AnimationSystemGroup` runs before `SpawnSystemGroup`, so a spawned entity's toolkit part bindings
(`RigPartRef`) are only reliable from frame 2 onward — the toolkit's own `RigBindingSystem` handles
this the same way `BodyPartInitSystem` handles `BodyPart` (see [[Gotchas]]).
