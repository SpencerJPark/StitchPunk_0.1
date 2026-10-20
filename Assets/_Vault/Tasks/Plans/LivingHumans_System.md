# Living Humans — Design Spec (LH)

> **Status:** 🟡 specced 2026-10-20, not built. Owner product calls are LH-D1–LH-D4. Architecture calls LH-D5–LH-D18 were made under the delegated-architecture rule. All are settled; do not re-ask.
> **Read first:** `Memories/Code/Systems_AI.md` (whole), `Memories/Code/RULES.md`, `Memories/Code/Gotchas.md`, `Plans/SmartHumanAI_System.md` §9 (SH-D1–D9), `Verification/AIFoundations_System.md` §2 (AF-D1: tiers are the only way to preempt).
> **Relation to SmartHumanAI:** this is flavour that sits beside H4 (tactics), not a replacement for it. It touches different files except `ItemAwarenessSystem.cs` (LH-T3.2). If H4 is in flight, run T3.2 after it.

---

## 1. Purpose & scope

The owner's brief (2026-10-20): citizens and bandits should feel like *different people* to someone watching. The effect should be subtle and cheap:
- they talk to each other, with emotes over their heads;
- some are braver or more resourceful in a fight;
- some citizens are drawn to drink;
- it scales down as the on-screen crowd grows;
- it never gets in the way of the gameplay.

**In scope:**
- a personality rolled per unit at runtime;
- conversations that go back and forth, with personality-driven emotes;
- a pooled emote renderer with a crowd budget;
- vice and alcohol with full drunk behaviour (stumbling, non-lethal brawls, passing out);
- personality-driven fleeing and weapon grabbing.

**Out of scope:**
- relationship or grudge memory (LH-D4);
- zombies (LH-D3);
- new animations (no unit has toolkit animation data, see §8);
- sound for emotes;
- any UI Toolkit / Rive overlay.

## 2. What exists (verified 2026-10-20 — re-verify before trusting)

- **`PersonalityAttributes`** (`Components/AI/UtilityAiComponents.cs:62`) is a buffer of `{ personality, value }`. It is read generically by `ConsiderationScoringSystem.cs:149` (a trait consideration on any `UtilityActionSO`) and by `FleeAwarenessSystem` (Bravery gate).
- **Bug this spec fixes:** `UnitBakingUtil.cs:95-115` rolls Bravery/Friendly with `System.Random` **inside the baker**. That causes two problems:
  - every unit instantiated from one baked prefab has *identical* "random" values;
  - values reroll on every rebake, so baking is not deterministic.

  It also computes `wanderlustBase` and `gluttonyBase` and never uses them.
- **`PersonalityTypes`** (`Data/Enums/AiEnums.cs:167`): Bravery, Romance, Comedy, Friendly, Anger, Partier, Rest.
- **Talk pipeline:**
  - `SocialAwarenessSystem` emits the Talk option;
  - the `TalkBehaviour` asset runs `RequestSocialResponse`;
  - `SocialResponseSystem` accepts on the partner by putting both into `ActionType.Talk` targeting each other.
- **Friendly fire never creates a `ThreatEntry`** (`DamageEventSystem.cs:99`). A same-faction hit therefore cannot start self-defence, alarm or leash behaviour. This is what makes drunk brawls safe.
- **Camera in ECS:** `AnimationToolkitCameraData { position, forward }` singleton (`VisualBillboardSystem` uses it).
- **Entities Graphics** (`com.unity.entities.graphics@66588da3a112`):
  - `MaterialMeshInfo.FromRenderMeshArrayIndices` and a settable `MaterialMeshInfo.Material`;
  - the stock `URPMaterialPropertyBaseColor { float4 Value }` per-instance colour, which works with URP's own Unlit shader, so no custom shader is needed.
- **Assets:**
  - brains `HumanBaseBrain` → `CitizenBrain` / `BanditBrain`;
  - modules in `ScriptableObjects/AI/Modules/` (`TalkModule`, `FleeModule`, `EquipWeaponModule`, …);
  - interactions `Chair`, `Gramophone`, `SupplyCrate`, `Bathroom`.

## 3. Decisions

