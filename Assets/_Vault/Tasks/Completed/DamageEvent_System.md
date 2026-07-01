# DamageEvent System — Attack/Damage Refactor to Signal-Entity Model

> **Status:** ✔️ built & compiled (2026-07-01) · all 4 build phases landed, `Hurt` buffer removed. In-Editor play verification pending — see [`../Verification/verify-damage-event.md`](../Verification/verify-damage-event.md).
> **Raw source:** [`../Plans/futureneedsplan.md`](futureneedsplan.md) → combat/damage decoupling (no dedicated braindump anchor — Spencer-requested refactor).

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `dots-system-scaffold` — new `DamageEventSystem` (consumer) and the `CombatEventCommandBufferSystem`; rewriting the `AttackRequestJob` to spawn events + `ScheduleParallel` (§5).
- `dots-authoring-baker` — minor: remove the `Hurt` buffer add from `HealthAuthoring` (§7/§8).

---

## Context — why this change

The combat damage path is entered through a per-unit `Hurt` **buffer**, and the two systems that drain it (`ThreatUpdateSystem`, `DamageApplicationSystem`) iterate **every unit, every frame**, just to early-out on `if (hurtBuffer.Length == 0) return;`. Worse, the producer (`AttackRequestSystem`) is pinned **single-threaded** specifically because multiple attackers may write into the same victim's `Hurt` buffer (see its own line-44 comment).

This refactor moves attacks onto the project's **one-frame signal-entity pattern** (the `LoggingSystem` / `PlaySound` model): when a hit lands, spawn a small `DamageEvent` entity carrying everything needed to apply it. A single consumer reads all events, applies damage + updates threat + handles death, then `DestroyEntity(query)`. Outcome:

- Combat reaction becomes **O(hits-that-happened)** instead of O(all units).
- `AttackRequestSystem` becomes **`ScheduleParallel`** (each attacker spawns its own event entity → no shared-write hazard).
- The per-unit `Hurt` buffer disappears entirely, and `ThreatUpdateSystem` + `DamageApplicationSystem` collapse into one `DamageEventSystem`.
- All damage sources (melee **and** thrown items) flow through one unified path.

**Locked decisions (Q&A):** keep the per-attacker swing timer, spawn only the *hit* · fold threat+damage into one consumer · event is AOE-ready but resolves SingleTarget only in v1 · one-way (no attacker feedback) · **same-frame** via a dedicated combat ECB · migrate thrown items too.

## 1. Purpose & v1 scope

Replace the `Hurt`-buffer damage path with a one-frame `DamageEvent` signal entity. A hit spawns one `DamageEvent`; `DamageEventSystem` reads all of them, applies damage to `Health`, updates the victim's `ThreatEntry` buffer, captures killing-blow knockback, enables `Dead`, then destroys every event entity that frame.

**v1 handles:**
- Melee hits (`AttackRequestSystem`) → spawn `DamageEvent`.
- Thrown-item hits (`ThrownItemHitSystem`) → spawn `DamageEvent` (`attackerEntity = Null`, `attackType = Throw`).
- One unified consumer: damage accumulation, threat update, death trigger, killing-blow capture for ragdoll.
- `DamageEvent` carries `DamageBehaviour` + source position + range so AOE/Cone/Line/Chain are addable later with **no schema change**.

