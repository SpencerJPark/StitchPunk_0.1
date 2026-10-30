# Trade System — Weekly Orders by Carrier Pigeon

> **Status:** 🔨 Phases 0–1 in build 2026-10-28; all open questions resolved (TR-D9–D16). The owner answered eight product questions (TR-D1–TR-D8, §11). Architecture calls were made under the standing delegation (TR-A1–TR-A12). Open tuning and content questions are marked **← DECISION** inline and collected in §12.
> **Inspiration:** GTA Chinatown Wars' dealer economy, cut down to **orders only**: clients ask for something, you make it, you deliver it, you get paid. There is no buy-low/sell-high market in v1 (TR-D5).

---

**Skills Needed:**
- `dots-feature-group`: two new top-level groups, `WorldTimeSystemGroup` and `TradeSystemGroup` (TR-A2)
- `dots-blob-library`: `ClientSO` + `OrderTemplateSO` → `ClientLibrary` blob (§4)
- `dots-authoring-baker`: `TradeDropOffAuthoring`, `PigeonArmTargetAuthoring`, `TradeBoardAuthoring`
- `dots-system-scaffold`: every system in §5
- `dots-test`: the EditMode reward/generation fixtures and the PlayMode delivery/expiry fixtures (§10)

---

## Context

What already exists and gets reused (checked 2026-10-28):

- **Production:** `WorkstationSummonSystem` on the caravan (`CampLoop_System.md` Phase 2, `Caravan_System.md` CV-D1). Summoned and field-revived units are both ordinary player `Minion`s, so **any minion of the right `UnitType` can fill an order**, however it was made.
- **Payment:** the `ResourceStack` ledger and `ResourceChangeRequest` deltas. `ResourceType.Currency` already exists and is in the HUD, but nothing earns it yet, so this system becomes Currency's first real source. The ledger is on the caravan (CV-D5) and is reached through `ResourceLedgerLink`.
- **Squads:** `PlayerHordeSlot`. A delivered unit has to leave its squad cleanly.
- **Interaction:** `PlayerInteractable` + `PlayerInteractableReach` for the pigeon and the drop-off hand-over.
- **Despawn:** the central `Despawn` funnel (`Despawn_System.md`). `UnitPoolReturnSystem` already culls pool units by distance, but the out-of-sight check delivered units need is new (§5).
- **No world clock exists.** `SchedulesWaypoints_System.md` §2 specs a `WorldClock` but it was never built. **This spec builds it (TR-A1)**, and Schedules then reuses it as is: `timeOfDay01` is the same field.
- **Save:** `IPersist` snapshots single components only. Buffers and `Entity` fields are excluded (`PersistComponents.cs`). The ledger gets around this with `ResourceSaveSyncSystem` + a snapshot component, and trade does the same (TR-A10).

## 1. The loop

```
Week N starts (Day 1, 06:00)
  └─ WeeklyOrderGenerationSystem rolls the week's offers onto the TradeBoard
  └─ a pigeon flies to the player and lands on their arm
Player interacts with the pigeon → Letter UI: accept / decline each offer
  └─ accepted orders get a map marker at their drop-off
Player builds the units (summon / revive) and brings them to the drop-off (on foot or in the caravan)
Player interacts with the client marker → "Hand over"
  └─ check type + quantity + health → pay into the ledger, reputation goes up
  └─ the units leave the squad, walk to the client and stay until the player is far away and can't see them, then despawn
Due day passes on an accepted, undelivered order → it fails and reputation goes down
Week ends (Day 7 → Day 1) → unaccepted offers expire silently; the next week starts
```

## 2. Phases

| # | Phase | Status | Milestone |
|---|---|---|---|
| 0 | **World clock**: `WorldClock` singleton, `WorldTimeSystemGroup`, day/week rollover signals, HUD clock, debug skip-day/skip-week | 🔨 built 2026-10-28, smoke-tested headless, awaiting owner play check | the HUD shows "Week 1 · Mon 06:00", and skipping a week fires `WeekStarted` once |
| 1 | **Clients + order generation**: `ClientSO`, `OrderTemplateSO`, `ClientLibrary` blob, drop-off points, `TradeBoard` singleton, weekly roll, difficulty + reward maths | 🔨 built 2026-10-28, smoke-tested headless, awaiting owner play check | a debug menu lists the week's offers with difficulty and payout; same seed → same week |
| 2 | **Pigeon + letter**: pigeon flies to the player's arm and the letter opens by itself (UI Toolkit placeholder), accept/decline, drop-off markers | 🔨 built 2026-10-28, reworked 2026-10-29, headless-tested, awaiting owner play check | accepted orders show markers in the world |
| 3 | **Delivery**: hand-over interaction, validation, payout, squad removal, walk to the client, out-of-sight despawn | 🔨 built 2026-10-29, awaiting owner play check | deliver 2 Brutes to the Army → Currency goes up, the Brutes stand at the barracks, and they are gone when you come back |
| 4 | **Deadlines + reputation**: due-day expiry, reputation tiers changing payout, order count and order size | 🔨 built 2026-10-30, awaiting owner play check | failing an order drops the tier, and the next week's letter from that client is smaller |
| 5 | **Content pass**: the first recurring clients, household templates, drop-offs placed in the game scene, new order-only unit types | ⬜ | settled: TR-D14 (flavour on existing types) |
| 6 | **Save**: clock + board + relationships survive a save/load | ⬜ | load mid-week → same day, same accepted orders, same reputation |