### Owner (2026-10-20)
- **LH-D1 Emote art:** placeholders now, swappable later. A generated icon atlas; replacing the PNG changes nothing else.
- **LH-D2 Alcohol:** **full drunk behaviour**: cravings, tipsy, stumbling, picking fights, passing out. It must still never take away from gameplay (LH-D12, LH-D13).
- **LH-D3 Who:** **citizens and bandits**. Bandits lean brave/resourceful, citizens lean social/vice. Zombies untouched.
- **LH-D4 Conversation:** **back-and-forth**. Speakers alternate, and each beat's emote comes from both personalities. It can turn into an argument. No relationship memory.

### Architecture (delegated, 2026-10-20)
- **LH-D5 Personality is rolled at runtime.** The baker writes only `baseValue` + `variance` and enables `PersonalityRollPending`. `PersonalityRollSystem` rolls `value = saturate(baseValue + rand(-1,1) × variance)` with `Unity.Mathematics.Random`. The seed is a hash of `entity.Index`, `entity.Version` and a per-session seed in the `PersonalitySeed` singleton (set once in `OnCreate` from `System.Environment.TickCount`). Pool reuse re-enables `PersonalityRollPending`, so a recycled body is a new person. `System.Random` leaves the baker entirely.
- **LH-D6 Six traits, all on 0..1:**

  | Trait | Enum | What it drives |
  |---|---|---|
  | Bravery | `Bravery` (exists) | Flee threshold at low HP (existing gate). Bandits get a wide variance so a few run when almost dead. |
  | Sociability | `Friendly` (exists) | Talk option score; warmer emotes |
  | Temper | `Anger` (exists) | Arguments; brawl eligibility when drunk |
  | Vice | `Partier` (exists) | `NeedType.Vice` decay rate (0 vice = never craves) |
  | Humour | `Comedy` (exists) | Laugh emotes in conversation |
  | Resourcefulness | `Resourceful` (**append**) | EquipWeapon score; how far it will detour for a weapon |

  `Romance` and `Rest` stay unused.
- **LH-D7 Entry patterns:**
  - `PersonalityRollPending` is an enableable request.
  - `EmoteState` is a plain component that only its own entity's systems write. A new emote replaces the current one only if `priority >= current.priority` or the current one has expired (`EmoteRules.TryShow`).
  - `Conversation` and `Intoxication` are enableable state components.
  - `CrowdLifeLod` is a singleton.
- **LH-D8 Groups:**
  - New **`CrowdLifeSystemGroup`** in `SimulationSystemGroup`, `[UpdateAfter(StateMachineSystemGroup)] [UpdateBefore(ItemSystemGroup)]`. Declared in `SystemGroups.cs`; files go in `Systems/CrowdLifeSystemGroup/`.
  - `EmotePresentationSystem` goes in the existing `PresentationSystemGroup`.
  - `DrunkStumbleSystem` goes in the movement toolkit's `MovementSystemGroup`, after `MovementFollowerSystemGroup` and before `MovementExecutionSystemGroup` (verify these names in `Packages/com.dotsmovementtoolkit` before writing), in `Systems/MovementSystemGroup/`.
  - `DrunkAwarenessSystem` goes in `UtilityAwarenessSystemGroup`.
- **LH-D9 Emote renderer = a fixed pool of quad entities, no GameObjects, no UI.**
  - `EmotePoolAuthoring` bakes `poolSize` (24) quad entities from one `RenderMeshArray`: one quad mesh plus one material per `EmoteIcon`. All materials use stock `Universal Render Pipeline/Unlit`, transparent, sharing the atlas texture with a per-material `_BaseMap_ST` cell offset.
  - Switching icon = setting `MaterialMeshInfo` to that icon's material index.
  - Fade and hide = `URPMaterialPropertyBaseColor.Value.w`. Hidden slots get alpha 0 and scale 0.
  - Each frame `EmotePresentationSystem`:
    1. gathers units with a live `EmoteState`;
    2. keeps those within `emoteViewDistance` (35 m) and in front of the camera (`dot(toUnit, camForward) > 0.2`);
    3. ranks them by `priority` then distance;
    4. fills the first `budget` slots: above the head (`headHeight` 2.1 m), camera-facing like `VisualBillboardSystem`, popping in over 0.15 s and fading out over the last 0.3 s.
