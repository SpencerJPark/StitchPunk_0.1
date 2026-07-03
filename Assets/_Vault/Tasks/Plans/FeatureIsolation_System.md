# Feature Isolation Follow-ups — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Raw source:** [`../Claude/Structural_Review_2026-07.md`](../Claude/Structural_Review_2026-07.md) — the items the 2026-07-02 execution pass deliberately deferred (action-list rows 4-partial and 7). Prerequisite: that pass's compile + Test Runner verification is green.

---

**Skills Needed:**
- `dots-test` — PlayMode World fixtures (§3) — needs the PlayMode assembly set up per the skill's note (separate folder + asmdef, no `includePlatforms: Editor`)

---

## 1. Purpose & v1 scope

Finish converting system groups from "ordered containers" into **pluggable features**. The mechanism shipped in the structural pass (`GameSceneSystemGroup` gate, `FeatureTags.cs`, `FeatureConfigAuthoring`); this plan wires it into scenes, removes the now-redundant boilerplate, and builds the proof: single-feature World tests.

## 2. Work item A — wire the feature plugs

1. Add a `FeatureConfigAuthoring` GameObject to the DOTSTestScene subscene (next to the `GameSceneTag` prefab — `Assets/Prefabs/SceneTags/`), all four checkboxes on. Consider making it a prefab beside GameSceneTag so future subscenes get both together. **Editor step — Spencer.**
2. Add `OnCreate` overrides in `SystemGroups.cs`: `CombatSystemGroup` requires `CombatFeature`, `BuildingsSystemGroup` → `BuildingsFeature`, `SoundSystemGroup` → `SoundFeature`, `SaveSystemGroup` → `SaveFeature` (each also keeps the base `GameSceneTag` require via `base.OnCreate()`).
3. Play-verify: all features run; untick Combat → attacks stop but movement/AI/animation continue; retick + rebake → restored.
   **Ordering constraint: step 1 must land (baked) before step 2 compiles into a play session, or those four features go dark.** Do them in one sitting.

**← DECISION:** which groups get plugs beyond the initial four — UtilityAI? Item? (Movement/Health/Animation/StateMachine are spine, not plugs.) *Recommendation: stop at four until a scene actually wants to unplug something else.*

## 3. Work item B — single-feature World tests (the proof of isolation)

PlayMode assembly `Assets/_Scripts/Tests/PlayMode/` (`StitchPunk.Tests.PlayMode.asmdef`). Two reference fixtures, then the pattern is reusable:

- **Movement:** `World` containing `MovementSystemGroup` members only + minimal singletons (grid config). Spawn one entity with `LocalTransform` + movement components, enable `PathRequest` to a point, pump `world.Update()` N frames, assert position converged. Drives the feature purely through its [[Contracts]] entry.
- **Health via DamageBus:** `World` with `DamageBusSystem` + `HealthSystemGroup` members. Create a unit-ish entity (Health, Dead disabled), enqueue a lethal `DamageEvent` into `DamageBus.raw`, update, assert `Dead` enabled + killing-blow fields captured. Proves the bus contract stands alone.

Expect friction: these fixtures will surface hidden cross-feature `RequireForUpdate`s and singleton dependencies — **that's the point**; each one found is either a missing contract row or a coupling to break. Log findings in the structural review doc.

**← DECISION:** hand-assemble the world per fixture (explicit, verbose) vs a `TestWorldBuilder` util (`AddFeature(typeof(MovementSystemGroup))` reflection helper). *Recommendation: hand-assemble the first two, extract the builder from what repeats.*

## 4. Work item C — strip redundant per-system gating

Once A is verified: remove the ~73 per-system `state.RequireForUpdate<GameSceneTag>()` lines (group gate covers them). Mechanical sed + compile; do it as one commit so it's trivially revertable. Update the `dots-system-scaffold` skill template to stop emitting the line (its SKILL.md still shows it — the RULES.md section already says data-requirements-only).

## 5. Work item D *(optional, deferred-by-default)* — `Components/Contracts/` folder

Physically move the request/event structs indexed in [[Contracts]] into `Components/Contracts/` (pure file moves within the same asmdef — no code change; several structs share files with internal components and need splitting out). Prereq for the someday `StitchPunk.Contracts` asmdef. *Do only when touching those files anyway.*

## 9. Build phases

1. A (scene wiring + group requires + play-verify) — one sitting.
2. B Movement fixture → B Health fixture → extract builder if warranted.
3. C strip + skill template update.
4. D opportunistically.

## 10. Verification

A: untick-per-feature play matrix (4 features × on/off). B: PlayMode Test Runner green; each fixture < 1s. C: play-smoke identical to pre-strip (gating semantics unchanged — group covers all stripped requires); conformance tests still green.

## Open decisions (collected)

- [ ] §2 — plug set beyond Combat/Buildings/Sound/Save.
- [ ] §3 — hand-assembled worlds vs TestWorldBuilder (recommend: extract after two).
- [ ] §5 — do work item D now vs opportunistically (recommend opportunistic).
