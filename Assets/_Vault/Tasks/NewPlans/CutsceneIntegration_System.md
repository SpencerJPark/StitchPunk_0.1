# Cutscene Integration — Design Spec (G1)

> **Status:** ✅ spec ready, not built. Written 2026-09-04.
> **Roadmap:** [`Cutscene_Roadmap.md`](Cutscene_Roadmap.md) — read its §4 protocol first. This is the spec that makes **a cutscene play in the game for the first time**.
> **Depends on:** toolkit A61 (`CutsceneStage`, `CreatePlayRequestFromStage`, `TryFindStage`) and A62 (`CutsceneCameraPose.isDriven`, speed propagation). **Parallel-safe with:** A63.
> **Executor:** one Sonnet session, possibly two — commit per phase.

---

**Skills Needed:**
- `dots-feature-group` — `CutsceneSystemGroup` (§3.1)
- `dots-system-scaffold` — every system in §3
- `dots-authoring-baker` — `CutsceneActor` on units via `UnitBakingUtil`
- `dots-test` — the two PlayMode fixtures in §6

---

## 1. Purpose & scope

Wire `com.dotsanimationtoolkit`'s cutscene player into Stitch Punk: a request to start one, the teardown of AI / movement / facing / player input on the actors it puppets, the camera, the sound consumer, a narrative action to trigger it, and a debug key to play one in `DOTSTestScene`. Interactions (marks, attach, dialogue, facing) are **G2**; this spec only has to play clip blocks + root keys + camera + events on baked actors.

**Verified starting facts (2026-09-04):** no file under `Assets/_Scripts/` references any cutscene runtime type. `CutsceneActiveTag` is baked on the narrative singleton and toggled by `NarrativeEventManager`, but **no input or AI system reads it**. `CameraManager.EnterCinematic(Vector3)` moves a follow target; it cannot take a pose or FOV. `AnimEventSoundSystem` (`SoundSystemGroup`) is the only game-side `AnimEventOutput` consumer and matches actors, not request entities.

## 2. Architecture

```
NarrativeEventManager (PlayCutsceneAction)   CutsceneDebugTrigger (F9)      any system
              └───────────────┬──────────────────────┘──────────────────────┘
                              ▼ spawns a one-frame signal entity
     CutsceneRequest { cutsceneKey, layerIndex, speed } + CutsceneRequestBindingOverride buffer
                              ▼
     CutsceneSystemGroup (Player → CUTSCENE → UtilityAI)
       CutsceneStartSystem   — TryFindStage → CreatePlayRequestFromStage → apply overrides;
                               on every bound unit: enable CutsceneActor + ActionInterruptRequest,
                               MovementAPI.HaltPathing; enable ActiveCutscene on the narrative
                               singleton; destroy the signal
       CutscenePlayerControlSystem — CutsceneActiveTag = ActiveCutscene enabled (G2 refines this)
       CutsceneEndSystem     — CutscenePlaybackState.isComplete → disable CutsceneActor, re-arm AI,
                               destroy the request, disable ActiveCutscene
                              ▼ per frame (package, unchanged)
     CutsceneTimelineSystem / CutscenePartOverrideSystem drive the bound entities
                              ▼
     CutsceneCameraBridge (Mono, LateUpdate) — CutsceneCameraPose.isDriven → CameraManager cutscene vcam
     AnimEventSoundSystem — second pass over request entities (non-positional)
```

Group position: **after `PlayerSystemGroup`, before `UtilityAISystemGroup`** — a cutscene that starts this frame gates AI selection this frame, and a request spawned by the narrative manager (a MonoBehaviour `Update`, i.e. before `SimulationSystemGroup`) is consumed the same frame. Clip blocks target `AnimationToolkitLayer.Override` by default: it out-prioritises whatever `UnitAnimationAssignmentSystem` writes to Base/Action, so assignment needs **no** cutscene awareness.

## 3. Systems and components

### 3.1 Group + folder

