# Caravan — Drive, Park, Crash, Storage, Spawn Exits, Chimney Smoke

> **Status:** 🔍 Phases 0–5 built 2026-10-24/26 ("Do it" = the recommended answer for every CV-Q, §11). Compile clean, EditMode 12/12, PlayMode 30/30 on the touched fixtures. **Never played**: the play pass is [`verify-caravan.md`](verify-caravan.md). §12 records where the build departed from this spec.
> **Builds on:** [`CampLoop_System.md`](../Plans/CampLoop_System.md) Phases 0–5 (workstation, furnace, summon queue, gathering deposit, ledger). This spec turns that workstation into the caravan. It does not fork it.
> **Design source:** owner brief 2026-10-23 + `Spencer/Design_Decisions.md` → Caravan ("static rotation 3rd person"), Tournament (the hut is the stationary workstation, the caravan is the one in the wild).

## 1. Context

The owner's brief, 2026-10-23: you drive the caravan around, then park it and it becomes your mobile workstation. The wheels spin and the doors animate. You can crash into units and knock over, damage or kill them. The caravan has its own storage and its own health. Minions leave it in one of two ways: small ones walk out of the back and hop down, and for large ones the caravan shakes, the back doors burst open and a smoke cloud billows out to cover the spawn. The chimney smokes while wood is burning for electricity.

**What already exists (do not rebuild):**

- **`Assets/Prefabs/Vehicles/Caravan.prefab`** is the art rig, currently set dressing in `CampLoop.unity` (a plain GameObject, not baked). Its children are `Wheels/{FrontLeftWheel, FrontRightWheel, BackLeftWheel, BackRightWheel}`, `FrontWheelHinge`, `BackWheelHinge`, `Doors/{LeftDoor, RightDoor}` with `LeftDoorHingeControl` / `RightDoorHingeControl`, `Shakable` (the body root for shaking), `Chimey` (sic), `Horse`, `DriverAnchor`, `CarrageDriverLeft/Right`, a Rive panel, `TeslaCoil`, `WorkTable`, `Chest`, `Zones/BackZone`, a Rigidbody and 8 BoxColliders. The wheel and door pivots must be checked in the Editor at Phase 1 (CV-O1).
- **The workstation** (`Prefabs/Buildings/Workstation.prefab`, `WorkstationAuthoring`, `Components/Structures/WorkstationComponents.cs`) comes with `Health` 200, the repair recipe, the lose rule (`WorkstationDestroyedSystem`), furnace, craft, the summon queue, pack-up and mount. The player deploys it with B and a ghost (`WorkstationPlacementSystem` / `WorkstationDeploySystem`).
- **The ledger** is `ResourceStack` / `StorageCapacity` / `ResourceChangeRequest` / `ResourceShortfall` on the **Player**, with `ResourceLedger` (Utils) and `ResourceChangeSystem` (PlayerResourceSystemGroup). Every spender reaches it through `Workstation.ownerPlayer` or the Player singleton.
- **Input:** the `Vehicle` action map already exists in `Settings/Input/PlayerControls.inputactions`, with `Steer` (Vector2, WASD/stick), `Look`, `Switch to Back`, `ExitVehicle` (E / pad North) and `Speed` (C / pad East). `ActionMaps.Vehicle`, `CinemachineCameraType.Vehicle` → `CameraManager.vehicleCam`, and a `CameraTargetController` Vehicle case are already wired. Nothing in ECS drives them yet.
- **Damage:** the `DamageBus` (`DamageEvent` carries `sourcePosition`, `launchForceX/Y`, `ragdollForce`, `spin`). A lethal event launches the toolkit ragdoll through `RagdollLaunchInitSystem`. Knockback applies **on death only** (memory: combat knockback), and this spec keeps that rule: survivors get stun plus a shove, not a ragdoll.
- **Stun:** `StunnedState` (enableable, `duration`) in `Components/Units/UnitComponents.cs`.
- **Push-out precedent:** `PlayerBodyBlockSystem` (MovementExecutionSystemGroup, after `UnitMoverSystem`) does a sphere overlap on the Units layer (6) plus a capped per-frame push. Crash detection copies its query shape.
- **VFX:** `VfxUtil.Play` → `VfxRequest` → `VfxDrainSystem` → `VfxManager` (a pooled `ParticleSystem` per `VfxType`). `VfxType` is currently `{ None, RollDustStart, RollDustLand }`.

**Skills Needed:** `dots-system-scaffold` (every system), `dots-authoring-baker` (`CaravanAuthoring`, the `UnitSO` exit field), `dots-test` (PlayMode fixtures for the drive integrator, crash damage and the ledger move). No `dots-feature-group` (CV-D11: no new top-level group). No `dots-blob-library`: the exit style rides `UnitDataBlob`.