Phases 0 → 1 → 2 → 3 run in order. Phase 4 needs 3, and phase 6 can be built any time after 4. Phase 5 is content and runs alongside the rest once 1 lands.

## 3. Architecture

```
WorldTimeSystemGroup (NEW, GameSceneSystemGroup, after GameManager, before Player)
    WorldClockSystem               ticks time, writes WorldClock, raises DayStarted / WeekStarted on the TradeBoard

PlayerSystemGroup / WorkstationInputSystemGroup
    (existing interaction resolution sets PlayerInteractable targets; the pigeon + drop-off reuse it)

BuildingsSystemGroup
    ...existing workstation systems...

TradeSystemGroup (NEW, GameSceneSystemGroup, after Buildings, before PlayerResource)
    WeeklyOrderGenerationSystem    WeekStarted → expire leftover offers → roll new offers → spawn the pigeon request
    TradeOrderResponseSystem       accept/decline requests from the letter UI → order state
    TradeDeliverySystem            hand-over request at a drop-off → validate → pay (ResourceChangeRequest) + reputation + release the units
    TradeOrderExpirySystem         DayStarted → fail accepted orders past their due day → reputation penalty

PlayerResourceSystemGroup
    ResourceChangeSystem           drains the payout deltas the same frame

LateSim: SpawnSystemGroup
    PigeonSpawnSystem              PendingPigeon → instantiate the pigeon prefab, flying toward the player's arm

LateSim: DespawnSystemGroup
    DeliveredUnitDespawnSystem     DeliveredToClient + far from the player + outside the camera view → Despawn

Presentation (MonoBehaviour)
    PigeonPresenter                flight-in arc + landing + idle peck
    TradeLetterUI                  UI Toolkit placeholder letter: offers, accept/decline, Rive later
    TradeOrderMarkerPresenter      world markers on accepted drop-offs + a small "Orders" HUD list
    WorldClockHudUI                "Week 3 · Thu 14:20"
```

**TR-A1: one shared `WorldClock`.** It matches the `SchedulesWaypoints_System.md` §2 struct, with week fields added, so the Schedules build later only adds its scoring axis.

**TR-A2: two new top-level groups.** Time is a world service that other features (Schedules, shop hours, night spawns) will read, so it doesn't belong inside Trade. Both groups derive from `GameSceneSystemGroup`, so menus don't advance time and there is no per-system `GameSceneTag`. `TradeSystemGroup` runs **before** `PlayerResourceSystemGroup`, so a payout reaches the HUD the same frame. Both are declared in `SystemGroups.cs`, and the Sim order line in `CLAUDE.md` gains `→ WorldTime` after GameManager and `→ Trade` after Buildings.

**TR-A3: orders live in a singleton buffer, not as entities.** There are at most ~12 orders a week, the save system can't remap `Entity` fields yet, and the letter UI wants one list. So the `TradeBoard` singleton entity carries `DynamicBuffer<TradeOrder>` + `DynamicBuffer<ClientRelationship>`. Orders reference clients by `ClientId` and drop-offs by `DropOffId` (ints baked from SOs), never by `Entity`.

**TR-A4: entry patterns.** Every request is an **enableable component on the entity it acts on**, as in Camp Loop (CL-A1):
- `TradeOrderResponseRequest` (buffer on `TradeBoard`: `{orderId, accept}`), written by the letter UI
- `TradeHandOverRequest` (enableable on the **drop-off** entity), set when the player interacts with that drop-off's client marker
- `DayStarted` / `WeekStarted`: enableable tags on `TradeBoard`, raised by `WorldClockSystem` and **cleared by `WorldClockSystem` at the start of its next update** (one-frame, deterministic, so nobody else has to consume them)
- `PendingPigeon` (buffer on `TradeBoard`), drained by `PigeonSpawnSystem`