- **LH-D10 Crowd budget.** `visibleHumans` = humans in view range this frame, written to `CrowdLifeLod` by the presentation system.
  - `budget = visibleHumans <= 12 ? poolSize : max(6, poolSize − (visibleHumans − 12) / 2)`.
  - `beatIntervalScale = 1 + max(0, visibleHumans − 12) / 24`. Bigger crowds get fewer emotes per unit and slower beats.
  - Units off screen still make decisions (cheap) but get no emotes.
  - The math lives in a pure static `CrowdLifeBudget` class so it can be tested.
- **LH-D11 Conversation is deterministic on both sides without cross-writes.**
  - When a unit is in `Talk` and its partner is in `Talk` targeting it back, `ConversationSystem` enables `Conversation` with `{ partner, startTime, pairSeed }`. `startTime` and `pairSeed` come from the *lower-index* entity of the pair, so both sides compute the same values.
  - Beat `k` spans `[startTime + k·beatLength, …)` with `beatLength = lerp(1.6, 2.8, hash01(pairSeed,k)) × beatIntervalScale`. The speaker is the lower-index entity on even beats and the partner on odd beats.
  - Each unit writes **only its own** `EmoteState`: a speaking emote on its own beats, an occasional reaction on the partner's beats.
  - **Mood** = `pairBase + Σ beatDrift`. `pairBase = 0.6·(sociabilityA·sociabilityB) − 0.5·max(temperA, temperB) + 0.2·(humourA+humourB)/2`, and `beatDrift` is ±0.15 from `hash01(pairSeed,k)`. Both sides get identical results.
  - Emote pick per beat (pure `ConversationEmoteRules.PickEmote(mood, speakerTraits, intoxication, hash)`):
    - mood > 0.5 → Heart/Laugh (Laugh weighted by Humour) or Happy;
    - −0.2..0.5 → Happy/Question/Idea/Exclaim;
    - < −0.2 → Angry/Sad;
    - intoxicated > 0.3 adds Drunk.
  - **Argument:** mood < −0.6 and `max(temper) > 0.6`. Both show Angry at priority 3 and the conversation ends: each unit enables its own `ActionInterruptRequest` (verify its semantics in `BehaviorInterruptSystem` first). If either is drunk > 0.5, `DrunkAwareness` may turn the next think into a Brawl against the partner (LH-D13).
- **LH-D12 Vice and drinking.**
  - `NeedType.Vice` (append). `PersonalityRollSystem` scales the unit's Vice `Motivation.decayRate` by `vice²`, so low vice never craves and high vice craves every few minutes.
  - New `ActionType.DrinkAlcohol` + `DrinkAlcoholAction` (tier `Ambient`, scores on Vice) + `DrinkAlcoholBehaviour`: `Approach 1.2` → `ShowEmote Drink` → `WaitTime 4` → `ModifyMotivation Vice +70` → `AddIntoxication 0.3` → `ReleaseInteraction 45`.
  - A **`Bar`** interaction asset (maxUsers 3) is placed once in `CampLoop_Sub` near the campfire.
- **LH-D13 Drunk tiers.** `Intoxication.level` (0..1) decays at 0.015/s, so it is sober again ~60 s after one drink.
  - `> 0.3`, **tipsy:** a Drunk emote every ~6 s (priority 1); `DrunkStumbleSystem` scales `moveSpeed` by `1 − 0.35·level` and adds a lateral sine wobble (amplitude `0.6·level` m, ~0.7 Hz, phase from entity index) to `Movement.targetPosition` after path following.
  - `> 0.5` **and Temper > 0.6**, **may brawl:**
    - `DrunkAwareness` offers `Brawl` at tier `Ambient` + its top score against the nearest **same-faction** human within 4 m, or the argument partner.
    - Never against the player, minions, or a unit in combat.
    - At most one brawl per unit per 60 s (`Intoxication.brawlCooldown`).
    - `BrawlBehaviour`: `Approach 1.2` → `ShowEmote Angry` → `LoopUntil` (timeout 8 s): `PlayActionAnimation` → `RequestAttack`.
  - `≥ 0.9`, **passes out:** `DrunkAwareness` offers `PassOut` at tier `Critical`, so it beats Wander/Talk/Ambient. `PassOutBehaviour`: `ShowEmote Sleep` (duration 15) → `WaitTime 15` → `AddIntoxication −0.5`. The unit stands still with a zzz until an animation profile exists (§8).
