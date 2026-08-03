---
tags: [task, claude, code, architecture, review]
related: "[[Code_Audit_2026-07]], [[Memories/Code/Systems]], [[Memories/Code/RULES]], [[Memories/Code/Skills]]"
created: 2026-07-02
status: active
---





# Structural Review — System Groups as Plug-and-Play Features (July 2026)

Scope: **existing code and structure only** — how close the codebase is to the stated goal of *"system groups as isolated features that upstream orchestrating groups call into, ultimately plug-and-play."* This is the companion to [[Code_Audit_2026-07]] (which is a feature/roadmap backlog); nothing here adds gameplay. Written as a senior-consultant pass: what's genuinely good, where the structure lies to you, and the shortest path to real pluggability.

**Inventory at time of review:** 129 system files, 36 declared groups (all in `SystemGroups.cs`), 26 request/event contract components, 10 asmdefs, 73/129 systems individually gated on `GameSceneTag`.

---

## Verdict up front

You are **already building the right architecture** — you just haven't made it *enforceable* yet. The three pillars of plug-and-play ECS are all present in embryo:

1. **A contract layer** — 26 enableable request/event components (`PathRequest`, `AttackRequest`, `HealRequest`, `PickupRequest`, `SocialInvite`, `SaveRequest`, …) plus the `DamageBus` NativeQueue. `BehaviorExecutionSystem`'s own header says it: *"Requests are the API boundary to downstream systems — this system enables them; downstream systems do the work."* That is exactly the pattern. Most Unity DOTS projects never get here.
2. **A single ordering manifest** — `SystemGroups.cs` is one readable file with intent comments. Group nesting (Coordinator→Routing→Follower→Execution inside Movement; Execution→Reaction inside Combat) shows real phase thinking.
3. **Data-driven behavior** — the BehaviorSO → blob → interpreter split means features are *content*, not code, at the top of the stack.

What's missing is the layer that keeps this true as the project grows: **nothing enforces any of it**. Folder names drift from group names, dead systems sit commented-out in live folders, gating is copy-pasted per-system, and the only thing stopping a future system from reaching directly into another feature's internals is discipline. The recommendations below are ~80% enforcement and hygiene, ~20% new structure. None of them are rewrites.

---

## Finding 1 — The folder tree lies about the group tree (highest priority)

Your stated mental model is "folder = feature = group." The filesystem currently breaks that in nine places:

