# Factory Minimal Loop — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Raw source:** [`../Claude/Code_Audit_2026-07.md`](../Claude/Code_Audit_2026-07.md) item #11 + `futureneedsplan.md` step 10 — "**1 product, 1 line, 1 buyer** — resist building the economy."
> **Prerequisite note:** the production loop is currently **PARKED** — `ProductionSystem` + `FactoryLibraryBakingSystem` live fully-commented in `Core/Unused/` (2026-07 structural pass). Phase 0 is un-parking them. `PlayerResource_System.md` (spec ready) is the output sink and should build first or alongside.

---

**Skills Needed:**
- `dots-blob-library` — only if `FactoryLibraryBlob` needs re-scaffolding after un-parking (§4)
- `dots-authoring-baker` — station/scene wiring fixes discovered during un-park (§6)
- `dots-unit-ai` — worker-staffing behavior (phase 4, optional)

---

## 1. Purpose & v1 scope

Make the factory loop **run end-to-end for the first time**: the code shipped in Phase 1 (grid + `ProductionSystem` tick) but was parked; no `ProductionRecipeSO` assets or `_FactoryLibrary` asset were ever created, so the loop has never produced anything. Vertical-slice scope: one recipe (`CorpseBody + MechScrap + ElectricCharge → FleshAutomaton`), one station chain, output feeding the Player Resource ledger.

**Out of v1:** placement UI (Phase 2), conveyor belts, the economy/trade layer, multiple recipes. Worker carry (Phase 3) is ← DECISION'd below.

## 2. Architecture

Already designed and coded — this plan is **activation + assets + the missing sink**, not architecture. The one new seam: production output → player resources. `ProductionSystem.TickProductionJob` writes `StationOutputSlot`; v1 adds a small `OutputCollectionSystem` (BuildingsSystemGroup, after ProductionSystem) that drains the `OutputBay` station's slots into the `ResourceStack` ledger via the PlayerResource plan's delta-buffer mutation contract.

**← DECISION:** how outputs reach the OutputBay in v1 — (a) stations chain directly (output slot feeds next station's input slot by grid adjacency — pure data, no workers); (b) worker carry (`CarryTask` + `WorkerCarrySystem`, the original Phase 3). *Recommendation: (a) for the minimal loop — worker carry is the audit's "undead staffing reuses revival + minion orders" payoff, but it's a whole behavior; land the tick first.*

## 4. Data model

Assets to create (Editor work): 4 `ProductionRecipeSO` (one per station type: PrepTable, AssemblyBench, GalvanicCharger, OutputBay), `_FactoryLibrary` SO referencing them. Code already expects them (`FactoryLibrarySO` → `FactoryLibraryBlob`).

## 5. Systems

- **Un-parked:** `ProductionSystem` → `Systems/BuildingsSystemGroup/`, `FactoryLibraryBakingSystem` → `Systems/PostBakingSystemGroup/` — restore from `Core/Unused/`, uncomment, fix drift vs current APIs (the park predates the rig + DamageEvent v2 commits; expect small compile fixes). The conformance tests will pass once they're back in their folders with live `[UpdateInGroup]`.
- **New:** `BuildingsSystemGroup/OutputCollectionSystem.cs` — OutputBay slots → resource deltas.
- **Note:** `BuildingsSystemGroup` is already declared + gated (`GameSceneSystemGroup`); its folder is currently empty and waiting.

## 7. Integration points

PlayerResource ledger (hard dependency for phase 3), `FactoryGridAuthoring`/`FactoryStationAuthoring` scene setup in DOTSTestScene, [[Contracts]] row if `OutputCollection` introduces a request (prefer direct buffer write inside the Buildings feature — it's intra-feature).

## 9. Build phases

0. **Un-park:** restore both files, uncomment, compile clean, conformance tests green.
1. Assets: 4 recipes + `_FactoryLibrary`; scene: grid + 4 stations in DOTSTestScene; hand-populate `StationInputSlot` in the Entities inspector → watch `ProductionProgress` tick and outputs write. *(This is the audit's original "test" step that never ran.)*
2. Station chaining (decision (a)) → full line: inputs at PrepTable → FleshAutomaton at OutputBay.
3. `OutputCollectionSystem` → ResourceStack ledger + HUD shows the product count.
4. *(Optional / decision (b))* worker-carry behavior via `dots-unit-ai`.

## 10. Verification

Phase 1: `ProductionProgress` visibly ticks in the Entities window; output slot count increments on cycle complete. Phase 2: seed only PrepTable inputs → FleshAutomaton appears at OutputBay with no manual intervention. Phase 3: HUD resource count increments. Soak: 10 cycles, no slot leak (input counts hit zero exactly).

## Open decisions (collected)

- [ ] §2 — v1 transport: grid-adjacency chaining (recommended) vs worker carry.
- [ ] §9.4 — include worker staffing in this plan or split to its own once revival/orders stabilize.