- **LH-D14 Brawls are non-lethal by construction.** In `DamageEventSystem`, when source and target are the same faction **and** the source's `StateMachine.action == ActionType.Brawl`, the hit cannot take the target below `35%` of max HP. It is clamped, and lethal handling never runs. This is one branch, and it covers rocks held mid-brawl too. Because there is no `ThreatEntry` (friendly fire), no guard or ally joins in.
- **LH-D15 Personality → fighting:**
  - Bandits: `BanditBrain` must include `FleeModule` (add it if missing). `Bandit.asset` gets `bravery 0.75, braveryVariance 0.35`, so roughly 1 in 6 bandits flees near death.
  - `EquipWeaponAction` gets a `Resourceful` trait consideration.
  - `ItemAwarenessSystem`'s mid-fight detour (6 m) becomes `6 × lerp(0.5, 1.5, resourcefulness)`.
- **LH-D16 Unit tuning:** `MaleCitizen`: sociability 0.65±0.3, vice 0.35±0.35, temper 0.3±0.3, humour 0.5±0.4, resourcefulness 0.3±0.25, bravery as today. `Bandit`: sociability 0.45±0.3, vice 0.4±0.3, temper 0.55±0.3, humour 0.35±0.3, resourcefulness 0.65±0.3. A new `UnitSO.hasLivingPersonality` (true for these two) gates every component added in §4. Zombies and the player get none of them.
- **LH-D17 Placeholder art** is generated by an Editor menu, `Tools/Stitch Punk/Crowd Life/Generate Placeholder Emotes`. It draws a 4×4 atlas (256 px cells) of simple coloured shapes plus marks into `Assets/Art/CrowdLife/EmoteAtlas.png`, then writes the 14 materials and a quad mesh beside it. Running it again overwrites the PNG only if `overwriteArt` is ticked, so the owner's replacement art is safe.
- **LH-D18 Performance budget:** all of `CrowdLifeSystemGroup` + `EmotePresentationSystem` + `DrunkStumbleSystem` + `DrunkAwarenessSystem` together must be **≤ 0.3 ms main thread at 30 humans** in the CampLoop fight capture (measure with the `Performance.md` method). Every job is Burst-compiled and scheduled with `ScheduleParallel`, except presentation ranking (one `IJob`; N ≤ a few hundred). Nothing allocates per frame beyond `Allocator.TempJob` lists.

## 4. Data model (exact names — workers must use these)

```csharp
// Data/Enums/AiEnums.cs — APPEND only, never reorder
PersonalityTypes: + Resourceful
NeedType:         + Vice
ActionType:       + DrinkAlcohol, Brawl, PassOut
BehaviorCommandType: + AddIntoxication   // FloatParam = delta (clamped 0..1)
                     + ShowEmote         // IntParam = (int)EmoteIcon, FloatParam = seconds (0 = 2.5), Duration unused; priority 2

// Data/Enums/EmoteEnums.cs — NEW
public enum EmoteIcon : byte { None, Happy, Laugh, Heart, Angry, Sad, Question, Exclaim, Idea, Sweat, Music, Sleep, Drunk, Drink }  // 13 icons + None; atlas cell = (int)icon - 1

// Components/AI/UtilityAiComponents.cs — CHANGE
public struct PersonalityAttributes : IBufferElementData { public PersonalityTypes personality; public float value; public float baseValue; public float variance; }
public struct PersonalityRollPending : IComponentData, IEnableableComponent { }

// Components/AI/CrowdLifeComponents.cs — NEW
public struct EmoteState : IComponentData { public EmoteIcon icon; public float remaining; public float duration; public byte priority; }
public struct Conversation : IComponentData, IEnableableComponent { public Entity partner; public double startTime; public uint pairSeed; public int lastBeatHandled; }
public struct Intoxication : IComponentData, IEnableableComponent { public float level; public float baseMoveSpeed; public float brawlCooldown; public float nextDrunkEmoteIn; }
public struct CrowdLifeLod : IComponentData { public int visibleHumans; public int emoteBudget; public float beatIntervalScale; }
public struct PersonalitySeed : IComponentData { public uint value; }
public struct EmotePoolSlot : IComponentData { public int slotIndex; public float shownFor; public Entity owner; }
public struct EmotePoolConfig : IComponentData { public int poolSize; public float viewDistance; public float headHeight; public int firstIconMaterialIndex; }

// Utils/CrowdLife/ — NEW pure static, Burst-compatible, testable
EmoteRules.TryShow(ref EmoteState state, EmoteIcon icon, float seconds, byte priority) -> bool
ConversationEmoteRules.PairMood(in traitsA, in traitsB) / BeatMood(...) / PickEmote(...) / IsArgument(...)
CrowdLifeBudget.Budget(int visibleHumans, int poolSize) / BeatIntervalScale(int visibleHumans)
PersonalityTraitLookup.Get(in DynamicBuffer<PersonalityAttributes>, PersonalityTypes) -> float (0.5 if missing)

// Data/SOs/UnitSO.cs — ADD
bool hasLivingPersonality; float temper, temperVariance, vice, viceVariance, humour, humourVariance, resourcefulness, resourcefulnessVariance;
```

