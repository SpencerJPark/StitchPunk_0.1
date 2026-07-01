# DamageEvent v2 — Scalable Source-Agnostic Damage Bus

> **Status:** 🔨 built — code landed, verify pending (see [`../Verification/verify-damageevent-v2.md`](verify-damageevent-v2.md)). Compile gate NOT run this session (Unity MCP not connected); left to verification.
> **Builds on:** [`../Completed/DamageEvent_System.md`](../Completed/DamageEvent_System.md) (v1 — the Hurt-buffer → signal-entity refactor, now landed & compiled). This is v2: swap the transport for a queue, generalize the damage model beyond "attacker", and light up AOE friendly-fire.
>
> **Resolved decisions (build):** bus owner = `GameManagerSystemGroup` OrderFirst · **fields renamed** `attackType→damageSource` (`[FormerlySerializedAs]` on `AttackSO`) · `HazardZoneSystem` in `CombatExecutionSystemGroup` (`[UpdateBefore(AttackRequestSystem)]`) · **whole-zone** retrigger gate · Cone/Line/Chain fall back to single-target · fall/hazard ragdoll uses **hazard/impact X**. **Deviation:** the producer-written `aoeCount` was dropped (parallel `NativeReference` increment is a race) — `DamageResolutionSystem` scans the drained `raw` array on the main thread to detect AOE presence.

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `dots-system-scaffold` — new `DamageBusSystem` (owner), `DamageResolutionSystem` (AOE expand job), rewritten `DamageEventSystem` (consumer), new `HazardZoneSystem` (§5). Also the `ScheduleParallel` expand job.
- `dots-authoring-baker` — new `HazardAuthoring` + `Baker` for the spike-hazard example (§4/§6).

---

## Context — why this change

v1 replaced the per-unit `Hurt` buffer with a one-frame `DamageEvent` **entity** spawned via a dedicated combat ECB and destroyed each frame (`DestroyEntity(query)`). That fixed the O(all-units) scan, but a scale review flagged two ceilings:

1. **Per-hit structural churn** — every hit does `CreateEntity` + `AddComponent`, then a `DestroyEntity(query)` each frame. Entity create/destroy is the expensive part of ECS; at thousands of hits/frame this is archetype/chunk churn.
2. **The model is attacker-centric** — `attackerEntity` + `AttackType` assume a unit swung a weapon. Damage actually comes from many sources with no attacker: **fall damage, spikes, burning, drowning**. That was the original motivation for the entity/recycle idea.

v2 addresses both: a recycled **`NativeQueue<DamageEvent>` bus** (zero structural change) and a **source-agnostic** event (`sourceEntity` + a `DamageSource` that spans attacks *and* hazards). It also lights up **AOE with friendly fire** (splash hits everyone in radius except the source).

**Locked decisions (Q&A):**
- **Transport:** `NativeQueue<DamageEvent>` bus (ParallelWriter), *replaces* the per-frame entity spawn/destroy + `CombatEventCommandBufferSystem`. DamageEvents are no longer entities (not inspectable in the Entities window — add a per-frame debug counter/log instead).
- **Damage typing:** **rename the existing `AttackType` enum → `DamageSource`** (global, type-only rename — keep field names for SO serialization safety) and add `Fall, Hazard, Burn, Drown`.
- **AOE:** **parallel expand pre-pass** — a `ScheduleParallel` job expands each `AreaOfEffect` event into per-target single-target events; the consumer applies single-target only.
- **Friendly fire:** AOE hits everyone in range **except the source entity** (and skips already-`Dead`). No faction filter on damage.
- **Threat:** registers **only** when the source is *hostile* to the target (source's `Faction` is in the target's `AttackFaction` buffer) — friendly-fire and environmental damage never create a `ThreatEntry`.
- **Env example:** a **spike hazard zone** (`HazardAuthoring` + `HazardZoneSystem`, proximity, plain authored fields) proves the non-attack path.
- **Deferred:** DoT / status-effect ticking (`DamageSource.Burn` reserved, no ticking system this plan); Cone / Line / Chain resolution (carried, fall back to single-target).

## 1. Purpose & v1 scope

Replace the `DamageEvent`-entity transport with a recycled `NativeQueue` bus, generalize `DamageEvent` to a source-agnostic value struct, and resolve AOE (radius) with friendly fire. One consumer still applies damage + threat + death.

