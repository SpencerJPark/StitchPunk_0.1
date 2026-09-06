---
tags: [memory, code, systems, animation]
related: "[[Systems]], [[Components]], [[Data]]"
---

# AnimationSystemGroup — Context

Animation is driven by the `com.dotsanimationtoolkit` package now — there is no game-owned keyframe
pipeline any more. This note covers the **game↔toolkit seam**: what the game still owns, what it
hands to the package, and where the two meet in the frame. Full toolkit behavior lives in the
package's own `Documentation~/` (start at `Packages/com.dotsanimationtoolkit/Documentation~/index.md`);
this note only covers the game-side call sites and conventions.

See `Assets/_Vault/Tasks/NewPlans/AnimationToolkitMigration_System.md` for the migration history and
the decisions this seam is built on (locked 2026-08-29). Rig/clip/ragdoll-body **authoring** is a
separate, ongoing task — nothing here assumes real assets exist yet.

---

## What the game still owns

- **`AnimationSystemGroup`** (`SystemGroups.cs`): two systems in `AnimationAssignmentSystemGroup`,
  `UnitFacingSystem` then `UnitAnimationAssignmentSystem` (`[UpdateBefore]` edge, in that order —
  facing must resolve before clip selection reads it). Assignment decides which `ClipId` each layer
  should play from the `UnitLibraryBlob` and issues `AnimationCommandUtil.Play` only on change —
  never every frame, since commands are requests, not state. Ordered
  `[UpdateBefore(typeof(AnimationToolkitSystemGroup))]` so commands issued this frame apply this frame.
- **Facing** (`DirectionFacing_System.md`, built 2026-08-29): `UnitFacing : IComponentData { Direction
  current; }` on unit roots, written only by `UnitFacingSystem` — world-fixed `velocity.xz` (via
  `Movement.targetPosition - LocalTransform.Position`) quantized through the toolkit's
  `FacingResolver.FromMovement`, with an aim override (to-target direction) while `unitAction.current`
  is an attack and `CombatTarget` is enabled. On change it pushes `PartFacing { viewOffset, mirrorX }`
  onto every `BodyPart` that carries one, view offset read from `PartLibraryBlob.PartDef.GetViewOffset`.
  `DirectionSetAsset` (**toolkit-side** since 2026-08-29, `DotsAnimationToolkit.Authoring`) replaces
  bare `ClipAsset` on every clip-mapping field that should turn (`UnitSO.idleAnimation`/`movingAnimation`,
  `StanceAnimationMapping`, `ActionAnimationMapping`) — five east-side slots, effective
  `AnimationDirections` **derived** from which are filled (`TryGetEffectiveDirections`, shared by the
  bake-time warning and the panel's live readout — never re-derive this elsewhere).
  **The clip pick folds twice, and both folds matter.** `FacingResolver.ResolveClipFacing(unitFacing.current,
  blob.animationDirections, ...)` quantizes at the ACTOR's turn granularity, then
  `DirectionSetBlob.ResolveSlot` folds that again into what THIS set actually authored. Calling the
  raw `GetSlot` instead returns an empty `ClipId` for any facing the set never drew — which reads on
  screen as the unit freezing whenever it faces that way, not as a missing clip. (That second fold was
  missing until 2026-08-29 even though `effectiveDirections` was already being baked for it; pinned by
  `DirectionSetBlobFoldTests`.) `AIUtils.GetAnimationByAction` and `UnitAnimationAssignmentJob`'s two
  resolvers all go through it — `PlayerAttackSystem` and the `PlayActionAnimation` behavior command get
  directionality "for free" this way, no extra decision logic. The game's own `Direction`/
  `AnimationDirections` enums and `DirectionUtils` are **deleted** — everything uses the toolkit's
  `DotsAnimationToolkit.Direction`/`AnimationDirections` now. Authoring tool: the Clip Editor's
  **2D Direction Sets** toggle pane (`Packages/com.dotsanimationtoolkit/Editor/ClipEditor/DirectionSets/`),
  fed the game's units through `UnitDirectionSetContextProvider` (see [[Editor]]). Phase 5 (real
  Six-direction art) is still owner-pending — see the spec's status header.