`SystemGroups.cs`: `[UpdateInGroup(typeof(SimulationSystemGroup))] [UpdateAfter(typeof(PlayerSystemGroup))] [UpdateBefore(typeof(UtilityAISystemGroup))] public partial class CutsceneSystemGroup : GameSceneSystemGroup { }` — insert at its pipeline position, update the header comment, add it to `SystemGroupOrderTests.SimulationPipeline` between `PlayerSystemGroup` and `UtilityAISystemGroup`. Folder `Assets/_Scripts/Systems/CutsceneSystemGroup/`. `Contracts.md` rows for `CutsceneRequest` and `ActiveCutscene`. Pipeline line in `Systems.md` and the folder map in `Assets/CLAUDE.md`.

### 3.2 Components — `Components/Cutscene/CutsceneComponents.cs`

```csharp
public struct CutsceneRequest : IComponentData { public ulong cutsceneKey; public byte layerIndex; public float speed; }   // one-frame signal entity
public struct CutsceneRequestBindingOverride : IBufferElementData { public uint slotId; public Entity target; }       // same entity; wins over the stage binding
public struct CutsceneActor : IComponentData, IEnableableComponent { }   // every unit, baked disabled; enabled while a cutscene binds it
public struct ActiveCutscene : IComponentData, IEnableableComponent { public Entity playRequest; }   // on the NarrativeEventTag singleton, baked disabled
```

`UnitBakingUtil.BakeRequirements` adds `CutsceneActor` disabled. `SpawnStateInitSystem` resets it off (the Gotchas rule: every enableable with a spawn default gets a line there). `NarrativeEventAuthoring.Baker` adds `ActiveCutscene` disabled beside `CutsceneActiveTag`. The player entity needs `CutsceneActor` too — find its baker (grep `new Player` under `Authoring/`) and add it.

### 3.3 `CutsceneStartSystem` (OrderFirst)

Not `[BurstCompile]`: it calls `CutscenePlaybackApi` (managed `EntityManager` API) and makes structural changes. Query `CutsceneRequest` entities; for each: `TryFindStage` (miss → `Debug.LogWarning` naming the key, destroy the signal). `CreatePlayRequestFromStage(entityManager, stage, layerIndex, speed)`; for every override, replace-or-add the `CutsceneActorBinding` entry. For every binding whose target has `CutsceneActor`: enable it; if it has `ActionInterruptRequest` enable it (the single AI teardown path, `BehaviorInterruptSystem`); if it has `PathRequest` call `MovementAPI.HaltPathing` and set `Movement.targetPosition = LocalTransform.Position`. Write `ActiveCutscene { playRequest }` + enable. A second request while one is active is dropped with a warning (Phase G §8: no concurrent cutscenes). Destroy all signal entities with one `DestroyEntity(query)` at the end (the `LoggingSystem` lifecycle).

### 3.4 `CutsceneEndSystem`

Each frame, if `ActiveCutscene` is enabled and its request's `CutscenePlaybackState.isComplete` (or the request no longer exists): for every bound `CutsceneActor`: disable it, and revive the brain the way `ReviveRequestSystem`/`SwapBrainSystem` do after teardown — read `Systems_AI.md` and copy the exact re-arm (enable `ActionRequest`, whatever "blank-slate Idle" requires). Destroy the request entity (the toolkit leaves that to the host). Disable `ActiveCutscene`.

### 3.5 Gates (one-line guards, the `IsCombatAction` style)