**v2 handles:**
- **Bus transport:** producers `Enqueue` `DamageEvent` values into a singleton `NativeQueue`; a resolution pass expands AOE; the consumer drains + applies. No entity create/destroy.
- **Source-agnostic model:** `sourceEntity` (Null-able) + `DamageSource` (attacks *and* Fall/Hazard/Burn/Drown).
- **AOE + friendly fire:** `DamageBehaviour.AreaOfEffect` → radius query, damage all in-range except `sourceEntity` and `Dead`.
- **Threat gate:** only hostile attack sources register `ThreatEntry`.
- **Spike hazard:** `HazardZoneSystem` enqueues `Hazard` damage on overlap (retrigger-throttled).

**Out of v2:** DoT/status effects (reserve `Burn`); Cone/Line/Chain (carried in schema, resolved as single-target for now — ← DECISION); spatial acceleration structure (brute-force snapshot scan in the expand job; note hash as the next optimization).

## 2. Architecture

Producers across several system groups `Enqueue` raw `DamageEvent` values into a persistent `NativeQueue` owned by a bus system. A resolution pass (parallel) expands AOE into single-target events into a second queue. A single main-thread consumer drains that and applies. All same frame.

```
GameManagerSystemGroup (OrderFirst)
  DamageBusSystem  ── owns 2 NativeQueues + aoeCount; clears residue, resets producer handle,
                      hands out the ParallelWriter singleton. (EntityCommandBufferSystem-style owner.)

  producers (parallel jobs, each Enqueue into rawBus + AddJobHandleForProducer):
    CombatExecutionSystemGroup / AttackRequestSystem   (melee → source = attacker, DamageSource = weapon)
    ItemSystemGroup / ThrownItemHitSystem              (thrown → source = Null, DamageSource = Throw)
    <hazard group> / HazardZoneSystem                  (overlap → source = Null/hazard, DamageSource = Hazard)
                                     ▼
CombatReactionSystemGroup
  DamageResolutionSystem  ── completes producer handles; if aoeCount>0 snapshots targets;
                             ScheduleParallel expand: SingleTarget → copy to resolvedBus;
                             AreaOfEffect → radius scan snapshot, emit 1 event per in-range
                             non-source alive target into resolvedBus. Clears rawBus.
                                     ▼
  DamageEventSystem (consumer) ── Burst main-thread: drain resolvedBus:
                             • skip already-Dead target
                             • threat: only if source hostile to target (AttackFaction) → ThreatEntry
                             • damage → Health; on lethal event capture Health.kill* + enable Dead
                             then resolvedBus is empty (drained). Emit a debug count if logging.
                                     ▼
HealthSystemGroup → DeathSystem → Ragdoll2DInitSystem  (unchanged; reads Health.kill*)
```

**← DECISION (bus owner placement):** `DamageBusSystem` `[UpdateInGroup(GameManagerSystemGroup)] OrderFirst` so the reset runs before *any* producer (thrown items live in `ItemSystemGroup`, earlier than combat). Swap to a dedicated init group if preferred.

**Critical gotcha — manual job dependency.** A `NativeQueue` passed through a singleton **bypasses ECS automatic dependency tracking**. Producers write from `ScheduleParallel` jobs in different groups; the resolution job reads the queue. This must be wired exactly like `EntityCommandBufferSystem`: each producer calls `DamageBusSystem.AddJobHandleForProducer(state.Dependency)` after scheduling; `DamageResolutionSystem` does `state.Dependency = JobHandle.CombineDependencies(state.Dependency, bus.ProducerHandle)` before its expand job. **Get this wrong and you get a race / disposed-container crash, not a compile error.** `DamageBusSystem` is therefore a small **managed** system (owns containers + the combined handle); all hot work stays in Burst jobs.

## 3. Entry points

Still the signal pattern, but the signal is now a **queued value**, not an entity:

- **Producer side:** any system obtains `SystemAPI.GetSingleton<DamageBus>().rawWriter` (a `NativeQueue<DamageEvent>.ParallelWriter`) and `Enqueue`s. Melee (`AttackRequest` windup unchanged), thrown items, and the new hazard zone are the v2 producers. For AOE producers, also bump `bus.aoeCount` (a `NativeReference<int>`) so the resolution pass knows to snapshot targets.
- **Consumer side:** `DamageEventSystem` drains the *resolved* queue. No `DestroyEntity` — draining empties it; `DamageBusSystem` clears any residue next frame.

## 4. Data model

No SO→Blob library. `DamageEvent` becomes a **plain value struct** (no longer `IComponentData` — it lives in a queue, not on an entity):