- **Cutscene facing (G2)** — `UnitFacingJob` includes cutscene actors rather than excluding them:
  an enabled `CutsceneFacing` supplies the facing vector, and an actor the cutscene has no answer for
  keeps the facing it had. The angle is measured **from +X toward +Z** (0 east, 90 north), so
  `(cos, sin)` lands in facing space directly — it is *not* a `LocalTransform` Y euler, and the two
  are a reflection about 45° (`UnitFacingJob.CutsceneAngleToFacingSpace`, pinned by `FacingSpaceTests`).
- **The command seam** — every write site issues `AnimationCommandUtil.Play`/`Stop` against
  `DynamicBuffer<AnimationCommand>` + `EnabledRefRW<AnimationCommandPending>`, never touches
  `PlaybackLayer` directly: `BehaviorExecutionSystem`/`BehaviorInterruptSystem` (`PlayAnimation`/
  `PlayActionAnimation`/`StopAnimation` behavior commands), `PlayerAttackSystem` (swing clip),
  `NarrativeEventManager` (managed, via `EntityManager.GetBuffer<AnimationCommand>` +
  `SetComponentEnabled<AnimationCommandPending>` directly — no lookup available outside a system).
- **The read seam** — `PlaybackQuery.IsPlaying`/`PlaybackLayer.flags & PlaybackFlags.Active` answer
  "what's actually playing", read against the toolkit's own `PlaybackLayer` buffer. Never track a
  shadow copy of playback state game-side.
- **`AnimationToolkitLayer`** (`Data/Enums/AnimationToolkitLayer.cs`): the six-layer convention every
  rig in this game declares, in this order — `Base(0) / Action(1) / Override(2) / Face(3) / Eyes(4) /
  Mouth(5)`. Cast to `byte` at the `AnimationCommandUtil`/`PlaybackQuery` call site. The toolkit does
  **not** enforce that layer 3 means "Face" on every rig — it's a project convention every rig must
  follow by hand so a tag-bound `FaceExpressions` clip set's starting-layer references mean the same
  thing across rigs (see the migration spec §4).
- **Clip vocabulary** — `UnitSO.idleAnimation`/`movingAnimation`/`actionAnimations`/
  `stanceAnimations`, `BehaviorCommandAuthoring.AnimationClip`, `NarrativeEventSO`'s
  `PlayAnimationAction.animationClip` are all direct `ClipAsset` object references (toolkit
  `Authoring` assembly). Bakers (`UnitLibraryBakingSystem`, `BehaviorLibraryBakingSystem`) write
  `clipAsset.Id` (a `ClipId`) into the blob; a null `ClipAsset` bakes to `default` (`ClipId.IsValid ==
  false`), which every call site checks before issuing a command.
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
- **Ragdoll** — `RagdollLaunchInitSystem`/`RagdollReviveSystem` (`HealthSystemGroup`) build a toolkit
  `RagdollLaunch` impulse from `Health.kill*` and enable `RagdollActor` on death; disabling
  `RagdollActor` on revive is the toolkit's own job (it restores the pose captured on enable exactly).
  `CorpseCellSystem` (`GameManagerSystemGroup`) rebuilds its spatial hash from `RagdollActor` +
  `RagdollState.flags & RagdollStateFlags.Sleeping` — position registry only; the legacy artificial
  corpse-stacking landing-height hack was dropped (the toolkit's ragdoll is real Unity Physics box
  colliders — verify actual body-vs-body stacking in play-test before reintroducing anything like it).

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

## What's still pending

Rig targets, layers, and ragdoll bodies are not yet authored on any real rig (owner's task, in
progress separately). Every system above compiles and is wired correctly but is currently a no-op —
nothing has the toolkit's `ActorAuthoring`/`RigTargetAuthoring` components yet. Do not treat "compiles
clean" as "verified in play" for anything in this note until a real rig exists.