**TR-A5: hand-over is an interaction, not automatic.** Walking a squad past a drop-off must never give away units by accident. The player presses interact at the client marker, and only units within `handOverRadius` of the drop-off count.

**TR-A6: delivered units become scripted NPCs, not minions.** On hand-over each unit: is removed from its `PlayerHordeSlot`; has `Minion` + `PlayerUnitBrain` disabled; gets `DeliveredToClient { dropOffId, standPoint }`; and gets a `MovementAPI` move to the drop-off's stand point, then idles. `UtilityBrain` stays off, so it doesn't wander. This is the inverse of `PlayerMinionSpawnInitSystem`.

**TR-A7: out-of-sight despawn (TR-D8).** `DeliveredUnitDespawnSystem` despawns a delivered unit once **both** of these hold: distance to the player > `deliveredDespawnDistance` (default 60 m), and the unit's position is outside the main camera's view (a viewport test against `AnimationToolkitCameraData`, or the camera singleton the billboard path already reads). It goes through the `Despawn` funnel, so pooling works as for any unit.

**TR-A8: randomness is seeded.** The weekly roll uses `Random(hash(worldSeed, weekNumber))`, so a reload or a test sees the same week. `worldSeed` lives on `TradeBoard` and is saved.

**TR-A9: difficulty and reward are pure static functions** in `Utils/TradeMath.cs` (Burst-compatible, EditMode-testable), not inline in the systems.

**TR-A10: save goes through snapshots**, the same way as `ResourceSaveSyncSystem`: `WorldClockSnapshot : IPersist` and `TradeBoardSnapshot : IPersist` with `FixedList512Bytes<TradeOrderSaved>` + `FixedList128Bytes<ClientRelationshipSaved>`. That gives a cap of about 16 orders and 30 clients, which is fine for v1. Log a warning if either overflows.

**TR-A11 (reworked 2026-10-29): the pigeon is an entity that flies to a `PigeonArmTarget` child of the player**, lands, opens the letter after a short delay, and flies off and is destroyed once the letter is closed. No interaction is involved.

**TR-A12: UI is a UI Toolkit placeholder** (CL-D11 precedent), built so a Rive letter artboard can replace it later.

## 4. Data model

### Clock (`Components/Time/WorldClockComponents.cs`)

```csharp
public struct WorldClock : IComponentData
{
    public float timeOfDay01;          // 0 = midnight. Schedules reads this unchanged.
    public int   dayOfWeek;            // 0..6, 0 = Monday
    public int   weekNumber;           // 1-based
    public float secondsPerDay;        // default 1440 (24 real minutes, TR-D9)
    public float dayStartTimeOfDay01;  // 0.25 (06:00): a new week starts at Mon 06:00, not midnight
    public bool  isPaused;             // letter UI or any menu open (TR-D10)
}
public struct DayStarted  : IComponentData, IEnableableComponent { }   // on TradeBoard, one frame
public struct WeekStarted : IComponentData, IEnableableComponent { }   // on TradeBoard, one frame
```

### Authored data (`Data/Trade/`)

```csharp
public enum ClientKind : byte { Household, Recurring }
public enum DropOffTag : byte { Household, Military, Noble, Church, Industrial }   // one-offs pick a drop-off by tag

[CreateAssetMenu] public class ClientSO : ScriptableObject
{
    public int        clientId;               // stable; serialized into saves
    public string     displayName;            // "The Crown Army, 3rd Garrison"
    public ClientKind kind;                   // Recurring for authored clients
    public int        homeDropOffId;
    public Sprite     letterSeal;             // shown on the letter (UI only, not baked)
    public float      baseOrderChancePerWeek; // 0.6
    public OrderTemplateSO[] orderPool;
    public ReputationTierThresholds tiers;    // default 0 / 25 / 50 / 80
}

[CreateAssetMenu] public class OrderTemplateSO : ScriptableObject
{
    public string   flavourText;              // "We need muscle for the bridge. Strong and fresh."
    public UnitType unitType;
    public int2     quantityRange;            // (1,1) maid, (2,4) soldiers
    public float    minHealthFraction;        // 0 = any condition, 0.75 = "fresh"
    public int2     dueDayRange;              // days after acceptance, clamped to the week end
    public ReputationTier minimumTier;        // the Army's big orders need Trusted
    public float    weight;
    public ResourceAmount[] bonusResources;   // optional: scrap/electricity paid on top of Currency
}
```