## 2. Phases

| # | Phase | Playable result |
|---|---|---|
| 0 | **Bake the caravan as the workstation**: `CaravanAuthoring` on `Caravan.prefab`, which also carries `WorkstationAuthoring`; placed in `CampLoop_Sub` instead of deployed; B-key deploy disabled when a caravan exists | Play `CampLoop`: the caravan is there, you mount its work table, summon, and the furnace runs, just like the placeholder |
| 1 | **Drive + park + wheels/doors**: board, drive, park, wheel spin, door open/close, horse follows the body, vehicle camera | Board, drive around, park, step out, mount the work table |
| 2 | **Storage**: the ledger moves from the Player to the caravan | HUD, spends, gathering deposits and save all read the caravan |
| 3 | **Crash**: speed-scaled damage, stun + shove, ragdoll on kill, caravan self-damage + slowdown | Drive through a bandit group |
| 4 | **Spawn exits**: walk-out-and-hop and smoke-burst, door/shake sequencing | Summon a zombie and a brute and watch both exits |
| 5 | **Chimney smoke** | The chimney smokes while burning and stops when out of wood |

Phase 1 is the first milestone (CV-D12). Phases 2–5 are independent of each other once 1 lands, so they can run as parallel workers.

## 3. Data model

**`Assets/_Scripts/Components/Structures/CaravanComponents.cs`** (new):

```csharp
// On the caravan entity, beside Workstation / WorkstationFurnace / Health.
public struct Caravan : IComponentData
{
    public float3 driverSeatLocalOffset;   // from DriverAnchor
    public float3 boardPointLocalOffset;   // where the player must stand to board (E)
    public float3 exitPointLocalOffset;    // where the player is put on park/exit
    public float3 backDoorInteriorLocalOffset;  // walk-out start, inside the back doors
    public float3 tailgateEdgeLocalOffset;      // walk-out hop point
    public float3 burstSpawnLocalOffset;        // smoke-burst spawn point behind the doors
}

public struct CaravanDriveTuning : IComponentData          // baked, inspector-tuned
{
    public float maxForwardSpeed;        // m/s, default 7
    public float maxReverseSpeed;        // default 2.5
    public float acceleration;           // default 3.5
    public float brakeDeceleration;      // default 9
    public float coastDeceleration;      // default 1.5
    public float turnRateAtFullSpeed;    // deg/s, default 55
    public float turnRateAtLowSpeed;     // deg/s, default 25 (a horse cart can't spin in place)
    public float parkMaxSpeed;           // must be below this to park, default 0.5
    public float collisionCastRadius;    // static-geometry cast, default 1.4
    public float wheelRadius;            // for spin, default 0.45
}

public struct CaravanDriveState : IComponentData
{
    public float  forwardSpeed;          // signed, m/s
    public float  steerInput;            // -1..1 last frame (presenter tilts the front axle)
    public float  wheelSpinRadians;      // accumulated, presenter reads it
}

// Enableable mode bits on the caravan. Exactly one of Driving / Parked is enabled.
public struct CaravanDriving : IComponentData, IEnableableComponent { public Entity driver; }
public struct CaravanParked  : IComponentData, IEnableableComponent { }

// Presentation-facing sequencing state, written by sim, read by CaravanPresenter.
public enum CaravanDoorPhase : byte { Closed, Opening, Open, Closing, BurstOpen }
public struct CaravanDoors : IComponentData
{
    public CaravanDoorPhase phase;
    public float phaseTime;              // seconds into the phase
    public float openSeconds;            // default 0.6
    public float burstSeconds;           // default 0.12 (doors slam)
}
public struct CaravanShake : IComponentData
{
    public float remainingSeconds;
    public float amplitude;              // metres, presenter applies to Shakable
}

// Crash
public struct CaravanCrashTuning : IComponentData
{
    public float minimumImpactSpeed;     // below this, push only. Default 2
    public float damagePerMetrePerSecond;// default 6 → 42 at top speed
    public float stunSecondsPerMetrePerSecond; // default 0.25
    public float shoveDistance;          // survivors pushed sideways out of the path, default 1.5
    public float selfDamagePerHit;       // default 1
    public float speedLossPerHit;        // fraction of forwardSpeed lost per unit hit, default 0.15
    public float rehitCooldownSeconds;   // same unit can't be hit again for this long, default 1
}
public struct CaravanRecentHit : IBufferElementData { public Entity unit; public double hitTime; }

// Spawn exits
public enum CaravanExitStyle : byte { WalkOutAndHop, SmokeBurst }
public struct CaravanExitInProgress : IComponentData, IEnableableComponent   // on the caravan: summon queue head waits while enabled
{
    public CaravanExitStyle style;
    public UnitType unitType;
    public float elapsed;
    public byte unitSpawned;             // 0/1
}
public struct CaravanExitWalker : IComponentData, IEnableableComponent       // on a walk-out unit until it lands
{
    public float3 interiorPoint, tailgatePoint, landingPoint;
    public float  elapsed;
}

// Chimney
public struct CaravanChimneySmoking : IComponentData, IEnableableComponent { }
```