```csharp
public struct DamageEvent   // queued value, not a component
{
    public Entity       targetEntity;    // resolved victim (set at enqueue for SingleTarget, per-target after AOE expand)
    public Entity       sourceEntity;    // was attackerEntity. Null for environmental / sourceless
    public DamageSource damageSource;    // was attackType
    public int          damageAmount;
    public float        distance;
    // death-only knockback (captured into Health.kill* on the lethal event) — see [[project_combat_knockback]]
    public float        hitSourceX;
    public float        ragdollForce;
    public float        launchForceY;
    public float        launchForceX;
    // AOE
    public DamageBehaviour damageBehaviour;
    public float3          sourcePosition; // AOE origin
    public float           range;          // AOE radius
}
```

**Bus singleton** (owned/created by `DamageBusSystem`, `Allocator.Persistent`, disposed in `OnDestroy`):

```csharp
public struct DamageBus : IComponentData
{
    public NativeQueue<DamageEvent> raw;       // producers Enqueue
    public NativeQueue<DamageEvent> resolved;  // expand writes single-target
    public NativeReference<int>     aoeCount;  // >0 ⇒ resolution snapshots targets
    // ParallelWriters fetched fresh each frame via raw.AsParallelWriter()
}
```

**Enum rename (`DamageSource`)** — `Assets/_Scripts/Data/Enums/AttackEnums.cs`. Rename the `AttackType` **type** to `DamageSource`, append env members:

```csharp
public enum DamageSource   // was AttackType — attack + environmental damage origins
{
    None, Instant, Punch, Claw, Throw, Kick, Slash, Stab, Swing, ShootOneHand, ShootTwoHand, Explode,
    Fall, Hazard, Burn, Drown,   // v2 environmental
}
```

> **Rename is serialization-safe** because Unity serializes SO fields by **field name**, not enum type name — so `AttackSO`/`UnitSO`/`ItemSO` assets keep their values as long as we rename the *type* only and leave field names (`attackType`, `killAttackType` → your call) intact. **← DECISION (field names):** rename only the type (recommended, zero re-authoring) vs. also rename fields `attackType → damageSource` (cleaner, needs `[FormerlySerializedAs("attackType")]` on serialized SO fields to preserve data). `Health.killAttackType` is **dead** (written, never read — only `killSourceX` feeds the ragdoll), so renaming it to `killDamageSource` is free.

**Hazard authoring config** — plain authored fields on `HazardAuthoring` (no blob): `damageAmount`, `damageSource = Hazard`, `radius`, `retriggerInterval`. Baked into a `HazardZone` component.

## 5. Systems

**New — `DamageBusSystem`** (`GameManagerSystemGroup`, OrderFirst; managed)
- Creates `raw`/`resolved`/`aoeCount` in `OnCreate` (Persistent), disposes in `OnDestroy`, creates the `DamageBus` singleton.
- Each frame (before producers): `raw.Clear()` / `resolved.Clear()` / `aoeCount.Value = 0`, reset the combined producer handle.
- `public void AddJobHandleForProducer(JobHandle)` (combines) and `public JobHandle ProducerHandle { get; }` — the ECB-owner pattern.

**New — `DamageResolutionSystem`** (`CombatReactionSystemGroup`, `[UpdateBefore(typeof(DamageEventSystem))]`)
- `state.Dependency = CombineDependencies(state.Dependency, bus.ProducerHandle)`.
- If `aoeCount.Value > 0`: gather `NativeArray<TargetSnapshot { Entity entity; float3 pos; }>` from `SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Health>()` (main-thread gather; `Dead` filtered in-job).
- `ScheduleParallel` expand `IJob`/`IJobParallelFor` over `raw.ToArray(Allocator.TempJob)`: `SingleTarget` → `resolvedWriter.Enqueue(evt)`; `AreaOfEffect` → for each snapshot target: skip `entity == sourceEntity`, skip out-of-range (XZ `distancesq` vs `range`), skip `Dead`, else `Enqueue` a copy with `targetEntity = that entity`, `damageBehaviour = SinlgeTarget`. Cone/Line/Chain → treat as SingleTarget on `targetEntity` (← DECISION).
- Clears `raw` after `ToArray`.