Household one-offs use a separate `HouseholdOrderTablesSO`: a name list ("The Pemberton household"), a template list, and the `DropOffTag.Household` pool. Each roll invents a one-time client with a fresh negative `clientId`, which never earns a relationship row.

**Baked:** `ClientLibrary` blob (the `dots-blob-library` five-file pipeline) with `BlobArray<ClientBlob>` and nested `BlobArray<OrderTemplateBlob>`, `HouseholdTablesBlob`, and `TradeTuningBlob` (§4 tuning). Display strings stay out of the blob: the UI looks names and flavour text up from the SOs by id through a managed `TradeTextLookup`.

### Runtime (`Components/Trade/TradeComponents.cs`)

```csharp
public enum TradeOrderState : byte { Offered, Accepted, Delivered, Failed, Expired, Declined }

public struct TradeBoard : IComponentData { public uint worldSeed; public int nextOrderId; }

public struct TradeOrder : IBufferElementData
{
    public int    orderId;
    public int    clientId;           // < 0 = a household one-off
    public int    templateIndex;      // into the client's pool, or the household table
    public int    dropOffId;
    public UnitType unitType;
    public byte   quantity;
    public half   minHealthFraction;
    public byte   dueDayOfWeek;       // absolute 0..6 in the week it was issued
    public int    weekIssued;
    public float  difficulty;         // frozen at roll time, so the letter and the payout agree
    public int    currencyReward;     // before the condition bonus
    public TradeOrderState state;
}

public struct ClientRelationship : IBufferElementData
{
    public int clientId;
    public int reputation;            // 0..100, starts at 10
    public int ordersCompleted;
    public int ordersFailed;
}

public struct TradeOrderResponseRequest : IBufferElementData { public int orderId; public bool accept; }
public struct PendingPigeon            : IBufferElementData { public int weekNumber; }

// On each drop-off entity (baked from TradeDropOffAuthoring)
public struct TradeDropOff : IComponentData
{
    public int        dropOffId;
    public DropOffTag tag;
    public float      handOverRadius;   // 6 m
    public float3     standPoint;       // where delivered units go and idle
    public byte       dangerRating;     // 0..3, authored: feeds difficulty
}
public struct TradeHandOverRequest : IComponentData, IEnableableComponent { }

// On delivered units
public struct DeliveredToClient : IComponentData { public int dropOffId; public int orderId; }

// Pigeon
public struct CarrierPigeon : IComponentData { public int weekNumber; }
```

### Difficulty and reward (`Utils/TradeMath.cs`, TR-A9)

```
difficulty = quantity × unitTierWeight[unitType]       // from UnitDataBlob.summonCost: corpse + scrap + electricity value
           + minHealthFraction × healthDifficultyWeight  // fresh units are harder
           + deadlineTightness × deadlineWeight          // (7 − daysAllowed) / 7
           + dropOff.dangerRating × dangerWeight

currencyReward = round(difficulty × currencyPerDifficulty × reputationPayoutMultiplier[tier])
payout at hand-over = currencyReward
(no condition bonus, TR-D11)
```

`unitTierWeight` comes from the existing `summonCost`, so no second price list has to be kept in sync. All weights live on a `TradeTuningSO` → `TradeTuningBlob`.

### Reputation (Phase 4, TR-D4)

| Tier | Reputation | Payout × | Offers per week from this client | Max order size |
|---|---|---|---|---|
| Stranger | 0–24 | 1.0 | 0–1 | template min |
| Known | 25–49 | 1.1 | 1 | mid |
| Trusted | 50–79 | 1.25 | 1–2 | full range, unlocks `minimumTier = Trusted` templates |
| Partner | 80–100 | 1.4 | 2 | full range, prefers the big templates |

Deltas: delivered **+8** (+4 more if early), failed (accepted, past due) **−15**, declined **0**, ignored (never opened the letter) **−2**  (TR-D12: no decay).

## 5. Systems