## 5. Systems

| System | Group / folder | Reads → writes |
|---|---|---|
| `PersonalityRollSystem` (new) | `SpawnInitSystemGroup` | enabled `PersonalityRollPending`, `PersonalitySeed` → `PersonalityAttributes.value`, Vice `Motivation.decayRate`; disables pending |
| `ConversationSystem` (new) | `CrowdLifeSystemGroup` | `StateMachine`, partner `StateMachine`/traits (lookup, RO), `CrowdLifeLod` → own `Conversation`, own `EmoteState`, own `ActionInterruptRequest` on argument |
| `IntoxicationSystem` (new) | `CrowdLifeSystemGroup` | enabled `Intoxication` → decay, drunk emote, `brawlCooldown`; disables at level 0 and restores `moveSpeed` |
| `EmoteDecaySystem` (new) | `CrowdLifeSystemGroup` (OrderLast) | `EmoteState.remaining -= dt`; icon → None at 0 |
| `DrunkStumbleSystem` (new) | toolkit `MovementSystemGroup`, after followers | `Intoxication` → `Movement.moveSpeed`, `Movement.targetPosition` wobble |
| `DrunkAwarenessSystem` (new) | `UtilityAwarenessSystemGroup` | `Intoxication`, traits, `Conversation`, faction map → `UtilityActions` (Brawl, PassOut) |
| `EmotePresentationSystem` (new) | `PresentationSystemGroup` | `EmoteState`, `LocalToWorld`, camera data → pool slot `LocalTransform`, `MaterialMeshInfo`, `URPMaterialPropertyBaseColor`; `CrowdLifeLod` |
| `BehaviorExecutionSystem` (edit) | existing | + `AddIntoxication` (enables `Intoxication`, stores `baseMoveSpeed` on first enable), + `ShowEmote` |
| `DamageEventSystem` (edit) | existing | + LH-D14 brawl floor |
| `ItemAwarenessSystem` (edit) | existing | + LH-D15 detour scaling |
| `UnitSpawnerSystem` (edit) | existing | re-enable `PersonalityRollPending` and reset `Intoxication`/`Conversation` on pool reuse |

## 6. Build plan: subagent waves

**Orchestrator rules** (root `CLAUDE.md` § Subagent Delegation, plus memory):
- Spawn only `worker` (edits) and `verifier` (checks), with `model` set explicitly: **Sonnet** for logic, **Haiku** for mechanical or YAML work.
- Each worker edits **at most two named files**. Paste its §4 names into the brief. Reading is limited to named line ranges.
- Every brief ends with: *"run `python Tools/devloop/devloop.py gate <assembly>` before your report; at turn 30 stop editing and report; report ≤ 30 lines."*
- **Subagents never use the Unity MCP.** The orchestrator owns `refresh_unity`, asset creation, scene edits, tests and Play mode.
- One gate per wave, run by the orchestrator, against the spec-pinned names. If the gate reports CS1061/CS1501 across assemblies, the Editor's DLLs are stale: `refresh_unity` and re-gate before blaming the code.
- A capped or rate-limited worker is never resumed. Diff its files and spawn a fresh worker with only what is left.
- Commit after each green wave, staging paths explicitly.

### Wave 0 — orchestrator, inline
- Read §2's files and confirm the append positions of each enum.
- Confirm the toolkit `MovementSystemGroup` sub-group names and the `ActionInterruptRequest` semantics, then write the answers into the W2 briefs.
- Run a baseline gate on `StitchPunk.Systems`.