- `[WithDisabled(typeof(CutsceneActor))]` on the jobs in `WinnerSelectionSystem`, `MinionActionSelectionSystem`, and every awareness job in `UtilityAwarenessSystemGroup/` (`ClearOptionsSystem` included — an actor mid-cutscene keeps an empty option list). `WithDisabled` requires presence, which §3.2 guarantees for units; props never match these queries.
- `UnitFacingSystem`: skip units whose `CutsceneActor` is enabled (G2 replaces the skip with the `CutsceneFacing` bridge).
- `PlayerMoveSystem`: when `CutsceneActiveTag` is enabled, `targetPosition = current` and continue (same shape as the aim lock). `PlayerAttackSystem`, `PlayerRollInputSystem`, `PlayerEquipmentInputSystem`, `PlayerPickupSystem`, `DialogueStartSystem`: early-out on the same tag. Read the tag through `SystemAPI.TryGetSingletonEntity<NarrativeEventTag>` — a scene without the narrative singleton must behave as "no cutscene".
- `CutscenePlayerControlSystem`: `CutsceneActiveTag = ActiveCutscene enabled`. (G2 adds the rendezvous exception.) `NarrativeEventManager` keeps setting the tag for `blockPlayerInput` events; the two writers OR together — implement as "enabled if either", by having this system only *enable* while active and only *disable* when no narrative event is active either (`ActiveNarrativeEvent` disabled).
- `PersistentSaveSystem`: skip a `SaveRequest` while `ActiveCutscene` is enabled; warn once per cutscene.

### 3.6 Camera — `CutsceneCameraBridge` (Mono, `MonoBehaviours/Managers/`)

`CameraManager` gains `[SerializeField] CinemachineCamera cutsceneCam` and `CinemachineCameraType.Cutscene`, plus `EnterCutscene()` / `ExitCutscene()` (store/restore the previous type exactly like the cinematic pair). The vcam has **no Follow/LookAt** — the bridge owns its transform. Bridge `LateUpdate`: read the `CutsceneCameraPose` singleton (via `World.DefaultGameObjectInjectionWorld`, query cached); on `isDriven` rising edge `EnterCutscene()`; while driven, `cutsceneCam.transform.SetPositionAndRotation(pose.position, pose.rotation)` and `cutsceneCam.Lens.FieldOfView = pose.fieldOfView`; on the falling edge `ExitCutscene()`. Hard cuts need nothing: moving one vcam is instant; the brain only blends between vcams, so the entry and exit blends are the brain's default (owner tunes them in the Inspector). **Verify in the checkpoint** whether the gameplay cameras are orthographic — if so, the bridge must also set `Lens.OrthographicSize` from FOV, or the cutscene vcam stays perspective by design; record the answer in §7.

### 3.7 Sound

`AnimEventSoundSystem`: add a second, main-thread pass over entities with `CutscenePlay` + enabled `AnimEventsPending`, mapping keys through the same blob and playing **non-positional** (position = `ListenerPosition` singleton, `followSource = false`). Cutscene events belong to no actor; a `LocalTransform` on the request entity would be a lie.

### 3.8 Triggers

- `PlayCutsceneAction : NarrativeActionBase { CutsceneAsset cutscene; AnimationToolkitLayer layer = Override; float speed = 1f; bool waitForCompletion = true; List<CutsceneSlotEntityOverride { uint slotId; int narrativeEntityId; }> overrides; }` in `NarrativeEventSO.cs`. `NarrativeEventManager.ExecuteActionAsync` gains the case: spawn the signal entity with `cutsceneKey = cutscene.StableId`, resolve overrides through `TryGetEntity`, then `await UniTask.WaitUntil(() => !ActiveCutscene enabled)` when waiting.
- `CutsceneDebugTrigger` (Mono, `MonoBehaviours/Debug/` or beside the existing debug menus — grep `DebugSaveMenu`): `[SerializeField] CutsceneAsset cutscene; KeyCode key = F9;` spawns the same signal. Placed in `DOTSTestScene`'s main scene for the checkpoint.

## 4. Decisions

- **DECIDED — group placement:** after Player, before UtilityAI (§2). The old plan's "after StateMachine" would let selection pick an action on the start frame.
- **DECIDED — entry pattern:** one-frame signal entity (`CutsceneRequest`), the `LoggingSystem` lifecycle; playback state lives on the toolkit's request entity, referenced from `ActiveCutscene`.
- **DECIDED — teardown:** `ActionInterruptRequest` (existing single path) + `CutsceneActor` gates; no new interrupt kind.
- **DECIDED — camera:** a dedicated cutscene vcam driven by transform, not the cinematic follow target.
- **DECIDED — no concurrency:** a second request while one runs is dropped with a warning.
- **DECIDED — save lock:** requests skipped with a warning, never queued.