| System | Group | Behaviour |
|---|---|---|
| `WorldClockSystem` | `WorldTimeSystemGroup` | First: disable `DayStarted`/`WeekStarted` from last frame. If not paused: `timeOfDay01 += dt / secondsPerDay`. On crossing `dayStartTimeOfDay01`: `dayOfWeek++`, enable `DayStarted`. When `dayOfWeek` wraps 6 → 0: `weekNumber++`, enable `WeekStarted`. On the first frame of a new game, raise `WeekStarted` for week 1. |
| `WeeklyOrderGenerationSystem` | `TradeSystemGroup` | On `WeekStarted`: mark leftover `Offered` orders `Expired`; remove `Delivered`/`Failed`/`Expired`/`Declined` rows older than one week. Roll: for each recurring client, `chance = baseOrderChancePerWeek × tierFrequency`; pick templates by weight, filtered by `minimumTier`; roll quantity, due day, condition (scaled by tier). Then add `householdOffersPerWeek` (default 2, TR-D13) household one-offs. Freeze `difficulty` + `currencyReward`. Append `PendingPigeon`. |
| `TradeOrderResponseSystem` | same | Drains `TradeOrderResponseRequest`: `Offered` → `Accepted` / `Declined`. On acceptance, `dueDayOfWeek = min(today + rolled days, 6)`. Any other state is ignored (logged). |
| `TradeDeliverySystem` | same | For each drop-off with `TradeHandOverRequest` enabled: find the oldest `Accepted` order at that drop-off. Collect player `Minion`s of `unitType` within `handOverRadius` whose `Health.current / max ≥ minHealthFraction`, closest first. If the count is short of `quantity`, refuse (`TradeHandOverRefused` toast: "Need 2 more Brutes (fresh)"). Otherwise take exactly `quantity` units → release them (TR-A6) → queue `ResourceChangeRequest{Currency, payout}` + bonus resources on the ledger via `ResourceLedgerLink` → reputation + stats → state `Delivered`. Disable the request. |
| `TradeOrderExpirySystem` | same | On `DayStarted`: an `Accepted` order with `dueDayOfWeek < dayOfWeek` (or one from an earlier week) → `Failed`, reputation penalty, `TradeOrderFailed` toast. |
| `PigeonSpawnSystem` | `SpawnSystemGroup` | Drains `PendingPigeon` → instantiates the baked pigeon prefab with `CarrierPigeon` + `PigeonFlight`; `PigeonFlightSystem` flies it to the arm and `PigeonLetterSystem` opens the letter. Only one pigeon at a time: a still-unread pigeon is reused for the new week. |
| `PigeonReadSystem` | `TradeSystemGroup` | Player interaction on a `CarrierPigeon` → opens the letter (sets a managed flag the UI polls, or a `TradeLetterOpen` singleton tag) → the pigeon flies off (presenter) and is destroyed. |
| `DeliveredUnitDespawnSystem` | `DespawnSystemGroup` | TR-A7. |

Edited: `SystemGroups.cs` (two groups), `CLAUDE.md` Sim order, `Contracts.md` (four new requests/signals), and `PlayerUnit.prefab` (a `PigeonArmTarget` child).

## 6. UI (UI Toolkit placeholders, TR-A12)