### Wave 1 — data foundation *(3 workers, parallel)*
| Task | Model | Files | Brief essentials |
|---|---|---|---|
| T1.1 | Haiku | `Data/Enums/AiEnums.cs`, `Data/Enums/EmoteEnums.cs` (new) | §4 enum appends exactly, at the end of each enum |
| T1.2 | Sonnet | `Components/AI/UtilityAiComponents.cs`, `Components/AI/CrowdLifeComponents.cs` (new) | §4 structs exactly; field-order comments only where non-obvious |
| T1.3 | Sonnet | `Data/SOs/UnitSO.cs`, `Utils/UnitBakingUtil.cs` | Add the §4 UnitSO fields. Delete `System.Random` and the unused wanderlust/gluttony rolls. Write six `PersonalityAttributes` entries (`baseValue`, `variance`, `value = baseValue`). When `hasLivingPersonality`, add `PersonalityRollPending` (enabled), `EmoteState`, `Conversation` (disabled) and `Intoxication` (disabled), and append a `Vice` `Motivation`. |
**Gate:** orchestrator `refresh_unity`, then gate Data, Components, Utils.

### Wave 2 — core logic *(6 workers, parallel)*
| Task | Model | Files | Brief essentials |
|---|---|---|---|
| T2.1 | Sonnet | `Systems/SpawnInitSystemGroup/PersonalityRollSystem.cs` (new), `Systems/SpawnSystemGroup/UnitSpawnerSystem.cs` | LH-D5 roll + seed singleton; Vice decay `× vice²`; pool reuse re-enables pending |
| T2.2 | Sonnet | `Systems/SystemGroups.cs`, `Systems/CrowdLifeSystemGroup/EmoteDecaySystem.cs` (new) | LH-D8 group; `EmoteDecaySystem` OrderLast |
| T2.3 | Sonnet | `Utils/CrowdLife/ConversationEmoteRules.cs` (new), `Utils/CrowdLife/CrowdLifeBudget.cs` (new) | Pure static rules (LH-D10, LH-D11), plus `EmoteRules` and `PersonalityTraitLookup` in the same folder if under 60 lines each, otherwise T2.3b |
| T2.4 | Sonnet | `Systems/CrowdLifeSystemGroup/ConversationSystem.cs` (new) | LH-D11 using T2.3's names; only writes own entity |
| T2.5 | Sonnet | `StateMachineSystemGroup/ActionExecutionSystemGroup/BehaviorExecutionSystem.cs` (+ its command-handler file if commands are split out, grep `RequestSocialResponse`) | `AddIntoxication` and `ShowEmote` commands; both non-blocking, advance immediately |
| T2.6 | Sonnet | `Systems/CrowdLifeSystemGroup/IntoxicationSystem.cs` (new), `Systems/MovementSystemGroup/DrunkStumbleSystem.cs` (new) | LH-D13 tipsy tier + stumble; restore `baseMoveSpeed` when sober |
**Gate:** Systems + Utils. Then a **verifier** (Sonnet) checks T2.4 and T2.5 against LH-D11, LH-D7 and RULES (no cross-entity writes, no `.Run()`, `EnabledRef` naming).

### Wave 3 — awareness, damage, rendering, art tool *(5 workers, parallel)*
| Task | Model | Files | Brief essentials |
|---|---|---|---|
| T3.1 | Sonnet | `UtilityAwarenessSystemGroup/DrunkAwarenessSystem.cs` (new) | LH-D13 Brawl + PassOut options, same-faction only, the listed exclusions, tiers by `AIPriorityTier` name |
| T3.2 | Sonnet | `UtilityAwarenessSystemGroup/ItemAwarenessSystem.cs` | LH-D15 detour scaling only |
| T3.3 | Sonnet | `CombatReactionSystemGroup/DamageEventSystem.cs` | LH-D14 floor, one branch, before lethal handling |
| T3.4 | Sonnet | `PresentationSystemGroup/EmotePresentationSystem.cs` (new), `Authoring/CrowdLife/EmotePoolAuthoring.cs` (new) | LH-D9 + LH-D10; the baker builds the `RenderMeshArray` from serialized mesh + materials, follow `dots-authoring-baker` |
| T3.5 | Sonnet | `Editor/CrowdLife/EmoteAtlasGenerator.cs` (new) | LH-D17; `Texture2D` drawing, `EncodeToPNG`, `AssetDatabase` material creation; uses no `Handles`/`OnGUI` |
**Gate:** Systems, Authoring, Editor. A **verifier** checks T3.3 (it must be impossible for a brawl to kill) and T3.4 (no per-frame allocations besides TempJob; slots hidden when unused).