**Rewritten — `DamageEventSystem`** (`CombatReactionSystemGroup`, consumer) — same responsibilities as v1, new source:
- `[BurstCompile] ISystem`, `RequireForUpdate<GameSceneTag>`. Early-out if `resolved` empty.
- Burst main-thread `while (bus.resolved.TryDequeue(out DamageEvent e))`: skip already-`Dead`; **threat only if `sourceEntity != Null` and source is hostile to target** — look up source `Faction.factionType`, check the target's `AttackFaction` buffer contains it (reuse the `EnemyAwarenessSystem` hostility test), then the same `threatScore += damageAmount` / `staleTimer = THREAT_TTL` / new-entry `reactionDelay = REACTION_TIME` logic; apply `damageAmount` to `Health`; on lethal event capture `kill*` + enable `Dead`.
- Uses `ComponentLookup<Health>` (RW), `BufferLookup<ThreatEntry>` (RW), `ComponentLookup<Dead>` (RW), `ComponentLookup<Faction>` (RO), `BufferLookup<AttackFaction>` (RO).

**New — `HazardZoneSystem`** (`← DECISION: which group` — recommend a new `HazardSystemGroup` or fold into `CombatSystemGroup`; must run before `DamageResolutionSystem`)
- Proximity check (ThrownItemHit pattern): for each `HazardZone`, any `Health` unit within `radius` (XZ) whose per-unit retrigger cooldown elapsed → `Enqueue` `DamageEvent { sourceEntity = Null (or hazard entity), damageSource = Hazard, damageBehaviour = SinlgeTarget }`; stamp cooldown.
- `← DECISION:` cooldown storage — a `DynamicBuffer<HazardCooldown>` on the hazard vs a simple global `retriggerInterval` gate on the whole zone (simpler; recommended for v2).

**Edited — `AttackRequestSystem`, `ThrownItemHitSystem`** — swap ECB entity-spawn for `bus.rawWriter.Enqueue(...)`; set `sourceEntity`; `AddJobHandleForProducer` (melee, parallel) — thrown items already single-threaded. For any AOE attack, bump `aoeCount`.

**Deleted — `CombatEventCommandBufferSystem`** — the queue replaces the ECB; no playback system needed (same-frame ordering is by system-group placement).

## 6. Authoring bridge

`HazardAuthoring : MonoBehaviour` + `Baker` (use `dots-authoring-baker`): serialized `damageAmount`, `radius`, `retriggerInterval`; bakes `HazardZone` (`TransformUsageFlags.Dynamic` for position). Place one spike-hazard GameObject in `DOTSTestScene`.

## 7. Integration points