- **`WorldClockHudUI`**: one label under the resource row: `Week 3 · Thu 14:20`. A pigeon icon blinks while an unread letter waits.
- **`TradeLetterUI`**: parchment panel, one card per offer: client seal + name, flavour text, the requirement as symbols (unit icon × quantity, a heart with "≥75%" when there's a condition, a calendar with "by Thu"), the payout, and Accept / Decline buttons (the primary action big, per the readable-panels rule). A footer line shows each recurring client's tier. Closes with Esc. Can be reopened from the HUD orders list until the week ends.
- **`TradeOrderMarkerPresenter`**: a world-space marker over each accepted order's drop-off (client seal + "2/2 Brutes" progress, counting matching minions in range), plus an "Orders" HUD list in the corner showing the due day, turning red on the due day.
- **Hand-over prompt:** the existing interaction prompt on the drop-off's client marker reads "Hand over 2 Brutes".

## 7. Content — first pass (Phase 5)

| Client | Kind | Home | Wants | Notes |
|---|---|---|---|---|
| The Crown Army, 3rd Garrison | Recurring | Barracks drop-off (Military, danger 2) | Brutes ×2–4, Runners ×3–5 (scouts), fresh at Trusted+ | The big earner; flavour only, TR-D14 |
| Lady Ashgrove's House | Recurring | Manor (Noble, danger 0) | one unit, high condition, generous deadline | A small, steady relationship |
| The Bellfounders' Guild | Recurring | Foundry (Industrial, danger 1) | Brutes for hauling, any condition | Pays in scrap + electricity on top of Currency |
| Households | One-off | any Household drop-off | one basic zombie "to do the chores" | 2 a week, low pay, low risk |

**TR-D14:** v1 orders only use the three summonable types; flavour text carries the fiction.

## 8. File manifest

- `Components/Time/WorldClockComponents.cs`
- `Components/Trade/TradeComponents.cs`
- `Data/Trade/ClientSO.cs`, `OrderTemplateSO.cs`, `HouseholdOrderTablesSO.cs`, `TradeTuningSO.cs`, `TradeBlobs.cs`
- `Components/EntityLibraries.cs` (append the `ClientLibrary` component pair)
- `Authoring/Trade/TradeBoardAuthoring.cs` (clock settings, seed, library SO refs), `TradeDropOffAuthoring.cs`, `PigeonArmTargetAuthoring.cs`
- `Systems/PostBakingSystemGroup/ClientLibraryBakingSystem.cs`
- `Systems/WorldTimeSystemGroup/WorldClockSystem.cs`
- `Systems/TradeSystemGroup/WeeklyOrderGenerationSystem.cs`, `TradeOrderResponseSystem.cs`, `TradeDeliverySystem.cs`, `TradeOrderExpirySystem.cs`, `PigeonReadSystem.cs`
- `Systems/SpawnSystemGroup/PigeonSpawnSystem.cs`
- `Systems/DespawnSystemGroup/DeliveredUnitDespawnSystem.cs`
- `Systems/SaveSystemGroup/TradeSaveSyncSystem.cs`, `TradeLoadSyncSystem.cs` (Phase 6)
- `Utils/TradeMath.cs`
- `MonoBehaviours/Trade/PigeonPresenter.cs`, `TradeOrderMarkerPresenter.cs`
- `UI/WorldClockHudUI.cs`, `UI/TradeLetterUI.cs` (+ `.uxml`/`.uss`), `UI/DebugTradeMenu.cs`
- Assets: `Assets/Prefabs/Trade/CarrierPigeon.prefab`, `Assets/Data/Trade/Clients/*.asset`, `TradeTuning.asset`

## 9. Build phase gates

Each phase: compile gate (`devloop gate`) → rebake → play in the Camp Loop scene with `secondsPerDay` set to 20 for testing.

0. HUD clock advances. Debug "Skip to next week" fires `WeekStarted` exactly once (logged).
1. `DebugTradeMenu` lists the offers, and two runs with the same seed list the same week. EditMode `TradeMathTests` pass.
2. The pigeon lands on the caravan. Interacting opens the letter. Accepting puts a marker on the drop-off, and declining greys the card.
3. Deliver the Army's Brutes: Currency rises by the shown payout, the Brutes leave the squad list and walk to the stand point, and after driving 60 m+ away and back they are gone. A short squad is refused with the toast.
4. Skip past a due day → the order fails, the toast fires, and the tier drops on the next letter.
6. Save mid-week, quit, load → the same clock, board and tiers.

## 10. Fixtures (revert-to-fail each)

- **F1 `TradeMathTests`** (EditMode): difficulty is monotonic in quantity, condition and danger; reward × tier multiplier; condition bonus capped at 1.25. Revert-to-fail: drop the tier multiplier.
- **F2 `WeeklyOrderGenerationTests`** (PlayMode): same seed + week → an identical buffer; a Trusted-only template is never rolled for a Stranger. Revert-to-fail: drop the tier filter.
- **F3 `TradeDeliveryTests`** (PlayMode): 3 Brutes in range at full HP, order wants 2 → exactly 2 released, one Currency delta of the right size, state `Delivered`. Two in range but one below `minHealthFraction` → refused, no delta. Revert-to-fail: drop the health check.
- **F4 `TradeOrderExpiryTests`** (PlayMode): accepted, due Wed, `DayStarted` on Thu → `Failed` with the reputation delta; a declined order is untouched. Revert-to-fail: the state guard.

No fixtures for the clock tick, the UI or the pigeon: play-test those (the project's test-only-essentials rule).

## 11. Decisions recorded (do not re-ask)

| ID | Decision |
|---|---|
| TR-D1 | **Real-time clock**, week by week. The pigeon arrives at the start of each week. |
| TR-D2 | **Delivery = bring the units to the client's drop-off on the map**, on foot or by caravan. |
| TR-D3 | An order asks for **unit type + quantity**, optionally **a minimum condition (health)** and **a due day within the week**. No loadout requirements in v1. |
| TR-D4 | Rewards = **Currency + resources**, scaled by difficulty, with **reputation** changing pay, how often a client orders, and order size. Failing hurts. |
| TR-D5 | **Orders only** in v1. No price-swing market. (A demand modifier on payouts can come later.) |
| TR-D6 | **Authored recurring clients + randomly generated household one-offs.** |
| TR-D7 | **A pigeon lands at the caravan; interacting opens a letter** to accept/decline each order; accepted orders get map markers. |
| TR-D8 | A delivered unit **stays in the world until the player is far away and can't see it**, then despawns. |
| TR-D9 (Q1) | `secondsPerDay` = **1440** (24 real minutes per day, 1 real minute = 1 game hour). A week is 2 h 48 min of real time. Stays a tuning field on `TradeBoardAuthoring`. |
| TR-D10 (Q2) | The clock **pauses for the letter and for any menu** (settings / pause / station). Driven by a managed "UI blocks time" flag feeding `WorldClock.isPaused`. |
| TR-D11 (Q3) | **No condition bonus.** `minHealthFraction` is strict pass/fail; payout = `currencyReward`. |
| TR-D12 (Q4) | Reputation deltas as in §4; **no decay**, no `ReputationDecaySystem` in v1. |
| TR-D13 (Q5) | **2** household one-offs per week; recurring clients add 0–2 each. |
| TR-D14 (Q6) | **Flavour on existing types**; no new `UnitType`s in v1. |
| TR-D15 (Q7) | **All at once**; no partial hand-over. |
| TR-D16 (Q8) | A delivered unit is a **normal unit** until it despawns; no reputation effect. |

## 12. Open decisions

All resolved 2026-10-28 (recorded in §11 as TR-D9–TR-D16).

### Build deviations (Phases 0-1, 2026-10-28)

- **Signals live on the `WorldClock` entity, not `TradeBoard`** (TR-A4): the clock is a world service and must not depend on Trade. `WorldClockAuthoring` and `TradeBoardAuthoring` are two separate singletons. `WorldClockSkipRequest` (debug) and `TradeRerollRequest` (debug, proves same seed -> same week) are new enableables.
- **`TradeBoard` is `{ worldSeed }` only.** `orderId = weekNumber * 100 + index`, so a re-roll of the same week reproduces ids; household client ids are `-orderId`. `TradeOrder` gains `daysAllowed` (rolled at generation) next to `dueDayOfWeek` (set on acceptance); `minHealthFraction` is a `float`, not `half`.
- **Pause:** the existing pause/settings menus set `Time.timeScale = 0`, and `WorldClockSystem` ticks off the simulation delta time, so they stop the clock with no extra code. `WorldClock.isPaused` is the hook the letter UI (Phase 2) sets.
- **Reputation is read, not written, in Phase 1:** generation already filters templates by tier and scales offer count, order size and payout from `ClientRelationship.reputation`; the delivery/expiry systems that change it are Phases 3-4.
- **Roll logic is `Utils/TradeOrderRoller.cs`** (a pure function over the board buffers, so F2 can call it without the system); data SOs sit in `Data/SOs/` + `Data/Structs/TradeBlobs.cs`, matching the existing layout rather than a `Data/Trade/` folder. The client library is baked onto the `TradeBoard` entity by `TradeBoardAuthoring` (no separate library authoring).
- Per-tier health-condition scaling from the §5 roll description is not implemented; the template's `minHealthFraction` is used as authored.
- **Smoke test 2026-10-28 (execute_code in Play mode):** week 1 rolled on frame one; a 7-day skip raised `WeekStarted` once (week 2 rolled, week-1 offers `Expired`); re-rolling with seed 777 twice gave identical rows. F1 + order/placement fixtures: 13/13. Test content lives in `Assets/ScriptableObjects/Trade/` (Phase 5 replaces it); the CampLoop subscene has `WorldClock`, `TradeBoard` and three drop-offs (ids 1 Military, 2 Noble, 10 Household).

### Build deviations (Phase 2, 2026-10-28; reworked 2026-10-29)

- **Owner call 2026-10-29: the pigeon flies to the player and lands on their arm** (replaces the caravan perch and the E interaction). The landing point is a `PigeonArmTarget` child on `PlayerUnit.prefab` (`PigeonArmTargetAuthoring`, gizmo-drawn, default local (0.45, 1.25, 0)); move it to where the reach pose puts the hand, or parent it to an animated bone. Without one the pigeon uses `TradePigeonSettings.fallbackArmLocalOffset` on the player root.
- **The letter opens by itself** `letterOpenDelaySeconds` (1.2) after landing: that gap is reserved for the player's reach animation. `PigeonLetterSystem` (`TradeSystemGroup`) opens it, and sends the pigeon off once the letter is closed. The HUD `Letter` button still reopens the letter later (and counts as delivery if the pigeon is on the arm).
- **Removed:** `PlayerInteractablePriority`, the targeting change, `PigeonReadSystem` and `TradeInputSystemGroup`. Phase 3's hand-over still needs an early E-press group (see Gotchas.md).
- `PigeonFlightSystem` (Burst, `TradeSystemGroup`) replaces a presenter Mono; the pigeon is a placeholder sphere.
- **No Esc to close** yet (Esc is the game menu): there is a Close button. Buttons are clickable under the virtual cursor via `VirtualCursorPanel` + the router change in Contracts.md.
- **Markers** are a pole and camera-facing `TextMesh` per drop-off with accepted orders; the "2/2 Brutes" progress count waits for Phase 3.
- Fixture **F5 `TradeOrderResponseTests`** (PlayMode, revert-to-fail proven on the state guard) covers the due-day clamp and the Offered-only guard.

### Build deviations (Phase 3, 2026-10-29)

- **`DropOffInteractSystem` lives in `TradeInputSystemGroup`** (re-added: Player group, after `WorkstationInputSystemGroup`) because `PlayerPickupJob` clears the E press inside Item. `TradeDeliverySystem` stays in `TradeSystemGroup`.
- **A drop-off is only `PlayerInteractable` while an accepted order waits there**, so an idle marker never swallows E. Targeting is the existing nearest-within-5 m rule: with the caravan parked beside the marker, whichever is nearer gets the press.
- **Every accepted order at the drop-off is tried, oldest first**, not just the oldest: an order that cannot be filled does not block a later one that can. If none can be filled the toast names the first. Units are claimed once, so two orders never share a unit.
- **Reputation is paid on delivery** (+8, +4 if delivered before the due day, `TradeTuningSO.reputation*`), already part of Phase 4's table; the failed-order loss (`reputationLossFailed`) waits for the expiry system.
- **"Cannot see it" uses the `CameraView` singleton** (view circle + 6 m), not `CameraVisible`, which is presentation-only (RULES.md). Despawn needs the player >60 m away, the unit >= 3 s at the client, and the unit outside the view circle.
- **Release is deferred until every buffer write is done**: `AddComponent` on a unit invalidates the `DynamicBuffer`s the system holds.
- The walk order is issued 0.3 s after hand-over so the `ActionInterruptRequest` raised at release finishes tearing the old behavior down first. Units get a spiral of spots around `standPoint`.
- Markers now show `n/N Unit` from the player's minions in range ("ready, press E" when full); a toast shows `Need 2 more Brutes (at least 75% health)` / `Delivered — +130 Currency`.
- Fixture **F3 `TradeDeliveryTests`** (PlayMode): closest N taken and one payout, and a unit below the health floor is refused.
- Fixture **F6 `DeliveredUnitDespawnTests`** (PlayMode, revert-to-fail proven on the out-of-view condition): far-but-in-view stays, far-and-out-of-view despawns and loses the marker.
- **Smoke test 2026-10-29 (execute_code in Play mode):** accepting #102 then a hand-over request with a staged unit gave state Delivered, +48 Currency on the ledger, toast `HandOverDone`, and the unit left the squad as `DeliveredToClient`. A unit with its utility brain off (as a real minion has) reached its stand point; a borrowed bandit with a live brain wandered back to its own post, which is the AI, not the delivery. Despawn at 60 m was covered by F6 rather than driven in the scene. The E press itself and the on-screen marker/toast were not looked at.
- **Test-run gotcha:** a PlayMode run replaces the open scene with a temporary `InitTestScene...`; reload `Assets/Scenes/CampLoop.unity` before entering Play mode afterwards (the subscene is not baked into the temp scene, so the world has no Player and no board).

### Build deviations (Phase 4, 2026-10-30)

- **`TradeOrderExpirySystem`** (`TradeSystemGroup`, non-Burst) runs on `DayStarted`. An `Accepted` order fails when its due day is before today, or when its week has ended (`TradeMath.IsOrderOverdue`): Accepted orders survive the weekly roll on purpose. A skip that jumps several days raises `DayStarted` once, which is enough because the check is a comparison, not a count.
- **Penalty:** `TradeTuningSO.reputationLossFailed` (15), stopping at 0; `ordersFailed++`. A household one-off fails with no reputation effect (it has no relationship row). One toast per day: the first failure's order id and the reputation actually lost.
- **The "ignored letter" penalty (-2) is dropped**: the letter now opens by itself when the pigeon lands, so a letter can no longer be ignored. TR-D12 already ruled out decay.
- **Tier effects on offers/size/payout were already live from Phase 1** (the roll reads `ClientRelationship.reputation`); Phase 4 only added the thing that lowers it. No new roll code.
- Fixture **F4 `TradeOrderExpiryTests`** (PlayMode, revert-to-fail proven on the Accepted-only guard). Smoke 2026-10-30: accepted #101 (Army), skipped 5 days, it went Failed, reputation 40 -> 25, `OrderFailed` toast. Same seed re-rolled at Partner (80) gave two Army orders of 4 and 3 units at x1.4 pay (317/246), at Stranger (0) one order of 2 units (130).
