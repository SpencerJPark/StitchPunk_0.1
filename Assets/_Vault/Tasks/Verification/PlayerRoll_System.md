---
title: Player Dodge Roll
status: built 2026-10-23, play-test owed
created: 2026-10-23
area: player, combat, presentation
---

# Player Dodge Roll — System Spec

## Context

The necro engineer is the weakest body on the field, so the roll is how the player survives. It has to feel good. The reference is **Cult of the Lamb's roll**: a fast burst that slows down at the end, invulnerable for most of it, goes where you push, no stamina, a very short cooldown, and it interrupts your own swing.

**What exists today (2026-10-23):**
- `Roll` is bound to **Space** and **gamepad South** in `Assets/Settings/Input/PlayerControls.inputactions`.
- `PlayerInputManager.OnRoll` enables `OnRollPlayerInput { rollTime = 0.5 }`. `PlayerRollInputSystem` counts the timer down and disables it. **Nothing else reads it**, so pressing roll does nothing.
- The player has **no animations** (the actor profile was deleted in c07dfd75). The body is a paper doll of quads (`BodyPart` buffer on the root) under a `Visual` child that `VisualBillboardSystem` turns to face the camera. Every bit of polish in v1 is therefore code-driven. A real roll clip can be added on top later (`Spencer/Animations_Needed.md` row "Dodge roll").
- **There are no projectiles yet** (`RangedCombat_System.md` is unbuilt). All damage goes through `DamageBus` into `DamageEventSystem`, so one check there covers melee, thrown items and hazards now, and projectiles later.

## Owner calls (2026-10-23, settled; do not re-ask)