- **Health / Ragdoll:** unchanged — `DamageEventSystem` still captures `Health.kill*` on the lethal event; `Ragdoll2DInitSystem` reads `killSourceX`. Death-only knockback preserved ([[project_combat_knockback]]). For sourceless damage (fall/hazard) `hitSourceX` = the hazard/impact X (or unit's own X → straight-down fall) — ← DECISION on fall/hazard ragdoll feel.
- **AI fight-back:** `ThreatEntry` still produced with identical constants **but now faction-gated** — friendly fire no longer provokes `SelfDefenceAwarenessSystem`. Confirm `ThreatDecaySystem` / bravery unaffected ([[project_bravery_system]]).
- **Enum rename ripple:** `AttackType` → `DamageSource` touches **~29 refs across 23 files** (`AttackSO`/`UnitSO`/`ItemSO`, `AttackBlobs`/`ItemBlobs`/`UnitBlob`, `AttackLibraryBakingSystem`, `BehaviorExecutionSystem`, `PlayerAttackSystem`, `MinionSelfDefenceSystem`, `EnemyAwarenessSystem`, `AIUtils`, etc.). Mechanical; do it as one pass and gate on a clean console.
- **Save:** no DTO change (enum values/order unchanged; only the type name and appended members).

## 8. Proposed file manifest

**New:**
- `Assets/_Scripts/Components/Combat/DamageBus.cs` — `DamageBus` singleton (+ `HazardZone`, `HazardCooldown` if used, or a separate `Components/Combat/Hazard.cs`).
- `Assets/_Scripts/Systems/CombatSystemGroup/DamageBusSystem.cs` (managed owner).
- `Assets/_Scripts/Systems/CombatSystemGroup/CombatReactionSystemGroup/DamageResolutionSystem.cs` (AOE expand).
- `Assets/_Scripts/Systems/<HazardGroup>/HazardZoneSystem.cs`.
- `Assets/_Scripts/Authoring/Hazards/HazardAuthoring.cs`.

**Edited:**
- `Components/Combat/DamageEvent.cs` — drop `IComponentData`, `attackerEntity→sourceEntity`, `attackType→damageSource`.
- `Data/Enums/AttackEnums.cs` — rename `AttackType→DamageSource`, add env members (+ every one of the ~23 files referencing the type).
- `CombatReactionSystemGroup/DamageEventSystem.cs` — drain queue, faction-gated threat.
- `CombatExecutionSystemGroup/AttackRequestSystem.cs` + `ThrownItemSystemGroup/ThrownItemHitSystem.cs` — enqueue instead of spawn; `AddJobHandleForProducer`.
- `Components/Units/UnitComponents.cs` — `killAttackType→killDamageSource`.
- `SystemGroups.cs` — declare `HazardSystemGroup` if chosen (← DECISION).

**Deleted:**
- `Systems/CombatSystemGroup/CombatEventCommandBufferSystem.cs`.

**Assets:** a spike-hazard GameObject in `DOTSTestScene` (`HazardAuthoring`). No SO re-authoring (rename is serialization-safe).

## 9. Build phases

1. **Enum rename (isolated, compiles):** `AttackType → DamageSource` + env members across all 23 files. Gate on a clean console before touching anything else — keeps the mechanical churn out of the logic diff.
2. **Bus infra:** `DamageBusSystem` + `DamageBus` singleton + queue lifecycle. Nothing enqueues yet; verify it creates/clears/disposes cleanly (no leak warning on exit).
3. **Migrate producers + consumer to the queue:** rewrite `AttackRequestSystem` / `ThrownItemHitSystem` to `Enqueue`; rewrite `DamageEventSystem` to drain; delete `CombatEventCommandBufferSystem`. **Wire `AddJobHandleForProducer`.** Melee + thrown now flow through the bus, single-target only. (Faction-gated threat lands here.)
4. **AOE expand:** `DamageResolutionSystem` + `aoeCount` + target snapshot + parallel expand. Add a debug AOE attack (or hazard with AreaOfEffect) to exercise friendly fire.
5. **Hazard example:** `HazardAuthoring` + `HazardZoneSystem`; place a spike zone in the scene.
6. **Verify** (below).

## 10. Verification

Play `DOTSTestScene` (re-bake — `HazardAuthoring` + enum are baking-relevant):
- **Parity (melee + thrown):** same as v1 — damage ticks, death, ragdoll direction/feel unchanged; a hit citizen still fights back after the 0.3s flinch. Throw a Rock → damages/kills, **no** threat (source Null).
- **Friendly fire (AOE):** trigger an `AreaOfEffect` hit near mixed allies+enemies → **everyone in radius except the source** takes damage; allies caught in it are damaged but **do not** gain a `ThreatEntry` (inspect the victim's `ThreatEntry` buffer) and do **not** turn on the caster.
- **Hazard:** walk a unit onto the spike zone → periodic `Hazard` damage (retrigger throttled), can kill, ragdoll plays, no threat entry, `DamageSource.Hazard` on the lethal capture.
- **Scale / churn proof:** profiler — **no per-frame entity create/destroy** in combat anymore; `DamageEventSystem` only works on hit frames; the expand job shows as a parallel job only when AOE events exist. Stress: ~50 attackers focus-firing → confirm the consumer is the only main-thread combat cost and the producer stays parallel.
- **Dependency safety:** run with the **Jobs Debugger / safety checks on** — confirm no race or disposed-container errors from the queue (validates `AddJobHandleForProducer` wiring). This is the highest-risk area.
- *Spencer-only (Editor):* ragdoll feel for the new sourceless (fall/hazard) knockback direction; AOE "feels" like it hits the right radius.

## Open decisions (collected) — RESOLVED
- [x] §2 — `DamageBusSystem` placement → **`GameManagerSystemGroup` OrderFirst**.
- [x] §4 — rename type **and** fields (`attackType→damageSource`, `[FormerlySerializedAs("attackType")]` on `AttackSO`; other-named fields kept: `attack`/`weaponAttack`/`defaultAttack`; `killAttackType→killDamageSource`).
- [x] §5 — `HazardZoneSystem` group → **`CombatExecutionSystemGroup`** (`[UpdateBefore(AttackRequestSystem)]`, before `DamageResolutionSystem`); no new group added.
- [x] §5 — hazard cooldown → **single whole-zone `retriggerInterval` gate** (`lastTriggerTime` stamp).
- [x] §5 — Cone/Line/Chain → **fall back to single-target** (kept in the enum).
- [x] §7 — fall/hazard ragdoll direction → **`hitSourceX` = hazard/impact X** (tip away from the hazard).
- [x] *Build deviation* — dropped the producer-written `aoeCount` `NativeReference` (parallel increment = race); `DamageResolutionSystem` detects AOE by scanning the drained `raw` array on the main thread.