## 5. File manifest

**New:** `Components/Cutscene/CutsceneComponents.cs`; `Systems/CutsceneSystemGroup/CutsceneStartSystem.cs`, `CutsceneEndSystem.cs`, `CutscenePlayerControlSystem.cs`; `MonoBehaviours/Managers/CutsceneCameraBridge.cs`; `MonoBehaviours/Debug/CutsceneDebugTrigger.cs`; `Tests/PlayMode/CutsceneSystemTests.cs`.
**Edited:** `SystemGroups.cs`, `Tests/SystemGroupOrderTests.cs`, `Utils/UnitBakingUtil.cs`, the player baker, `Authoring/NarrativeEventAuthoring.cs`, `Systems/SpawnInitSystemGroup/SpawnStateInitSystem.cs`, `WinnerSelectionSystem.cs`, `MinionActionSelectionSystem.cs`, every file in `UtilityAwarenessSystemGroup/`, `UnitFacingSystem.cs`, `PlayerFollowerSystem.cs` (`PlayerMoveSystem`), `PlayerAttackSystem.cs`, `PlayerRollInputSystem.cs`, `PlayerEquipmentInputSystem.cs`, `PlayerPickupSystem.cs`, `DialogueStartSystem.cs`, `PersistentSaveSystem.cs`, `AnimEventSoundSystem.cs`, `CameraManager.cs` + `CinemachineCameraType` enum, `NarrativeEventSO.cs`, `NarrativeEventManager.cs`, `_Vault/Memories/Code/Contracts.md`, `Systems.md`, `Assets/CLAUDE.md`.
**Scene:** a `CutsceneCam` vcam under the camera rig in `Game.unity` and in `DOTSTestScene`'s main scene; `CutsceneCameraBridge` + `CutsceneDebugTrigger` objects in the test scene. (Scene edits are the owner's or done via `execute_code`; say which.)

## 6. Build phases

