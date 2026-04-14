---
tags: [memory, code, systems, ai, motivation]
related: "[[Systems]], [[Components]], [[Systems_Movement]]"
---

# AISystemGroup — Context

The AI system is **unified behavior scoring**. Every unit has a `Brain` component (a `BrainType` enum) that points to a config entry in `BrainLibraryBlob`. That config defines which motivations decay, which behaviors score action options, and what weights apply. One system per behavior type runs for ALL brain types — a weight of 0 simply skips the entity.

No separate brain entity. No body entity. All AI state lives on the single unit entity.

---

## Architecture

### Single Entity per Unit
- Citizens, enemies, guards, minions — all single entities
- Brain type is `Brain.activeBrain` (BrainType enum)
- No `BodyLink`, `BrainLink` — **fully removed**. All AI state (Brain, motivations, ActionOptions, PlayerControlled, NeedsAction) is on the unit entity directly.

### Brain Config in Blob
- `BrainLibraryBlob` keyed by `(int)BrainType` → `BrainConfigBlob`
- Each `BrainConfigBlob` has: motivation decay rates / initial values / urgency thresholds + behavior configs (weight=0 = inactive)
- Baked from `BrainLibrarySO` (list of `BrainConfigSO` assets) by `BrainLibraryBakingSystem`

### Brain Swap
- Enable `SwapBrainRequest.newBrain` on any unit entity
- `SwapBrainSystem` changes `Brain.activeBrain`, resets `MotivationState` from blob, clears `ActionOption` buffer
- No entity destruction or instantiation

---

## Pipeline (Phase 2 — in progress)

```
AIAwarenessSystemGroup
  MotivationDegradationSystem  — per-motivation component decay (HungerMotivation etc.), [WithAll(ActiveBrain)]
  SpatialHashSystem            — spatial index for interaction discovery
  FactionRegistrySystem        — NativeParallelMultiHashMap<byte,Entity> rebuilt each frame

AIScoringSystemGroup           — all systems write to ActionOption buffer on the entity
  HungerScoringSystem          — spatial hash → HungerInteraction entities → score vs HungerMotivation
  EnergyScoringSystem          — (and 6 more motivation-interaction scoring systems...)
  BehaviourScoringSystem       — scores Behaviour buffer entries:
                                   Wander   → flat score 10f, ActionCategory.Wander
                                   Chase    → FactionRegistry lookup, ActionCategory.Chase
                                   MeleeAttack → CombatTarget in range, ActionCategory.Attack
                                   Flee     → ThreatEntry buffer, ActionCategory.Flee

AISelectionSystemGroup
  ActionSelectionSystem        — top-3 random pick; Interaction category → checks occupancy,
                                 other categories → selects directly; writes SelectedAction
  InteractionAssignmentSystem  — assigns winners to interaction, calls AIUtils.AssignWinners

AIExecutionSystemGroup
  InteractionExecutionSystem   — navigate to waypoint, trigger interaction
  AttackExecutionSystem        — sets AttackData.attackType (from blob key), Target.entity, enables Attack
                                  → AttackResolutionSystem handles damage + cooldown via AttackLibraryBlob
  FleeExecutionSystem          — PathRequest away from threat source
  WanderExecutionSystem        — idle/wander when no urgent options
```

---

## ActionOption Buffer

```csharp
public struct ActionOption : IBufferElementData
{
    public float score;
    public ActionCategory category;   // Interaction, Wander, Chase, Attack, Flee, ...
    public Entity targetEntity;       // interaction entity OR hostile entity
    public float3 targetPosition;     // flee destination or chase target pos
}
```

All scoring systems write to this buffer. `ActionSelectionSystem` trims to top 3 and writes `SelectedAction`.
Interaction scoring systems call `AIUtils.AddActionOption(ref options, ref entity, score)` — sets `category = Interaction` automatically.
Behaviour scoring systems call `AIUtils.AddActionOption(ref options, score, category, targetEntity, targetPosition)` for full control.

---

## MotivationState

Single component, 9 named float fields (0–100 range):
`hunger, energy, fun, social, comfort, bladder, safety, movement, selfPreservation`

Decay rates, initial values, and urgency thresholds per brain type live in `BrainLibraryBlob`.
`MotivationDecaySystem` ticks values down by `decayRate * deltaTime`, clamped to [0, 100].

---

## Brain Types

| BrainType | Motivations | Behaviors |
|---|---|---|
| Citizen | All 9, normal decay | Interaction (high), Flee (low) |
| Guard | Safety, SelfPreservation | Chase Undead, Attack, Flee (low) |
| FeralZombie | None (static) | Chase Player+Human, Attack |
| PlayerZombie | None | PlayerControlled enabled → scoring bypassed |
| Panic | SelfPreservation (fast decay) | Flee (very high) |
| Merchant | Social, Work | Interaction |
| Character | Per narrative | Via narrative system |

---

## Adding a New Brain Type

1. Add a value to `BrainType` enum.
2. Create `BrainConfigSO` asset (`AI/Brains/`) — set brainType, configure motivations and behaviors inline.
3. Add to the `BrainLibrarySO` asset's brains list.
4. Done — no new systems, no new components, no new authoring.

---

## Player-Controlled Minions

- `PlayerControlled` is baked (disabled) on ALL unit entities via `BrainAuthoring`
- `ActionSelectionSystem` uses `[WithDisabled(typeof(PlayerControlled))]` — minions are skipped
- When `PlayerControlled` is enabled: `MinionCommandSystem` writes `SelectedAction` directly
- `SwapBrainSystem` enables `PlayerControlled` when brain swaps to `BrainType.PlayerZombie`

---

## Key Files

| File | Path | Role |
|---|---|---|
| `BrainAuthoring.cs` | `Authoring/AI/Brains/` | Per-unit brain setup — replaces CitizenBrainAuthoring / ZombieBrainAuthoring |
| `BrainLibraryAuthoring.cs` | `Authoring/EntityLibraries/` | Singleton — references BrainLibrarySO |
| `BrainLibraryBakingSystem.cs` | `Systems/PostBakingSystemGroup/` | Builds BrainLibraryBlob from BrainLibrarySO |
| `SwapBrainSystem.cs` | `Systems/HealthSystemGroup/` | Brain swap — cheap value change, no entity ops |
| `BrainConfigSO.cs` | `Data/SOs/` | One per brain type — inline motivation + behavior config |
| `BrainLibrarySO.cs` | `Data/SOs/` | List of all BrainConfigSOs |
| `BrainBlobs.cs` | `Data/Structs/` | BrainLibraryBlob, BrainConfigBlob, MotivationValues9 |
| `Brains.cs` | `Components/AI/` | Brain, MotivationState, BrainType, SwapBrainRequest |
| `AIComponents.cs` | `Components/AI/` | ActionCategory, ActionOption, SelectedAction, NeedsAction, PlayerControlled |