| # | Call |
|---|---|
| RL-O1 | **I-frames cover most of the roll:** invulnerable from the start until 80% through, then a short vulnerable recovery |
| RL-O2 | **Short cooldown only:** no stamina or charges. About 0.2s after a roll, and a press during the end of a roll or the cooldown is **buffered** |
| RL-O3 | **The roll interrupts the player's own melee swing at any point**, including before the hit lands |
| RL-O4 | **Polish in v1:** squash/stretch + spin, dust puffs, roll sound, afterimage trail (all four) |
| RL-O5 | **Attack out of a roll is queued:** an attack pressed in the last `attackBufferWindow` (0.15s) of a roll fires the frame the roll ends |
| RL-O6 | **Hostile units body-block the player while walking; a roll passes through them.** Rolling through a horde is the intended escape. Minions and non-hostile units never block. (Discovered 2026-10-23: before this, the player walked through *every* unit. The `BaseUnit` capsule's exclude mask 1663 drops the Units layer, so there was nothing to roll past) |

## Architecture calls (made by Claude per the delegated-architecture rule)

| # | Call | Why |
|---|---|---|
| RL-D1 | **Entry: keep `OnRollPlayerInput` as an enableable input event**, now meaning "roll was pressed" (its `rollTime` field becomes `bufferRemaining`). The roll's own state is a new enableable `PlayerRoll` on the player | Matches every other `On*PlayerInput`. The press and the roll are different lifetimes, and the buffer lives on the press |
| RL-D2 | **Motion goes through the movement toolkit's `UnitMoverSystem`**, not a separate position write: during the roll `PlayerRollMoveSystem` writes `Movement.targetPosition` far along the roll direction and overrides `Movement.moveSpeed` with the curve's speed for that frame. The speed it replaced is saved in `PlayerRoll.restoreMoveSpeed` and written back when the roll ends | Wall-sliding and collision come free. Nothing else writes the player's `moveSpeed` after bake (`UnitSpeedBakingSystem` is post-bake only), so restoring it is safe |
| RL-D3 | **Speed curve:** `speed(t) = peakSpeed · (1 − t)^curvePower` over normalized time `t`, with `peakSpeed` solved so the roll covers exactly `rollDistance` (`peakSpeed = rollDistance · (curvePower + 1) / duration`). Default `curvePower = 1.6` | A fast start that slows down is what makes a roll read as snappy rather than floaty. Solving for distance keeps tuning to two numbers that make sense (distance, duration) |
| RL-D4 | **Direction:** move input if it's held when the roll starts, otherwise the way the root is facing. **Locked for the whole roll**, with no steering | Cult of the Lamb's roll is committed, and locking it makes the dodge readable for enemy AI later |
| RL-D5 | **Invulnerability is a generic enableable `DodgeInvulnerable`** (not player-named), checked in `DamageEventSystem` next to the existing dead/cutscene-actor skips | An enemy or minion dodge can reuse it later, and one check covers every damage source |
| RL-D6 | **Tuning lives on `PlayerControllerAuthoring` under `[Header("Roll")]`, baked into a `PlayerRollSettings` component** | There's only one player, so the blob-library pattern would be overkill. The same file already holds per-player tuning (`onFootCommandRadius`) |
| RL-D7 | **VFX uses a new generic one-frame `VfxRequest` signal entity**, plus `VfxDrainSystem` (copies requests into a `ResolvedVfx` singleton list, then `DestroyEntity(query)`), plus a `VfxManager` MonoBehaviour that keeps a `ParticleSystem` pool per `VfxType`. This mirrors `PlaySound` → `VoiceSelectionSystem` → `AudioManager` exactly | Particle systems don't run in pure ECS. This pattern is the project's proven bridge, and hit sparks, blood and impacts can reuse it later |
| RL-D8 | **Afterimages are copies of the player's body-part quad entities** (`EntityManager.Instantiate` on each `BodyPart.entity` that renders). Each copy has its `Parent` removed, its world pose frozen, `BodyPartTint` set to a ghost colour that fades, and gets `Lifetime` → `Despawn { ForceDestroy }` through the existing despawn funnel | A copy keeps the part's own texture and material, so the ghost is the real silhouette rather than a rectangle. No new shader is needed |
| RL-D10 | **Body-blocking is a game-side push-out, not a physics-layer change:** `PlayerBodyBlockSystem` runs in `MovementExecutionSystemGroup` after `UnitMoverSystem`. It calls `CollisionWorld.OverlapSphere` on the Units layer around the player, keeps hits whose `AttackFaction` buffer contains `FactionType.Player` and that are not `Dead`, and pushes the player out on XZ to `bodyBlockRadius` (1.0 = two 0.5 capsules). It's capped at `bodyBlockPushSpeed` (8 u/s, above walk speed so the block holds), and the push is collider-cast against the player's own filter so it never shoves the player into a wall. Skipped while `PlayerRoll` is enabled. Ending a roll inside an enemy eases the player out at the cap instead of snapping | Layers can't tell an enemy from a minion (both are layer 6), and the movement toolkit must stay faction-agnostic. The hostility test is the one `DamageEventSystem.SourceHostileToTarget` already uses |
| RL-D9 | **Squash/stretch and spin are applied to the `Visual` child** (the `VisualBillboard` entity whose `Parent` is the player). Scale goes through a `PostTransformMatrix`, added at runtime the first time it's needed. Spin is a rotation around the visual's local Z, multiplied **after** `VisualBillboardSystem` has set the facing | Animating the visual instead of the root leaves movement and collision untouched. The visual is shared by all units, so the matrix is added only to the player's visual and never baked onto every unit |

## Skills Needed

- `dots-system-scaffold`: four new systems (input/start, move, visual, afterimage fade) plus the VFX drain
- `dots-authoring-baker`: the `[Header("Roll")]` block and `PlayerRollSettings` baking in `PlayerControllerAuthoring`
- `dots-test`: one PlayMode fixture for the roll state machine (see Verification)

## Data model

All in `Assets/_Scripts/Components/Player/PlayerComponents.cs` unless noted.

```csharp
// Baked once from PlayerControllerAuthoring [Header("Roll")].
public struct PlayerRollSettings : IComponentData
{
    public float rollDistance;             // 3.2   world units covered by one roll
    public float rollDuration;             // 0.34  seconds
    public float speedCurvePower;          // 1.6   RL-D3
    public float invulnerableUntilFraction;// 0.8   RL-O1
    public float cooldownAfterRoll;        // 0.2   RL-O2
    public float inputBufferWindow;        // 0.15  a press this close to the end is kept
    public int   afterimageCount;          // 3     spaced evenly through the roll
    public float afterimageLifetime;       // 0.18
    public float4 afterimageTint;          // pale teal, alpha 0.55
    public float spinTurns;                // 1.0   full turns of the visual per roll
    public float landingSquashDuration;    // 0.12
    public float attackBufferWindow;       // 0.15  RL-O5
    public float bodyBlockRadius;          // 1.0   RL-D10
    public float bodyBlockPushSpeed;       // 8     RL-D10
}

// Repurposed (RL-D1): enabled = roll pressed and still buffered.
public struct OnRollPlayerInput : IComponentData, IEnableableComponent
{
    public float bufferRemaining;          // was rollTime
}

// Enabled = mid-roll. Disabled but present between rolls (baked disabled).
public struct PlayerRoll : IComponentData, IEnableableComponent
{
    public float3 direction;               // XZ, normalized, locked at start
    public float  elapsed;
    public float  restoreMoveSpeed;        // RL-D2
    public int    afterimagesSpawned;
    public float  landingSquashRemaining;  // visual recovery after the roll ends
    public bool   attackQueued;            // RL-O5
}

// Enabled = cooling down. Same shape as AttackCooldown.
public struct RollCooldown : IComponentData, IEnableableComponent
{
    public float remaining;
}
```

`Components/Combat/DodgeInvulnerable.cs`: `public struct DodgeInvulnerable : IComponentData, IEnableableComponent { }`

`Components/Effects/VfxComponents.cs`: `VfxRequest { VfxType type; float3 position; float3 direction; float scale; }` (one-frame signal entity), `ResolvedVfx { NativeList<VfxRequest> requests; }` (singleton, owned by `VfxDrainSystem`, `IsCreated` guard on dispose).

`Components/Effects/AfterimageGhost.cs`: `AfterimageGhost { float age; float lifetime; float4 startTint; }`

`Data/Enums/VfxType.cs`: `None, RollDustStart, RollDustLand`.
`Data/Enums/SoundType.cs`: **append** `RollWhoosh, RollLand` at the end. Inserting them in the middle would shift every serialized `SoundType`.

## Systems

| System | Group / order | Does |
|---|---|---|
| `PlayerRollStartSystem` (replaces `PlayerRollInputSystem`: `git mv` + rewrite) | `PlayerInputSystemGroup`, `[UpdateBefore(typeof(PlayerAttackSystem))]` | Ticks `OnRollPlayerInput.bufferRemaining` down and drops the press when it runs out. Starts a roll when a press is live and the player is **not** rolling, **not** on `RollCooldown`, the action map is `Player`, there's no cutscene (same early-out as today), and the player is not knocked out or mounted. **On start:** works out the direction (RL-D4); saves `moveSpeed`; enables `PlayerRoll` and `DodgeInvulnerable`; consumes the press. **Cancels the swing (RL-O3):** disables `AttackRequest`, clears `AttackCooldown`, and disables `OnAttackPlayerInput`. **Cancels aim:** disables `AimPlayerInput` and hides the indicator the way `PlayerAimSystem` does. Spawns `PlaySound { RollWhoosh, followSource }` and `VfxRequest { RollDustStart }` at the feet |
| `PlayerRollMoveSystem` (new) | `MovementFollowerSystemGroup`, `[UpdateAfter(typeof(PlayerMoveSystem))]` | Advances `elapsed`, writes `targetPosition = position + direction · 50` and `moveSpeed = speed(t)` (RL-D3), and sets `isRunning = false`. Disables `DodgeInvulnerable` once `t ≥ invulnerableUntilFraction`. Spawns an afterimage at each even slice of `t`. **On end:** restores `moveSpeed`, disables `PlayerRoll`, makes sure `DodgeInvulnerable` is off, sets `RollCooldown = cooldownAfterRoll`, sets `landingSquashRemaining`, and spawns `PlaySound { RollLand }` + `VfxRequest { RollDustLand }`. Ticks `RollCooldown` down. **It runs after `PlayerMoveSystem`, so its write wins during a roll.** `PlayerMoveSystem` stays untouched |
| `PlayerRollVisualSystem` (new) | `AnimationSystemGroup`, `[UpdateAfter(typeof(VisualBillboardSystem))]` | For the `VisualBillboard` entity whose parent is the player: **wind-up** (first 8% of the roll) squashes to (1.2, 0.8); **mid-roll** stretches along the roll's on-screen direction (1.1, 0.9) and spins `spinTurns · 360°` around local Z on an ease-out curve, with the spin sign taken from the roll direction's screen X; **landing** squashes to (1.15, 0.85) and springs back to 1 over `landingSquashDuration`. Writes the scale into `PostTransformMatrix`, adding it with an ECB on first use. Leaves it at identity when idle |
| `AfterimageSpawn` (a function in `PlayerRollMoveSystem`, not its own system) | — | For each `BodyPart` on the player that has `MaterialMeshInfo`: `Instantiate`, remove `Parent`, set `LocalTransform` from the part's `LocalToWorld` (position, rotation, uniform scale), add `AfterimageGhost`, set `BodyPartTint = afterimageTint`, add `Lifetime { afterimageLifetime }`. Uses an ECB from `EndSimulationEntityCommandBufferSystem` |
| `AfterimageFadeSystem` (new) | `PresentationSystemGroup` | Ages each ghost and lerps `BodyPartTint` from `startTint` to `startTint` with alpha 0 (or toward the background tone; see the probe in Phase 4). Scales each ghost to 0.9× over its life. `LifetimeSystem` → `Despawn` handles destruction |
| `VfxDrainSystem` (new, not Burst because of the structural change) | `PresentationSystemGroup` | Copies every `VfxRequest` into `ResolvedVfx.requests`, then `DestroyEntity(query)` |
| `VfxManager` (MonoBehaviour, `MonoBehaviours/Managers/`) | `LateUpdate` | Reads `ResolvedVfx`, plays a pooled `ParticleSystem` per entry at its position, aligned so the dust kicks backward along `direction`, then clears the list |

**Edits to existing systems:**
- `DamageEventSystem`: `continue` when the target has `DodgeInvulnerable` enabled. Place it next to the dead-victim and cutscene-actor skips, with a one-line comment. This also skips threat registration, which is correct: a dodged swing shouldn't count as a hit.
- `PlayerAttackSystem`: while `PlayerRoll` is enabled, an attack press is consumed. If it lands within `attackBufferWindow` of the roll's end it sets `PlayerRoll.attackQueued`, otherwise it is dropped. When the roll ends, `PlayerRollMoveSystem` re-enables `OnAttackPlayerInput` if `attackQueued` is set, and the swing fires next frame through the normal path (RL-O5).
- `PlayerAimSystem`: ignore aim input while `PlayerRoll` is enabled.
- `PlayerInputManager.OnRoll`: write `bufferRemaining = inputBufferWindow`. Read it from `PlayerRollSettings` on the player, and delete the `ROLL_DURATION` const.
- `PlayerControllerAuthoring.Baker`: bake `PlayerRollSettings`, plus `PlayerRoll`, `RollCooldown` and `DodgeInvulnerable`, all baked **disabled**.

**Buffering detail (RL-O2):** a press is always recorded. The window is measured against **landing**, not against the end of the cooldown (the 0.2s cooldown is longer than the 0.15s window, so measuring to the end of the cooldown would make a press near landing impossible to buffer, which `PlayerRollTests` caught). A press within `inputBufferWindow` of landing, or any press during the cooldown, holds and fires the frame the cooldown ends. A press made earlier in the roll counts down and is dropped, so you can't queue a roll from far back.

## File manifest

| File | New / edit |
|---|---|
| `Components/Player/PlayerComponents.cs` | edit: `PlayerRollSettings`, `PlayerRoll`, `RollCooldown`; `OnRollPlayerInput.rollTime` → `bufferRemaining`; fix the stale comment on `AttackCooldown` |
| `Components/Combat/DodgeInvulnerable.cs` | new |
| `Components/Effects/VfxComponents.cs` | new |
| `Components/Effects/AfterimageGhost.cs` | new |
| `Data/Enums/VfxType.cs` | new |
| `Data/Enums/SoundType.cs` | edit (append two entries) |
| `Authoring/Player/PlayerControllerAuthoring.cs` | edit |
| `Systems/PlayerSystemGroup/PlayerInputSystemGroup/PlayerRollStartSystem.cs` | `git mv` from `PlayerRollInputSystem.cs` + rewrite |
| `Systems/MovementSystemGroup/MovementFollowerSystemGroup/PlayerRollMoveSystem.cs` | new |
| `Systems/AnimationSystemGroup/PlayerRollVisualSystem.cs` | new |
| `Systems/MovementSystemGroup/MovementExecutionSystemGroup/PlayerBodyBlockSystem.cs` | new (RL-D10) |
| `Systems/PresentationSystemGroup/AfterimageFadeSystem.cs` | new |
| `Systems/PresentationSystemGroup/VfxDrainSystem.cs` | new |
| `Systems/CombatSystemGroup/CombatReactionSystemGroup/DamageEventSystem.cs` | edit (one skip) |
| `Systems/PlayerSystemGroup/PlayerInputSystemGroup/PlayerAttackSystem.cs` | edit (one guard) |
| `Systems/PlayerSystemGroup/PlayerInputSystemGroup/PlayerAimSystem.cs` | edit (one guard) |
| `MonoBehaviours/Managers/PlayerInputManager.cs` | edit |
| `MonoBehaviours/Managers/VfxManager.cs` | new |
| `Assets/Prefabs/VFX/RollDust.prefab` | new: small `ParticleSystem` (8–12 soft grey puffs, 0.35s, burst, gravity −0.2, size over life shrinking). Built in the Editor through MCP, not by a subagent |
| `Assets/Scenes/Game.unity` | add `VfxManager` next to `AudioManager`, with the dust prefab assigned for both `VfxType` entries |
| `SoundSO` assets for `RollWhoosh`, `RollLand` | owner supplies clips. Until then the types resolve to no SoundSO and play silently (check that `VoiceSelectionSystem` tolerates a missing entry; see Phase 5) |

## Build phases

1. **Data + bake.** Components, enums, authoring fields, `OnRollPlayerInput` rename (update `PlayerInputManager` in the same phase so it compiles). Gate: `devloop gate` for `StitchPunk.Components`, `StitchPunk.Authoring` and `StitchPunk.MonoBehaviours`.
2. **Core roll: start + move + i-frames + cancels + attack queue + body-block.** `PlayerRollStartSystem`, `PlayerRollMoveSystem`, `PlayerBodyBlockSystem`, the `DamageEventSystem` skip, the attack/aim guards. **The roll is playable at the end of this phase** with no polish. Gate: `StitchPunk.Systems`.
3. **Sound.** The `SoundType` append plus the two `PlaySound` spawns. Confirm a missing SoundSO doesn't throw.
4. **Visual feel.** `PlayerRollVisualSystem`. **Probe first** (one `execute_code`): does `2DTextureArrayShader` / `2DPackedRecolorShader` blend alpha from `_BaseColor`, or is it alpha-clipped? That decides the Phase 5 ghost fade (alpha vs tint toward a pale colour + shrink).
5. **Afterimages.** Spawn function + `AfterimageFadeSystem`.
6. **VFX bridge + dust.** `VfxDrainSystem`, `VfxManager`, the dust prefab, scene wiring.
7. **Editor pass.** Refresh → console clean → enter Play in `Game.unity` → roll. Retire per the plan-system skill.

Phases 3–6 touch disjoint files after Phase 2 lands, so they can run as parallel workers.

## Verification

- **PlayMode fixture** `PlayerRollTests` (one file): manual `World`, a player entity with `Movement`, `PlayerRollSettings`, `PlayerRoll` and `RollCooldown`. Asserts (a) total distance after `rollDuration` is within 5% of `rollDistance` with no walls, (b) `DodgeInvulnerable` is on at 50% and off at 85%, (c) a press during cooldown starts a roll the frame cooldown ends, (d) a press older than the buffer window does not. Before keeping each assert, revert the code it covers and watch it fail.
- **Play checklist** (`verify-player-roll.md`, written at retirement): roll in all 8 directions; roll standing still goes the way you face; roll into a wall slides along it; walk into a zombie and get blocked, walk into your own minion and pass through, roll through a zombie pack and come out the far side; end a roll inside a zombie and ease out without a snap; attack in the last moment of a roll and the swing comes out on landing; let a zombie swing land mid-roll and take no damage, then roll late and get hit; roll mid-swing and the swing never lands; roll mashing comes out as a steady cadence, not a stall; squash/spin/landing read clearly from the game camera; 3 ghosts trail and fade; dust at both ends; no console warnings.

## Open questions (none block the build; defaults shown)

- [x] ~~Attack straight out of a roll?~~ Yes, queued (RL-O5).
- [x] ~~Roll through enemies?~~ Yes. Walking is blocked by hostile units only (RL-O6, RL-D10).
- [ ] **Tuning numbers** (distance 3.2, duration 0.34, cooldown 0.2): starting points. Change them on the player prefab after the first play.