| What the disk says | What the code says |
|---|---|
| `UtilityAISystemGroup/UtilityDecisionSystemGroup/` (ConsiderationScoring, WinnerSelection) | Both systems update in `ActionSelectionSystemGroup` — a child of **StateMachineSystemGroup**. There is no `UtilityDecisionSystemGroup` type at all. |
| `UtilityAISystemGroup/UtilityAwarenessSystemGroup/` | Group is named `AIAwarenessSystemGroup` |
| `UtilityAISystemGroup/UtilityMotivationSystemGroup/` | Group is named `AIMotivationSystemGroup` |
| `UtilityAISystemGroup/UtilityPerceptionSystemGroup/` | **Empty folder**, no such group |
| `MinionActionSelectionSystemGroup/MinionOrderSystemGroup/` | **Empty folder**, no such group |
| `EffectsSystemGroup/` (holds only `EffectsDirectory.cs`) | No `EffectsSystemGroup` type declared |
| `HealthSystemGroup/Ragdoll2DSystem.cs` | Updates in raw `LateSimulationSystemGroup` |
| `PresentationSystemGroup/OrderMarkerSystem.cs`, `SelectedVisualSystem.cs` | Update in raw `LateSimulationSystemGroup` (siblings use Unity's actual `PresentationSystemGroup`) |
| `CombatSystemGroup/DamageBusSystem.cs` | Updates in `GameManagerSystemGroup` OrderFirst (documented and correct — but it lives in the wrong folder) |

Also: `SpawnSystemGroup/SpawnSystemGroup/` double-nesting, and `UnitPoolReturnSystem.cs` sits at the `SpawnSystemGroup/` root while updating in `DespawnSystemGroup` (whose folder exists one level down).

Why this matters for your goal: "plug-and-play" starts with **the folder being the unit of plugging**. If a feature's systems are scattered or mislabeled, you can't reason about what moves with it, what its contract is, or what breaks when you disable it. Every one of these mismatches is a place where a future session (yours or mine) will edit the wrong layer.

**Fix (S, mechanical):**
- Rename the two awareness/motivation folders to match the type names — or rename the types to `Utility*` to match the folders. Pick one; I'd rename the *types* to `UtilityAwarenessSystemGroup` / `UtilityMotivationSystemGroup` since the folder names carry the better taxonomy and CLAUDE.md already speaks "UtilityAI."
- Move `ConsiderationScoringSystem` + `WinnerSelectionSystem` to `StateMachineSystemGroup/ActionSelectionSystemGroup/` — *or*, better, re-parent `ActionSelectionSystemGroup` under `UtilityAISystemGroup` if you consider scoring+winner-selection part of the decision feature (I think you do — CLAUDE.md describes them as the decision layer). Decide where the decision/execution seam actually is, then make disk and code agree.
- Delete the two empty folders. Rename `EffectsSystemGroup/` to `Effects/` (or declare the group if effects systems are coming).
- Move `DamageBusSystem.cs` — see Finding 4.

**Fix (M, the part that makes it stick) — a conformance test.** This is the single highest-leverage change in this document. One EditMode fixture that reflects over `StitchPunk.Systems` and fails CI-style in the Test Runner whenever the structure drifts:

```csharp
// Assets/_Scripts/Tests/SystemPlacementConformanceTests.cs (EditMode)
// For every ISystem/SystemBase type in the StitchPunk.Systems assembly:
//   1. It has an [UpdateInGroup] attribute (no silent default-SimulationSystemGroup registration).
//   2. Its source file path contains the folder chain of its group ancestry
//      (map group type -> expected folder via one dictionary, or by name convention).
//   3. Every ComponentSystemGroup subclass is declared in SystemGroups.cs.
// Source paths come from a generated map: an editor script that scans Systems/**.cs
// for "partial struct XxxSystem" and records file -> type name. ~100 lines total.
```

Point 1 alone earns its keep: today, if someone adds a system and forgets `[UpdateInGroup]`, Unity silently auto-creates it in `SimulationSystemGroup` at an arbitrary position — the exact class of silent failure `Gotchas.md` exists to catch. (I verified your 8 commented-out systems are fully commented and *not* auto-created — but the hazard is one forgotten attribute away.)

---

## Finding 2 — Groups are ordering containers, not features (the actual plug-and-play gap)

Every one of your 36 `ComponentSystemGroup` subclasses is an empty body: `{ }`. They order children — nothing else. Meanwhile 73 of 129 systems each independently call `state.RequireForUpdate<GameSceneTag>()`. That's the same gate copy-pasted 73 times, and 56 systems *don't* have it (most legitimately — baking, presentation — but nothing distinguishes "legitimately ungated" from "forgot").

A `ComponentSystemGroup` is a `ComponentSystemBase`: it can carry `RequireForUpdate` itself, and when a group's requirements aren't met, **none of its children update**. This is the ECS-native feature toggle, and it's sitting unused:

```csharp
public partial class CombatSystemGroup : ComponentSystemGroup
{
    protected override void OnCreate()
    {
        base.OnCreate();
        RequireForUpdate<GameSceneTag>();
        RequireForUpdate<CombatFeature>();   // the plug
    }
}
```

`CombatFeature` is an empty singleton tag baked by a trivial `FeatureConfigAuthoring` in the subscene (one MonoBehaviour with bool checkboxes, one baker adding one tag per enabled feature). Now:

- **A scene decides which features exist.** The DOTS test sandbox can run Movement + Health with no Combat, no Social, no Save. Removing the singleton unplugs the feature — no code, no recompile, no `#if`.
- **Per-system `GameSceneTag` boilerplate collapses** to the ~10 top-level groups. Individual systems keep only *data* requirements (`RequireForUpdate<BehaviorLibrary>` etc.), which is what that API is for.
- **The gate is auditable**: "is this feature gated?" becomes a one-file question in `SystemGroups.cs` instead of a 129-file grep.

Two implementation notes: (a) `SystemGroups.cs` stops being attribute-only — the group bodies gain an `OnCreate`; keep them in the same file, that file is your manifest. (b) Systems that must run even when the feature idles (e.g. `DamageBusSystem`'s queue reset) stay outside the gated group — which it already is.

**The second half of pluggability is test worlds.** The payoff of isolated features is being able to stand one up alone. The existing EditMode tests cover pure math; the missing layer (already noted as absent in CLAUDE.md) is PlayMode/World tests that create a world containing *one feature group + its contract components* and drive it via requests: enable a `PathRequest`, tick `MovementSystemGroup`, assert position changed. If a feature can't be tested this way, it isn't isolated — the test *is* the definition of plug-and-play. Start with Movement (fewest upstream dependencies), then Health (drive it purely via the DamageBus).

---

## Finding 3 — The contract layer is good; protect it and name it

The request components plus `DamageBus` form a real API surface, and the direction of flow is consistent: decision layer → requests → execution features → events (damage, sound, threat) → reaction features. Observations:

- **`BehaviorExecutionSystem` carries 14 lookups**, of which four are Item-domain internals (`EquipBy`, `AttachedTo`, `AttachItemRequest`, `UnitEquip`). The stated rule in its own header — "this system enables requests; downstream systems do the work" — is bent here: the interpreter does equip/attach bookkeeping inline instead of only enabling `PickupRequest`/`AttachItemRequest` and letting `ItemSystemGroup` resolve ownership. Not urgent, but it's the one place the orchestrator reaches *into* a feature rather than *at* its API. Rule of thumb worth writing into RULES.md: **a behavior command that needs 3+ foreign-domain lookups should become a request handled by that domain.**
- **The contract has no home.** The 26 request/event structs are scattered through `Components/*` subfolders alongside feature-internal state. The boundary between "this component is another feature's API" and "this component is Movement's private business" exists only in your head. Cheapest fix: a `Components/Contracts/` folder (or just a `Contracts.md` vault index listing *request → producer systems → consumer system*, ~30 lines, generatable by grep). If you ever want compile-enforced boundaries (see Finding 5), this folder is the thing that becomes its own asmdef — carving it out now costs nothing.
- **Delete `SocialValidationRequest`** — it's self-annotated "legacy pre-migration — unused, removal candidate." Contract surfaces must not carry dead entries; every reader has to rule it out.

---

## Finding 4 — GameManagerSystemGroup is quietly becoming your "world services" layer — make that official

Its current residents: `FactionRegistrySystem`, `InteractionSpatialHashSystem`, `WaypointRegistrationSystem`, `HordeSystem`, `FloatingWorldOriginSystem`, plus `DamageBusSystem` (filed under Combat). That's not a game manager — it's **frame-setup infrastructure**: registries, spatial hashes, buses, origin shifting. Every feature depends on it; it depends on no feature. That's a real and correct layer — it's just unnamed, so things land there ad hoc.

**Recommendation (S):** rename the concept, not necessarily the class — a header comment in `SystemGroups.cs` declaring its charter ("shared world services: registries, buses, spatial structures; runs OrderFirst; features may read its singletons, it never reads feature state") and move `DamageBusSystem.cs` into `Systems/GameManagerSystemGroup/` (or a `Buses/` subfolder if more buses are coming — the DamageBus pattern is good enough that sound or threat may want one). If you'd rather keep the file with Combat, the conformance test from Finding 1 needs an explicit exemption list — prefer moving the file.

---

## Finding 5 — Asmdef strategy: don't split Systems, do (eventually) split Contracts

The tempting "senior" move is per-feature assemblies (`StitchPunk.Systems.Combat`, `.Movement`, …) so boundaries are compile-enforced. **My advice: don't.** Reasons specific to this project:

- All features share `StitchPunk.Components` — the porous layer is *data*, not systems. Splitting Systems buys little while Components stays monolithic.
- DOTS source generators multiply per-assembly compile cost; 15 tiny system assemblies will make your edit-compile-playtest loop noticeably worse, and that loop is your primary verification gate.
- You're a solo dev with a conformance-test option (Finding 1) that gets 90% of the enforcement for 5% of the friction.

The split that *would* pay off later: `StitchPunk.Contracts` (request/event components + shared enums) separate from `StitchPunk.Components` (feature state). Then a future feature assembly referencing only Contracts physically cannot touch another feature's internals. Do the folder-level split now (Finding 3), the asmdef split only if/when a second person joins the codebase.

Everything else about the assembly layout is right: Components has no Systems reference, data flows one way, `autoReferenced` everywhere is fine at this scale.

---

## Finding 6 — Dead code is parked in live folders, and one doc lies

Eight systems are fully commented out in place: `ProductionSystem`, `FactoryLibraryBakingSystem`, `MinionAttackOrderSystem`, `MinionOrderExecutionSystem`, `MinionSelfDefenceSystem`, `MinionCommandSystem`, `OutlineSystem`, `OutlineLayerUpdateSystem`. RULES.md already has the answer — `Core/Unused/` — and it's not being followed. Worse:

- **CLAUDE.md states "Factory System Phase 1 is built (ECS data layer + production loop)"** while both `ProductionSystem` and `FactoryLibraryBakingSystem` are entirely commented out. Any session that trusts the doc will build Phase 2 on a disabled Phase 1. Fix the doc or re-enable the systems, whichever reflects intent.
- The three Minion* systems overlap with the *live* minion-order path (`MinionActionSelectionSystem`) — commented-out near-duplicates of active logic are the most expensive kind of dead code, because every reader must diff them mentally.

**Fix (S):** move all eight to `Core/Unused/` (or delete — git remembers), and add a line to RULES.md: *commented-out systems never stay in `Systems/`*. The Finding-1 conformance test can enforce this too (any `.cs` under `Systems/` must contain at least one live system or be in an allowlist like `SystemGroups.cs`, `EffectsDirectory.cs`).

Naming hygiene while you're there (your own rule is "names read like documentation"): `FlowFeildSystem.cs` (Feild→Field), `Authoring/Registary/` (→Registry), the `Enviroment` stub (→Environment). Typos in names defeat grep — I initially missed FlowField systems searching for "Field."

---

## Finding 7 — Ordering is correct today but unverified

The top-level pipeline is held together by `UpdateBefore/After` chains across 14 sibling groups. It resolves correctly now, but a missing link fails *silently* — Unity just picks an arbitrary stable order, and you'd discover it as a one-frame-late bug in play testing (the worst kind to bisect). Two cheap guards:

1. **A group-order test** (PlayMode or EditMode with a real `World`): create the default world, walk `SimulationSystemGroup`'s update list, assert the sequence matches the documented pipeline (`GameManager → Player → UtilityAI → MinionActionSelection → StateMachine → Item → Movement → Buildings → Combat → Health → Design → Animation`, then the Late chain). ~40 lines, catches every future mis-attribution the moment it happens.
2. `MinionActionSelectionSystemGroup` declares `UpdateAfter(UtilityAI)` + `UpdateBefore(StateMachine)` but CLAUDE.md's pipeline diagram puts it between UtilityAI and StateMachine — consistent today; the test makes the diagram executable instead of aspirational.

---

## Skills & workflow changes

The skill suite is a real asset — it's why rule violations are near zero. Three additions align it with this review:

1. **New skill: `dots-feature-group`** — scaffolds a complete feature: group declaration in `SystemGroups.cs` (with gate `OnCreate` per Finding 2), matching `Systems/<Name>SystemGroup/` folder, `FeatureConfigAuthoring` checkbox + singleton tag, a stub contract entry in `Contracts.md`, and a line in the conformance test's group map. This makes "add a feature" a one-command operation and guarantees new features are born pluggable.
2. **Extend `dots-system-scaffold`** — derive the target folder *from* the chosen `[UpdateInGroup]` (refuse mismatches), and stop emitting per-system `RequireForUpdate<GameSceneTag>` once group-level gating lands (emit data requirements only).
3. **Extend `dots-test`** — add a "world fixture" template: minimal `World` + one feature group + contract components, the Finding-2 test pattern. The first two instances (Movement, Health-via-DamageBus) become the reference implementations.

Vault: add `Memories/Code/Contracts.md` (the request → producer → consumer index) to the CLAUDE.md folder map, and correct the Factory status line.

---

## Prioritized action list

Status column updated during the 2026-07-02 execution pass. ⚠ Everything awaits one compile + rebake + Test Runner pass in the Editor (the Unity MCP connection was down during execution — nothing below has been compile-verified).

| # | Action | Size | Status |
|---|---|---|---|
| 1 | Folder/type renames + moves from Finding 1; delete empty folders | S | ✅ Done — `AIAwareness/AIMotivation` → `UtilityAwareness/UtilityMotivation` types; decision systems → `StateMachineSystemGroup/ActionSelectionSystemGroup/`; interpreter → `ActionExecutionSystemGroup/`; Spawn folders flattened to siblings; empty/vestigial folders deleted |
| 2 | Move/delete the 8 commented-out systems; fix CLAUDE.md factory status | S | ✅ Done — all 8 in `Core/Unused/`; CLAUDE.md + Systems.md now say PARKED |
| 3 | `SystemPlacementConformanceTests` (placement + mandatory `[UpdateInGroup]` + groups-in-manifest + no corpses) | M | ✅ Written (`_Scripts/Tests/`) — needs first Test Runner run |
| 4 | Group-level `GameSceneTag` gating + `FeatureConfigAuthoring` singleton tags; strip per-system boilerplate | M | ◐ Partial — `GameSceneSystemGroup` base gates all 16 top-level feature groups; `FeatureTags.cs` + `FeatureConfigAuthoring` created but `RequireForUpdate<XFeature>` NOT wired (needs the authoring in scenes first — see plan); per-system requires left in place (harmless; strip later) |
| 5 | Group-order pipeline test | S | ✅ Written (`SystemGroupOrderTests.cs`) — also fixed the invalid cross-hierarchy `UpdateBefore(PlayerInputSystemGroup)` on GameManagerSystemGroup it would have caught |
| 6 | `Contracts.md` index; delete dead contracts | S | ✅ Done — `_Vault/Memories/Code/Contracts.md`; deleted `SocialValidationRequest` **and four more dead contracts found during indexing** (`SpawnItemRequest`, `DespawnItemRequest`, `UseItemRequest`, `DropRequest`, `ReleaseRequest`); folder split deferred with the asmdef item |
| 7 | First two single-feature World tests (Movement, Health) | M | ⏳ Planned — PlayMode World fixtures need live compile iteration; spec'd in Tasks/Plans |
| 8 | `dots-feature-group` skill + scaffold-skill updates | M | ✅ Skill created + Skills.md indexed; dots-system-scaffold/dots-test updates folded into the new rules in RULES.md |
| 9 | DamageBus file move + GameManagerSystemGroup charter comment | S | ✅ Done — file in `GameManagerSystemGroup/`, charter in SystemGroups.cs + Systems.md |
| 10 | (Deferred) `StitchPunk.Contracts` asmdef split | L | Deferred — only when a second contributor appears |

Items 1–5 are roughly two sessions and deliver most of the value. I'd sequence them exactly as numbered: hygiene → enforcement → gating — enforcement before gating so the gating refactor itself is checked by the new tests.

---

## What I deliberately did *not* recommend

- **Per-feature system asmdefs** — compile-loop cost outweighs enforcement value at solo scale (Finding 5).
- **An event-bus framework / generalizing DamageBus preemptively** — the enableable-request pattern covers 25 of 26 contracts fine; build a second bus when a second producer-fan-in problem actually appears (sound is the likely candidate).
- **Renaming `GameManagerSystemGroup`** — charter comment first; rename only if it keeps accreting.
- **Touching the interpreter's design** — 627 lines and 14 lookups is at the ceiling but not over it; the Item-lookup extraction (Finding 3) is the only cut worth making, and only when you're next in that file.