**On the Player** (`Components/Player/PlayerWorkstationComponents.cs`):

```csharp
public struct PlayerCaravanLink : IComponentData { public Entity caravan; } // set by CaravanLinkSystem on the first frame
public struct DrivingCaravan : IComponentData, IEnableableComponent { }      // player hidden, Vehicle map
```

**On `UnitSO`:** `public CaravanExitStyle caravanExitStyle = CaravanExitStyle.WalkOutAndHop;` baked into `UnitDataBlob.caravanExitStyle` (CV-D10). `PlayerZombieBrute` → `SmokeBurst`.

**`VfxType`** appends `CaravanSmokeBurst`, `CaravanChimneyPuff` ← DECISION CV-Q4 (chimney as a looping ParticleSystem on the prefab vs a `VfxType`). `CaravanDust` (driving dust off the back wheels) is an optional extra.

## 4. Systems

| System | Group | Behaviour |
|---|---|---|
| `CaravanLinkSystem` | `WorkstationInputSystemGroup`, OrderFirst | Once the caravan exists: set `PlayerCaravanLink`, `PlayerWorkstationLink`, `Workstation.ownerPlayer`, enable `CaravanParked`. Disables B-key placement while a caravan is linked (the hut scene keeps B). |
| `CaravanBoardSystem` | `WorkstationInputSystemGroup`, after Link, **before** `WorkstationMountSystem` | **Board:** parked, the player is not mounted, within 1.5 m of the board point (or `Target` = caravan and closer to the board point than the work table ← DECISION CV-Q1), and E → enable `DrivingCaravan`, enable `CaravanDriving{driver}`, disable `CaravanParked`, close doors, consume E. **Park:** driving, `ExitVehicle` pressed, `abs(forwardSpeed) < parkMaxSpeed` → reverse that and put the player at the exit point. Refused while `CaravanExitInProgress` is enabled. Too fast → log "Slow down to park" plus the `ResourceShortfall`-style flash on the HUD. |
| `CaravanDriveInputSystem` | `PlayerInputSystemGroup` (the existing input bridge writes `Steer` / `Speed` into a `CaravanDriveInput` component on the player) | Copies input only. Ignored unless `DrivingCaravan`. |
| `CaravanDriveSystem` | `MovementExecutionSystemGroup`, after `UnitMoverSystem` | Integrates `forwardSpeed` (throttle = `Steer.y`, brake when input opposes motion, coast otherwise). Yaw rate = `lerp(lowTurn, fullTurn, speed01) * Steer.x * sign(forwardSpeed)`, with no turning at zero speed. A Unity Physics `SphereCast` against static geometry (not the Units layer) clamps the step and zeroes speed on a hard hit. Ground snap uses the same down-ray as `WorkstationPlacementSystem`. Accumulates `wheelSpinRadians += forwardSpeed * dt / wheelRadius`. Snaps the hidden driver's `LocalTransform` to the seat. |
| `CaravanCrashSystem` | `MovementExecutionSystemGroup`, after `CaravanDriveSystem` (detection must see this frame's position. It is a DamageBus **producer**, the same as `HazardZoneSystem`) | While driving and `abs(forwardSpeed) ≥ 0.5`: a capsule overlap on Units layer 6 over the caravan footprint, using the `PlayerBodyBlockSystem` query shape. For each hit unit not in `CaravanRecentHit` within cooldown: **every unit is shoved** sideways out of the path (CV-D6). If speed ≥ `minimumImpactSpeed` **and the unit is not the player's `Minion`**, enqueue a `DamageEvent` (`damageSource = Vehicle` ← new `DamageSource` value, amount = speed × `damagePerMetrePerSecond`, `sourcePosition` = caravan front, `launchForceX` ∝ speed, `launchForceY` 2–4, `spin` 360) and enable `StunnedState{duration}`. Minions get the shove and the stun but no damage (CV-D6) ← DECISION CV-Q5 (stun minions too?). Per hit: `forwardSpeed *= 1 − speedLossPerHit`, and the caravan queues `selfDamagePerHit` against its own `Health`. The player on foot counts as a unit (dodge i-frames apply through the bus for free). |
| `CaravanDoorSystem` | `BuildingsSystemGroup` | Advances `CaravanDoors.phase` by time. Opening → Open → (hold while an exit is running) → Closing → Closed. `BurstOpen` jumps straight to Open after `burstSeconds`. |
| `CaravanExitSequenceSystem` | `BuildingsSystemGroup`, **between** `WorkstationSummonSystem`'s tick and its spawn hand-off ← changes `WorkstationSummonSystem` | When the queue head reaches zero: instead of appending `PendingMinionSpawn` directly, enable `CaravanExitInProgress{style from UnitDataBlob}`. The queue does not pop until the sequence finishes, and the next build doesn't tick meanwhile ← DECISION CV-Q3. **WalkOutAndHop:** doors Opening → at Open, append `PendingMinionSpawn` at the interior point with `CaravanExitWalker` → wait for the walker to land → doors Closing → done. **SmokeBurst:** `CaravanShake{1.2 s, 0.06 m}` → at 1.2 s, doors `BurstOpen` + `VfxUtil.Play(CaravanSmokeBurst, back)` + `CaravanShake{0.25 s, 0.15 m}` → at +0.35 s (smoke at full cover) append `PendingMinionSpawn` at the burst point → after +1.5 s doors Closing → done. |
| `CaravanExitWalkSystem` | `MovementExecutionSystemGroup` | Units with `CaravanExitWalker` enabled are scripted, not AI-driven: AI is gated the same way as a `CutsceneActor` ← DECISION CV-Q2 (reuse the `CutsceneActor` gate vs a new gate bit). Interior → tailgate is a walk at `moveSpeed`, playing `Walk`. Tailgate → landing is a 0.45 s parabolic hop, playing `CaravanHopDown`. On landing, disable the walker and hand off to the rally formation (the existing `rallyLocalOffset` path). |
| `WorkstationSummonSpawnSystem` | `SpawnSystemGroup` (existing) | Unchanged, apart from copying a `CaravanExitWalker` payload onto the instance when `PendingMinionSpawn` carries one (new optional field). |
| `CaravanChimneySystem` | `BuildingsSystemGroup`, after `WorkstationFurnaceSystem` | `CaravanChimneySmoking` = `WorkstationFurnace.isBurning && Wood ≥ 1` (read from the caravan's own ledger after Phase 2). Independent of driving or parked (CV-D8). |
| `CaravanDestroyedSystem` | — | **Not new.** `WorkstationDestroyedSystem` already fires the lose state off the caravan's `Health` (CV-D2). While driving, it also force-exits the driver first ← DECISION CV-Q6 (lose immediately, or the caravan becomes a wreck and the player keeps fighting?). |

**Pack-up is retired for the caravan.** `WorkstationPackUpSystem` stays for the hut. Driving off is what "packs up": boarding is refused while the summon queue is non-empty, the same rule pack-up enforces today.

**Presentation (MonoBehaviour, CV-D7): `CaravanPresenter`** on the `Caravan.prefab` companion, under `MonoBehaviours/`. Each frame it reads the caravan entity's `LocalTransform`, `CaravanDriveState`, `CaravanDoors`, `CaravanShake` and `CaravanChimneySmoking` and:

- moves the GameObject root to the entity (the prefab is presentation only and its Rigidbody is removed, so ECS owns the transform)
- spins the four wheels about their axle by `wheelSpinRadians`, and yaws `FrontWheelHinge` by `steerInput × 25°`
- swings the `Left/RightDoorHingeControl` angles by door phase (smoothstep for Opening/Closing, overshoot-and-settle for `BurstOpen`)
- applies Perlin offset × `amplitude` to `Shakable`
- plays/stops the chimney `ParticleSystem` on `Chimey`
- drives the `Horse` walk/trot/idle by `|forwardSpeed|` (CV-D3. The horse is part of the art rig, not a unit, so it never takes crash damage)
- hides the player's renderers while `DrivingCaravan` is on (or this lives in the existing player presenter)

**Input map switching:** `PlayerInputManager.ObservedUpdate` already flips maps off `WorkstationMounted`. Add a second watch: `DrivingCaravan` enabled → `ApplyActionMapSwitch(ActionMaps.Vehicle)`, which already selects `vehicleCam`. Disabled → back to `Player`. The camera keeps the game's fixed rotation (Design_Decisions: "static rotation 3rd person").

## 5. Storage — the ledger moves to the caravan (Phase 2, CV-D5)

The four ledger buffers and `PlayerResourcesSnapshot` bake onto the **caravan** instead of the Player. Every reader resolves the ledger through one hop:

```csharp
public struct ResourceLedgerLink : IComponentData { public Entity ledgerHolder; } // on Player; = caravan, or the hut workstation, or self
```

Touch list (all found by `grep -rln "ResourceStack\|ResourceChangeRequest" Assets/_Scripts`, so re-run it at build time): `PlayerControllerAuthoring` (stops baking the buffers once the caravan bakes them ← DECISION CV-Q7: what about scenes with no caravan? Recommended: the Player keeps the buffers as a fallback holder, so `ResourceLedgerLink` points at self when there is no caravan or hut), `WorkstationFurnace/Craft/Repair/SummonSystem` (`ownerPlayer` hop → the workstation's own buffers), `MinionGatherSystem` (deposit straight into the workstation), `PlayerReviverSystem` (spends via `ResourceLedgerLink` ← DECISION CV-Q8: does a field revive far from the caravan still draw rounds from it? Recommended yes, the ledger is "back at the caravan" and stays global in practice), `ResourceChangeSystem` (query is unchanged: it drains requests wherever the buffers live), `ResourceSave/LoadSyncSystem` (snapshot entity changes), `ResourceHudUI` and `DebugResourceMenu` (resolve via the link). "Caravan destroyed = stock lost" is moot while destruction is the lose state.

## 6. File manifest

New: `Components/Structures/CaravanComponents.cs`, `Authoring/Buildings/CaravanAuthoring.cs`, `Systems/PlayerSystemGroup/WorkstationInputSystemGroup/{CaravanLinkSystem, CaravanBoardSystem}.cs`, `Systems/MovementSystemGroup/MovementExecutionSystemGroup/{CaravanDriveSystem, CaravanCrashSystem, CaravanExitWalkSystem}.cs`, `Systems/BuildingsSystemGroup/{CaravanDoorSystem, CaravanExitSequenceSystem, CaravanChimneySystem}.cs`, `MonoBehaviours/CaravanPresenter.cs`, `Tests/PlayMode/{CaravanDriveTests, CaravanCrashTests}.cs`.
Changed: `PlayerWorkstationComponents.cs`, `PlayerInputManager.cs`, `WorkstationSummonSystem.cs`, `WorkstationSummonSpawnSystem.cs`, `UnitSO.cs` + `UnitBlob.cs` + the unit baker, `VfxType.cs`, `DamageSource` enum, `Caravan.prefab`, `CampLoop.unity` / `CampLoop_Sub.unity`, and the Phase 2 touch list in §5.
Art and animation owed (add to `Spencer/Animations_Needed.md`): `CaravanHopDown` (all walk-out units, P1, 1), horse `Idle`/`Walk`/`Trot` (P1, L), smoke-burst and chimney particle prefabs for `VfxManager`.

## 7. Build phases (detail)

**Phase 0.** Add `WorkstationAuthoring` + `CaravanAuthoring` to `Caravan.prefab` (mount point = `WorkTable`, summon point = behind `BackZone`). Strip the Rigidbody. Keep the BoxColliders that become the baked physics shape and let the presenter companion carry the visuals ← DECISION CV-Q9 (bake the mesh into the subscene and keep a separate presenter prefab, or a companion GameObject — check how `WorkstationGhostPresenter` and the Rive panel already handle this). Place it in `CampLoop_Sub`, and remove the loose scene copy from `CampLoop.unity`. `CaravanLinkSystem`. Gate: mount + summon work exactly as with the placeholder.

**Phase 1.** Components, `CaravanBoardSystem`, `CaravanDriveSystem`, `CaravanDoorSystem` (open on park, close on board), `CaravanPresenter` (transform, wheels, front axle, doors, horse), the input map watch. PlayMode fixture: the drive integrator (throttle reaches max, brake stops, no yaw at zero speed, a static wall stops the cast).

**Phase 2.** §5. PlayMode fixture: a gather deposit and a summon spend both land on the caravan's stack.

**Phase 3.** `DamageSource.Vehicle`, `CaravanCrashSystem`, and the presenter's dust (optional). PlayMode fixture: a hit at top speed deals the expected damage, a minion takes 0, and the rehit cooldown holds.

**Phase 4.** `UnitSO.caravanExitStyle` + blob, `CaravanExitSequenceSystem`, `CaravanExitWalkSystem`, the `WorkstationSummonSystem` hand-off, the presenter's shake and burst doors, `VfxType.CaravanSmokeBurst` + a `VfxManager` entry.

**Phase 5.** `CaravanChimneySystem` + the presenter's chimney toggle.

## 8. Verification (goes into `verify-caravan.md` at retirement)

Scene `Scenes/CampLoop.unity`, rebake first.
- Walk to the driver side and press E: the player vanishes onto the seat and the camera switches to the vehicle cam.
- W from standstill: speed ramps, the wheels spin forward, and the horse walks then trots. S brakes, then reverses slowly with the wheels counter-rotating. A/D at a stop: no turn.
- Drive into a wall: the caravan stops and doesn't clip through.
- Press E at speed: refused with a message. Stop and press E: the doors open, the player steps out, and you can mount the work table.
- Phase 2: the HUD values are unchanged after the move. Minions depositing wood raise the HUD. Save → load restores it.
- Phase 3: plough through bandits at full speed. Lethal hits ragdoll forward. Survivors are knocked aside and stunned. Your own minions are shoved but their health stays full. The caravan's health ticks down and it slows.
- Phase 4: summon a zombie and the doors open, it walks out and hops down, and the doors close. Summon a brute and the caravan shakes, the doors slam open, smoke billows, and the brute is standing in the smoke when it clears.
- Phase 5: burning with wood smokes the chimney, both parked and driving. At 0 wood the smoke stops.

## 9. Open questions the build must answer by looking (CV-O)

- **CV-O1** The wheel and door pivots in `Caravan.fbx`: are the wheels centred on their axle, and do `*HingeControl` sit on the hinge line? If not, add pivot empties in the prefab rather than editing the FBX.
- **CV-O2** The Rive "Horse Rive Panel": what does it display, and should the presenter feed it anything (speed? health?)? Leave it untouched unless it is obvious.

## 10. Settled decisions (owner, 2026-10-23)

| ID | Decision |
|---|---|
| CV-D1 | The caravan **is** the workstation: one entity, always present. Parking turns on workstation mode, and driving off replaces pack-up. B-key ghost deploy is retired for the caravan, and the tournament hut keeps it. |
| CV-D2 | The caravan has its own health: the workstation's `Health` + repair + lose rule carry over. |
| CV-D3 | Horse-drawn. The horse is part of the art rig, animated by the presenter from speed. |
| CV-D4 | Arcade kinematic driving in DOTS, tuned in the inspector. No dynamic rigidbody. |
| CV-D5 | The caravan holds the resource ledger. |
| CV-D6 | Crash = speed-scaled damage + stun + shove, ragdoll on kill. **Everyone** is knocked aside, but the player's **minions take no damage**. |
| CV-D7 | Hybrid presenter MonoBehaviour for wheels, doors, shake, chimney, horse and Rive. ECS owns the state. |
| CV-D8 | The furnace burns, and the chimney smokes, any time it has wood, parked or driving. |
| CV-D9 | E boards. E (`ExitVehicle`) parks and steps out, only below `parkMaxSpeed`. Parking opens the workstation. |
| CV-D10 | Exit style is a per-`UnitSO` enum `CaravanExitStyle { WalkOutAndHop, SmokeBurst }` baked to `UnitDataBlob`. |
| CV-D11 | No new top-level group: systems spread across WorkstationInput / MovementExecution / Buildings / Spawn. |
| CV-D12 | First milestone = drive + park + wheels/doors (Phases 0–1). |
| CV-D13 | (2026-10-26, overturns CV-Q6) **A caravan at 0 HP breaks; it isn't lost.** While broken: no driving (the driver is thrown out, boarding is refused) and no summoning (requests are dropped unpaid, the queue waits). Storage, furnace, crafting and repair keep working; a wood repair brings it straight back. `WorkstationDestroyedSystem` skips caravans, and the hut keeps the old rule. |
| CV-D14 | (2026-10-26) You lose by going down yourself, not by losing the caravan. The 5 s knockout and wake-at-the-caravan stay, but going down while the caravan is broken (or with no workstation) is game over (`PlayerKnockoutSystem`). |
| CV-D20 | (2026-10-28) **Doors open from zones, as baked ECS.** `DoorAuthoring` (one per door: name, signed `openDegrees`, timings) and `DoorZoneAuthoring` (a local box, an `openers` mask Player / Minions / OtherUnits, and the list of doors it opens) live on the baked `CaravanSim`, and any baked building can use them too. `DoorZoneSystem` sets `Door.zoneWantsOpen` while an allowed opener stands in a zone. A door in several zones stays open while any of them is occupied. `DoorSystem` settles and swings each door: a door with a mesh swings its own `LocalTransform`, and the caravan art mirrors `Door.angleDegrees` by door name (`CaravanPresenter.doors`). Owner calls: who opens is set per zone (caravan zones are player-only, and minions only sometimes); a zone on a vehicle being driven opens nothing, so nobody gets in or out; **mounting stays separate** (`CaravanBoardSystem`'s side/back rule is untouched, and board/park no longer move doors). The summon exits override the zones on `CaravanAuthoring.summonExitDoors` (`DoorOverride.HoldShut` → burst → `HoldOpen`, then `None` hands them back). Future (not built): AI drivers, e.g. fleeing into a vehicle and riding off, or chasing you in one. `CaravanDriving.driver` is already any entity. |
| CV-D19 | (2026-10-27) **Only heavy things cost momentum.** Rocks and other dropped items are driven straight through (no wall stop). Thrown rocks (`DamageSource.Throw`) do `thrownDamageMultiplier` (10%) damage to a vehicle and never slow it. Ramming only costs speed against units whose `UnitSO.isHeavy` is ticked (the Brute), at `speedLossPerHeavyUnit` each; light units cost nothing. Any other single hit at or above `heavyHitDamageThreshold` (30) eats momentum in proportion, and stops it dead at `fullStopHitDamage` (120). That's `VehicleDamageSystem`, between `DamageResolutionSystem` and `DamageEventSystem`. All of these numbers are on the VehicleProfileSO. |
| CV-D18 | (2026-10-26) **Vehicle data lives in a `VehicleProfileSO` per vehicle** (`ScriptableObjects/Vehicles/`, keyed by `VehicleType`, baked through `_VehicleLibrary.asset` → `VehicleLibrary` blob). It holds driving, wheel radius, footprint, crash, ride, and per-facing offsets for the driver seat and puller (8 directions, the toolkit's `Direction` enum, quantised with `FacingResolver` the same way units are), plus the puller's walk/run thresholds. Horse-drawn (`Drawn`) and cars (`SelfPropelled`, no puller) share one handling model and differ only in numbers (owner call). The entity carries just `Vehicle { vehicleType }`. `CaravanDriveTuning`/`CaravanCrashTuning` and their inspector fields are gone, and doors and summon exits stay on `CaravanAuthoring` because they're caravan-only. The Caravan profile has the old `HorseCaravanOffset` values. `CaravanPresenter.puller` (the Horse) is placed per facing, and `CaravanDriveState.pullerGait` is ready for the horse's own animations. |
| CV-D17 | (2026-10-26, replaces CV-D4's throttle/steer feel) **Handling = the owner's original pre-DOTS `VehicleController`**, ported in `CaravanDriveMath`. The stick is a world direction to head toward and its tilt is the target speed (`moveSpeed` 7, `forwardAcceleration` 1.2, `forwardDeceleration` 3). The heading eases round with `SmoothDampAngle` (`turnSmoothTime` 2.44), capped at `turnSpeed` 50°/s, or 40% of that at a standstill (`idleTurnSpeedFactor`). The body swings after the heading at `turnSpeed` while rolling. No reverse. The presenter ports `VehicleShakerAnimator`: a speed bounce plus a lean into turns, with the original numbers. Wheel radius is 1.2, as in the old wheel animator. The old tuned values came from git (`fd451b9c^:…/CaravanMovementData.asset`). |
| CV-D16 | (2026-10-26, overturns CV-Q1) **Side = drive, back = workstation.** E anywhere along either side or at the front climbs into the driver's seat; E behind the back doors (caravan-local z below the tailgate) opens the workstation. You can reach it from anywhere along its body: `PlayerInteractableReach` makes `PlayerTargetingSystem` measure 5 m from the hull's capsule, not from the origin near the back axle. |
| CV-D15 | (2026-10-26, overturns CV-Q5) **Your minions are knocked flying and get back up, with no damage.** So is everyone else hit above `minimumImpactSpeed`: they take damage, and the dead stay down. Units with real toolkit ragdoll bodies use the toolkit ragdoll. The rest (all test units today) use the code-driven **fake ragdoll**: they arc, topple in screen space, flail, lie for `0.8 + speed × stunSecondsPerMetrePerSecond` s, then stand up where they landed. Kills from any source now fling a fake-ragdoll unit too (`FakeRagdollDeathSystem`). |

## 11. Open decisions (all resolved 2026-10-24, the recommended option each time)

- [x] **CV-Q1** Board vs mount when both are in reach: board by proximity to the board point (recommended: the driver side and the work table are on opposite ends, so they never overlap) or by `Target`.
- [x] **CV-Q2** Walk-out AI gate: reuse the `CutsceneActor` gate (recommended: it already blocks AI, movement and damage, and immunity during the hop is fine) or a new `ScriptedMotion` bit.
- [x] **CV-Q3** Does the next summon's build timer pause while an exit plays? Recommended yes (simplest, and one exit at a time).
- [x] **CV-Q4** Chimney smoke: a looping `ParticleSystem` on the prefab toggled by the presenter (recommended) or puffs through `VfxRequest`.
- [x] **CV-Q5** ~~Do minions get stunned when rammed, or only shoved?~~ Superseded by CV-D15.
- [x] **CV-Q6** ~~Caravan destroyed while driving: instant lose?~~ Superseded by CV-D13/CV-D14.
- [x] **CV-Q7** Scenes with no caravan or hut: the Player keeps fallback ledger buffers (recommended).
- [x] **CV-Q8** Field revives far from the caravan still draw rounds from its ledger (recommended yes).
- [x] **CV-Q9** How the prefab splits between the baked entity and the presenter GameObject (check the existing hybrid pattern first).

**CV-Q9 as built:** the baked side is `Prefabs/Vehicles/CaravanSim.prefab` in `CampLoop_Sub` (BoxCollider + kinematic Rigidbody on the Structures layer, `WorkstationAuthoring` + `CaravanAuthoring`, no renderers). The art stays a plain GameObject in `CampLoop.unity` (`Caravan.prefab`), and `CaravanPresenter` snaps it to the entity every LateUpdate. **CV-Q4 as built:** both smokes are ParticleSystems on `Caravan.prefab` (`Shakable/ChimneySmoke`, `SmokeBurst`), played by the presenter, so there's no `VfxType` and no `VfxManager` wiring.

## 12. As built: where the code departs from §3–§7

- **The crash producer lives in `CombatExecutionSystemGroup`** (before `AttackRequestSystem`, the `HazardZoneSystem` slot), not MovementExecution. That's the documented safe window for a main-thread `DamageBus.raw` Enqueue. It sees this frame's caravan position because Movement runs earlier in Simulation.
- **`StunnedState` had no consumer anywhere.** The new generic `StunAndShoveSystem` (MovementExecution, before `UnitMoverSystem`) gives it one: a stunned unit, or one being slid by `ShoveMotion`, has `Movement.targetPosition` pinned to where it stands. It's **movement-only**: a stunned unit can still swing at what's in reach. Both components are added on first use by ECB, and units don't bake them.
- **The doors rest Open while parked** and Closed while driving. The smoke burst shuts them first, then shakes and slams them open. `CaravanDoorControl` (`Utils/CaravanDoorStateMachine.cs`) is the one state machine both the sim and the presenter read.
- **The player is not hidden while driving.** They sit visibly on `DriverAnchor` (seat `(0, 2.42, 2.72)`), placed after gravity each frame with `Movement.targetPosition` and `Gravity.verticalVelocity` pinned. The camera keeps following the player (`CameraTargetController`'s Vehicle case already does), and it uses `vehicleCam` only when the scene assigns one (`CameraManager.HasCamera`).
- **The horse is not animated.** Owner 2026-10-26: the Rive horse panel won't be used; the horse will get the normal toolkit animations the owner built. So CV-O2 is closed as "not Rive", and wiring the horse is future work.
- **The ledger moves on link, it isn't copied:** `CaravanLinkSystem` moves the player's rows, caps and pending deltas onto the caravan and zeroes the player's rows, so a missed call site shows zeros rather than spending a ghost copy. The save snapshot stays on the Player (its save identity) and mirrors whichever entity `ResourceLedgerLink` names.
- **Defaults measured off `Caravan.fbx`:** wheels pivot on their axles (radius 0.79), the axle bars pivot at the origin (so a new `Wheels/FrontAxlePivot` at `(0, 0.82, 1.21)` now parents the front axle and front wheels), and the door hinge controls sit on the outer hinge line. +Z is forward, and the back doors are at z ≈ −2.3 with the floor at y ≈ 1.93.
- **CV-D9 board point:** driver side `(-2.6, 0, 3)`. The work table mount moved to the back, `(0, 0, -3.6)`, so the two never compete.
- **Fake ragdoll (CV-D15).** `FakeRagdollAuthoring` sits on `MaleCitizen.prefab`, so every test unit has it; the player (BaseUnit) doesn't. `FakeRagdollMotionSystem` (MovementExecution, after gravity) owns the root while the unit is down and gates AI with `CutsceneActor`. `FakeRagdollPoseSystem` (Animation, after `VisualBillboardSystem`) topples the Visual through its `PostTransformMatrix` and flails the limbs, then restores the rest pose it captured on the first frame down. A pooled corpse handed out again is reset by `SpawnStateInitSystem` (phase Restoring). The toolkit branch (`drivesToolkitRagdoll`) is written but can't be exercised: no unit has ragdoll bodies yet, and whether the toolkit's release snaps a body back to where it was hit is unverified.
- **Broken (CV-D13).** `CaravanBrokenSystem` mirrors `Health ≤ 0` into `CaravanBroken`. The presenter slumps the body 4° and plays `Shakable/BrokenSmoke`.