**Out of v1:** multi-target resolution (only `DamageBehaviour.SinlgeTarget` is resolved — exact parity with today). Any attacker-side feedback/back-channel (kept one-way; `LoopUntil` already reads the target's `Dead`/range directly).

## 2. Architecture

Producers (parallel jobs) record `DamageEvent` entities into a **dedicated combat ECB** that plays back between attack execution and damage reaction, so damage still lands **same frame** (no regression vs today's direct buffer write). One consumer drains them.

```
CombatExecutionSystemGroup
  AttackRequestSystem  ── hit lands ─┐ (ScheduleParallel, ParallelWriter)
                                     │  spawn DamageEvent { target, attacker, dmg, knockback… }
ItemSystemGroup/ThrownItemSystemGroup
  ThrownItemHitSystem  ── hit lands ─┤  (also records into the same ECB)
                                     ▼
[CombatEventCommandBufferSystem]  ← plays back the ECB (events now exist)
                                     ▼
CombatReactionSystemGroup
  DamageEventSystem    ── read ALL DamageEvent entities (Burst main-thread loop, small N):
                            • accumulate damage → Health (skip already-Dead)
                            • update ThreatEntry (skip when attacker == Null)
                            • capture kill* on the lethal event, enable Dead
                          then EntityManager.DestroyEntity(damageEventQuery)
                                     ▼
HealthSystemGroup → DeathSystem → Ragdoll2DInitSystem  (unchanged; reads Health.kill*)
```

**← DECISION (naming):** `DamageEvent` / `DamageEventSystem` / `CombatEventCommandBufferSystem`. Swap if you prefer `HitEvent` / `AttackHit`.

## 3. Entry points

This system is entered by the **one-frame signal-entity pattern** (LoggingSystem model), not by a component on the acted-upon entity:

- **Producer side (unchanged trigger):** the per-attacker `AttackRequest : IComponentData, IEnableableComponent` (`AttackComponents.cs`) stays exactly as-is — it is the swing-windup timer (`elapsed` vs `attackBlob.hitTime`, `hitFired` guard). Only the *hit action* changes: instead of `hurtBufferLookup[victim].Add(...)`, the job spawns a `DamageEvent` entity.
- **Signal:** `DamageEvent : IComponentData` — a transient entity created via ECB, read by `DamageEventSystem`, destroyed same frame (`DestroyEntity(query)`). Not enableable, not persistent.

## 4. Data model

No new SO→Blob library — config still comes from the existing `AttackLibrary` blob (`AttackBlob` per `AttackType`, baked by `AttackLibraryBakingSystem`). `DamageEvent` is a pure runtime component (a one-frame value carrier; this is the sanctioned "runtime context data is the exception" case to the enum→blob rule — see [[Data Blob Pointer Pattern]]).

```csharp
public struct DamageEvent : IComponentData
{
    public Entity         targetEntity;     // v1: pre-resolved primary victim (SinlgeTarget)
    public Entity         attackerEntity;   // Null for thrown items / environmental
    public AttackType     attackType;
    public int            damageAmount;
    public float          distance;         // for logging/effects (carried from Hurt)
    // Death-only knockback (captured into Health.kill* on the lethal event) — see [[project_combat_knockback]]
    public float          hitSourceX;       // attacker world-X → ragdoll fall direction
    public float          ragdollForce;
    public float          launchForceY;
    public float          launchForceX;
    // AOE-ready carry-along (unused by v1 SinlgeTarget resolution):
    public DamageBehaviour damageBehaviour;
    public float3          sourcePosition;   // attacker position — future spatial query origin
    public float           range;            // attackBlob.range — future AOE radius
}
```

These are exactly today's `Hurt` fields **+ `targetEntity` + the three AOE-ready fields**.
`← DECISION (file location):` new `Assets/_Scripts/Components/Combat/DamageEvent.cs`, or append to `AttackComponents.cs`.
`← DECISION (AOE fields):` include `damageBehaviour`/`sourcePosition`/`range` now (recommended — keeps schema frozen), or add them only when AOE is built.

## 5. Systems

**New — `CombatEventCommandBufferSystem`** (`Assets/_Scripts/Systems/CombatSystemGroup/`)
- `public partial class CombatEventCommandBufferSystem : EntityCommandBufferSystem`.
- `[UpdateInGroup(typeof(CombatSystemGroup))] [UpdateAfter(typeof(CombatExecutionSystemGroup))] [UpdateBefore(typeof(CombatReactionSystemGroup))]`.
- Provides the `Singleton` that both producers record into; plays back so events exist before `DamageEventSystem`. (Thrown items in `ItemSystemGroup` run earlier in the frame but record into the same ECB — fine; playback is at this system.)

**New — `DamageEventSystem`** (`CombatReactionSystemGroup`) — *replaces* `ThreatUpdateSystem` + `DamageApplicationSystem`
- `[BurstCompile] ISystem`, `RequireForUpdate<GameSceneTag>`. Early-out if the `DamageEvent` query is empty.
- Burst **main-thread** loop over `SystemAPI.Query<RefRO<DamageEvent>>()` (small N — the LoggingSystem precedent; lookups write arbitrary targets so this stays single-threaded by nature). Uses `ComponentLookup<Health>` (RW), `BufferLookup<ThreatEntry>` (RW), `ComponentLookup<Dead>` (RW).
- Per event (resolve `targetEntity` only in v1):
  - Skip if target already `Dead`-enabled (avoid double-kill).
  - **Threat:** if `attackerEntity != Entity.Null`, find/add `ThreatEntry` (`threatScore += damageAmount`, `staleTimer = 4f`, new entries `reactionDelay = 0.3f`) — same constants as `ThreatUpdateSystem` (`REACTION_TIME`/`THREAT_TTL`).
  - **Damage:** `health.healthAmount -= damageAmount`. When this crosses `<= 0` and `Dead` not yet enabled, capture `kill*` (`killSourceX/RagdollForce/LaunchForceY/LaunchForceX/AttackType`) from this event and enable `Dead`.
- After the loop: `state.EntityManager.DestroyEntity(damageEventQuery)` (LogMessage pattern).

**Edited — `AttackRequestSystem`** (`CombatExecutionSystemGroup`)
- Drop `BufferLookup<Hurt>`. Keep `transformLookup` + `deadLookup` + the windup timer / range / alive checks (range/alive stay producer-side — recommended, avoids spawning events for whiffs).
- On hit: `ecb.CreateEntity(sortKey)` + `ecb.AddComponent(sortKey, e, new DamageEvent{…})` via `CombatEventCommandBufferSystem.Singleton.CreateCommandBuffer(...).AsParallelWriter()`.
- **`.Schedule()` → `.ScheduleParallel()`** — each attacker writes its own entity, so the shared-buffer hazard that forced single-threaded (its line-44 comment) is gone.
- `← DECISION:` range/alive validation stays in the producer (recommended) vs. moved into `DamageEventSystem`.

**Edited — `ThrownItemHitSystem`** (`ThrownItemSystemGroup`)
- Replace `BufferLookup<Hurt>` (`hurtBufferLookup.Add(...)`) with recording a `DamageEvent` into `CombatEventCommandBufferSystem`. Its "hittable target" test changes from *"has a `Hurt` buffer"* to *"has `Health`"* (since the buffer is being deleted) — swap the `NativeList<TargetData>` collection filter accordingly.

**Deleted — `ThreatUpdateSystem.cs`, `DamageApplicationSystem.cs`** (logic folded into `DamageEventSystem`; preserve their summaries in [[Systems_AI]]/[[Systems]] per the docs convention).

No `SystemGroups.cs` edit needed — the ECB system's placement is set by its own `[UpdateInGroup]/[UpdateBefore/After]` attributes. `SelfDefenceAwarenessSystem` and `ThreatDecaySystem` are untouched (they consume `ThreatEntry`, which `DamageEventSystem` still populates identically).

## 7. Integration points

- **Health / Ragdoll:** `Health.kill*` fields stay; `DeathSystem` and `Ragdoll2DInitSystem` are unchanged — they read `Health`, never `Hurt`. Death-only knockback semantics preserved ([[project_combat_knockback]]).
- **AI fight-back:** `ThreatEntry` is still produced with identical constants, so `SelfDefenceAwarenessSystem` (0.3s flinch, priority-3 fight-back) and `ThreatDecaySystem` keep working with no change ([[project_bravery_system]]).
- **`HealthAuthoring.cs`:** remove the `AddBuffer<Hurt>` call. **This is a baking change → the scene must be re-baked** before testing.
- **`ItemAwarenessSystem`:** no change — its "Hurt" reference is a comment; it checks `health < max`, not the buffer.
- **Logging:** keep the existing `LogCategory.Combat` hit/whiff/skip logs (move them into the producer at spawn time and/or the consumer).

## 8. Proposed file manifest

**New:**
- `Assets/_Scripts/Components/Combat/DamageEvent.cs` *(or append to `AttackComponents.cs` — ← DECISION)*
- `Assets/_Scripts/Systems/CombatSystemGroup/CombatEventCommandBufferSystem.cs`
- `Assets/_Scripts/Systems/CombatSystemGroup/CombatReactionSystemGroup/DamageEventSystem.cs`

**Edited:**
- `…/CombatExecutionSystemGroup/AttackRequestSystem.cs` — spawn `DamageEvent`, `ScheduleParallel`, drop `Hurt` lookup.
- `…/ItemSystemGroup/ThrownItemSystemGroup/ThrownItemHitSystem.cs` — spawn `DamageEvent`, filter targets by `Health`.
- `Assets/_Scripts/Components/Units/UnitComponents.cs` — delete the `Hurt` struct.
- `Assets/_Scripts/Authoring/Units/HealthAuthoring.cs` — remove `AddBuffer<Hurt>`.

**Deleted:**
- `…/CombatReactionSystemGroup/ThreatUpdateSystem.cs`
- `…/CombatReactionSystemGroup/DamageApplicationSystem.cs`

**Assets:** none (existing `AttackLibrary`/`AttackSO` unchanged). Re-bake `DOTSTestScene` after the `HealthAuthoring` edit.

## 9. Build phases

1. **Infra (compiles, no behavior change):** add `DamageEvent` component + `CombatEventCommandBufferSystem`. Nothing spawns/consumes yet.
2. **Consumer:** write `DamageEventSystem` (threat + damage + death + destroy). Still nothing spawns events — verify it no-ops on an empty query.
3. **Melee path live:** rewrite `AttackRequestJob` to spawn `DamageEvent` + `ScheduleParallel`; delete `DamageApplicationSystem` + `ThreatUpdateSystem`. Melee combat now flows end-to-end through events. (`Hurt` buffer still present, used only by thrown items.)
4. **Unify + delete buffer:** migrate `ThrownItemHitSystem`; delete the `Hurt` struct + the `HealthAuthoring` buffer add. Re-bake.
5. **Verify** (below).

## 10. Verification

Play `DOTSTestScene` (re-bake first — `HealthAuthoring` changed):
- **Phase 3 — melee parity:** two combatants fight; confirm damage ticks, health reaches 0, unit dies, ragdoll launches in the correct direction with the same knockback feel as before. Confirm a damaged citizen still fights back / flees (threat-driven `SelfDefenceAwarenessSystem` fires after the 0.3s flinch).
- **Phase 4 — thrown parity:** throw a Rock at a unit; confirm it damages and can kill (ragdoll, `attackerEntity = Null` so no threat entry added — matches old behavior).
- **Decoupling proof:** in the Entities inspector, confirm `DamageEvent` entities appear only on hit frames and vanish the same frame (none leak). In the profiler, confirm `DamageEventSystem` only does work on hit frames (no per-unit scan), and `AttackRequestSystem` now runs as a parallel job.
- **No-Hurt proof:** confirm no `Hurt` buffer remains on baked units and the project compiles with the struct deleted.
- *Spencer-only (Editor):* the ragdoll "feel" and combat responsiveness (same-frame timing) are visual — confirm in-editor they match the pre-refactor build.

## Open decisions (collected)
- [ ] §2 — naming: `DamageEvent` / `DamageEventSystem` / `CombatEventCommandBufferSystem` vs `HitEvent`/`AttackHit`.
- [ ] §4 — `DamageEvent` file: new `Components/Combat/DamageEvent.cs` vs append to `AttackComponents.cs`.
- [ ] §4 — include AOE carry-along fields (`damageBehaviour`/`sourcePosition`/`range`) now (recommended) vs when AOE is built.
- [ ] §5 — range/alive validation stays producer-side (recommended) vs moved into `DamageEventSystem`.