### Wave 4 — assets and scene *(orchestrator, inline, with the MCP)*
1. Run the generator menu, then check the PNG, materials and mesh exist.
2. Create `DrinkAlcoholAction` / `BrawlAction` / `PassOutAction` and `DrinkAlcoholBehaviour` / `BrawlBehaviour` / `PassOutBehaviour` (LH-D12, LH-D13), and add the behaviours to `_BehaviorLibrary`.
3. Create the `DrinkAlcoholModule`, `BrawlModule` and `PassOutModule` modules and add them to `HumanBaseBrain`.
4. Add a `Friendly` trait consideration to `TalkAction` and a `Resourceful` one to `EquipWeaponAction`.
5. Make sure `BanditBrain` has `FleeModule`.
6. Create the `Bar` interaction, add it to `_InteractionLibrary`, and place a bar in `CampLoop_Sub` by the fire. Add an `EmotePoolAuthoring` object to `CampLoop_Sub` and the Game subscene.
7. Set the LH-D16 values on `MaleCitizen.asset` / `Bandit.asset`.
8. Save, `refresh_unity`, read the console, and rebake by re-entering the subscene.

### Wave 5 — tests *(4 workers write, the orchestrator runs them)*
| Task | Model | File | Proves |
|---|---|---|---|
| T5.1 | Sonnet | `Tests/CrowdLifeRulesTests.cs` (EditMode) | Both sides of a pair compute the same mood, speaker and argument; budget and beat-scale math matches LH-D10 at 0/12/40/100 visible |
| T5.2 | Sonnet | `Tests/PlayMode/PersonalityRollTests.cs` | Two instances of one prefab get different values within `base ± variance`; a reroll after the pending flag is re-enabled changes them; the baker output does not depend on order |
| T5.3 | Sonnet | `Tests/PlayMode/BrawlNonLethalTests.cs` | Repeated same-faction Brawl hits stop at 35%; a non-brawl hit still kills |
| T5.4 | Sonnet | `Tests/PlayMode/EmotePresentationTests.cs` | With more live emotes than the budget, exactly `budget` slots are visible, ranked by priority then distance |
The orchestrator runs only these fixtures (lean test gate). Each must be watched failing (revert to fail with Burst off, per `feedback_burst_stale_after_revert`), else delete the test.

### Wave 6 — live check, measure, retire *(orchestrator)*
1. In CampLoop Play mode, use `execute_code` to check:
   - personalities differ across the bandits;
   - conversations alternate emotes;
   - a high-vice citizen drinks, wobbles, passes out;
   - a forced drunk + high-temper pair brawls and nobody drops below 35%.

   Put the player near the camp and confirm the emotes render. **Ask the owner to look**, because emotes are visual.
2. Profile the fight with the `Performance.md` method and record the LH-D18 number.
3. Retire this spec to `Verification/` with `verify-living-humans.md`. Update `Systems_AI.md` (a Crowd Life section) and `Plans/README.md`, the memory, commit and push.

## 7. Verification (what the Editor can show)
- CampLoop: two bandits standing together show alternating emotes over their heads, and warm or angry pairs look different.
- A citizen with a high vice roll walks to the bar, shows a mug, then drunk bubbles, and weaves while walking. Later they stand still with a zzz.
- A drunk brawl: two same-faction units trade punches, neither dies, guards don't join.
- 40+ humans on screen: fewer emotes at once, no emote further than 35 m, no frame-time change beyond LH-D18.

## 8. Known limits (not blockers)
- **No unit has toolkit animation data** (the MaleCitizen profile was deleted in c07dfd75). Drinking, punching, sway and passing out have no pose yet; the emotes carry the meaning. When the owner's animation profile lands, add `PlayAnimation` steps to these behaviours and a lie-down pose for PassOut.
- The emote art is a placeholder until the owner replaces `EmoteAtlas.png`, keeping the same 4×4 layout and icon order as `EmoteIcon`.

## Open decisions
- [x] All product calls answered (LH-D1–D4).
- [x] Architecture calls recorded (LH-D5–D18).
- [ ] Tuning numbers (LH-D12/13/16) are first guesses. Adjust them after the owner's first watch, without re-speccing.