- [x] **Phase 1 — group, components, baking, gates (§3.1, 3.2, 3.5).** Gate: compile; `StitchPunk.Tests` `SystemPlacementConformanceTests` + `SystemGroupOrderTests`. Landed 2026-09-04: `CutsceneSystemGroup` inserted Player→Cutscene→UtilityAI; `CutsceneComponents.cs` (`CutsceneRequest`/`CutsceneRequestBindingOverride`/`CutsceneActor`/`ActiveCutscene`); baked via `UnitBakingUtil`, the player baker, `NarrativeEventAuthoring`; `SpawnStateInitSystem` resets `CutsceneActor` off; `[WithDisabled(typeof(CutsceneActor))]` gates on WinnerSelection/MinionActionSelection/every UtilityAwarenessSystemGroup job except ThreatDecaySystem (see §7) and UnitFacingSystem; `CutsceneActiveTag`/`ActiveCutscene` early-outs on PlayerMoveSystem/PlayerAttackSystem/PlayerRollInputSystem/PlayerEquipmentInputSystem/PlayerPickupSystem/DialogueStartSystem/PersistentSaveSystem. 9/9 tests green.
- [x] **Phase 2 — start/end/player-control systems (§3.3–3.5).** Tests (PlayMode, `StitchPunk.Tests.PlayMode`, manual `World` like `BehaviorExecutionSystemTests`): `CutsceneStartSystem_BindsTheStage_EnablesCutsceneActor_AndRaisesInterrupt` (hand-built `CutsceneStage` entity + one unit entity with the components §3.3 touches; assert enabled bits and that the signal is gone); `CutsceneEndSystem_ReleasesActorsAndDestroysTheRequest` (mark `isComplete`, update, assert). Landed 2026-09-04: `CutsceneStartSystem`, `CutsceneEndSystem`, `CutscenePlayerControlSystem` in `Systems/CutsceneSystemGroup/`. Both fixtures genuinely failed first (see §7) before the fix; 2/2 green after.
- [x] **Phase 3 — camera bridge + sound pass (§3.6, 3.7).** No fixture. Gate: compile. Landed 2026-09-04: `CameraManager` gained `cutsceneCam`/`CutsceneCamera`/`EnterCutscene`/`ExitCutscene` + `CinemachineCameraType.Cutscene`; `CutsceneCameraBridge` (Mono) drives its transform/lens from `CutsceneCameraPose` each LateUpdate; `AnimEventSoundSystem` split its `AnimEventsPending` pass in two (`[WithNone(typeof(CutscenePlay))]` on the existing actor job, a new single-threaded `CutsceneAnimEventSoundJob` playing non-positionally at `ListenerPosition` for request entities). Scene wiring (vcam under the camera rig, bridge + debug trigger objects) deferred to the Phase 5 checkpoint setup.
- [ ] **Phase 4 — triggers (§3.8).** Gate: compile. Then a static self-review against `RULES.md` (no `var`, explicit types, `[ReadOnly]` import).
- [ ] **Phase 5 — full suites once** (`StitchPunk.Tests`, `StitchPunk.Tests.PlayMode`; also the toolkit suites since A61/A62 landed before this).
- [ ] **⏸ Owner checkpoint (the first real cutscene).** In `DOTSTestScene`: two placed minion actors and the player, a cutscene with a walking clip block + root keys for both minions, a camera lane with one move and one cut, one event. Sync to Stage, enter Play, press F9. Expect: both minions walk on their root keys with the walk cycle looping, the camera moves then cuts, the Entities window shows empty `UtilityActions` on both while it runs, WASD does nothing, and at the end they resume wandering from where they stopped and the camera blends back. Retire this spec into `Tasks/Verification/` with `verify-cutsceneintegration.md` holding exactly that list.

## 7. Open questions / build log

- Orthographic gameplay camera? (§3.6 — answer at the checkpoint.)
- **Phase 2 gotcha (caught by the new fixture, not pre-existing):** `CutsceneStartSystem`'s first draft cached `ComponentLookup<CutsceneActor>`/`ActionInterruptRequest`/`PathRequest`/`Movement`/`LocalTransform` once at the top of `OnUpdate`, before calling `CutscenePlaybackApi.CreatePlayRequestFromStage` — which does structural changes (creates the request entity, adds buffers). Every `ComponentLookup` obtained before a structural change is invalidated; using one afterward throws `ObjectDisposedException` at runtime with no compile error, silently aborting `OnUpdate` before it reached `DestroyEntity` — the fixture caught this as "signal entity never destroyed." Fixed by making the lookups `OnCreate` fields and calling `.Update(ref state)` on each right after the structural change, before using them on bound actors. Worth a `Gotchas.md` entry if a second system trips on it.
- **Phase 1 drift:** §3.5 says the `[WithDisabled(typeof(CutsceneActor))]` gate goes on "every awareness job in `UtilityAwarenessSystemGroup/`" and §5's file manifest lists every file in that folder as edited. `ThreatDecaySystem.cs` lives in that folder but its job (`ThreatDecayJob`) has no `[WithAll(UtilityBrain)]` — it matches any entity with a `ThreatEntry` buffer, which per `FactionAuthoring.cs` doc comments may include entities not baked through `UnitBakingUtil.BakeRequirements` (unverified). Gating it on `[WithDisabled(typeof(CutsceneActor))]` risks silently freezing threat decay forever for any such entity (a `WithDisabled` filter requires presence — an entity lacking `CutsceneActor` entirely would never match). Left ungated: decay running during a cutscene is harmless since its only consumers (Flee/SelfDefence awareness) are already gated off. Flagging per protocol §8 rather than silently deviating from the file manifest.
