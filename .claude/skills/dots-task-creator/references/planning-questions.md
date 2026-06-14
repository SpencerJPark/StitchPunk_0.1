# Planning Questions — DOTS system specs

The category checklist that drives the batched Q&A (step 4 of the skill). Not every category applies to every system — skip the irrelevant ones. Ask **foundational architecture first**, then work down. For each question: recommend a default (list it first, label "(Recommended)"), ask only on genuine forks, and record anything deferred as a `← DECISION` marker.

Aim for ≤4 questions per `AskUserQuestion` round; run as many rounds as needed.

---

## 1. Entry & activation *(ask first — it defines the system's shape)*
- How is the system entered? **(a)** an `IEnableableComponent` "request" on the entity it acts on (request model, like `AttackRequest`/`PathRequest`), **(b)** another system spawns a one-frame **signal entity** carrying a data component (`LoggingSystem` pattern → read all → act → `DestroyEntity(query)`), or **(c)** a singleton the system polls.
- Which **existing** request components / tags can be reused instead of adding new ones?
- Is the effect one-shot (fire-and-forget) or persistent (lives with a component on an entity)? Both?
- Does activation follow an entity (moves with it) or fire at a fixed world position?

## 2. Architecture & system-group placement
- Which `SystemGroup` does it live in, and what's the ordering vs neighbours (`UpdateBefore`/`UpdateAfter`)? (See `SystemGroups.cs`.)
- Pure ECS, or an **ECS-decides / MonoBehaviour-bridges** split (needed whenever managed Unity objects are involved — audio, UI, cameras)? If split, what crosses the boundary (a singleton list, an entity→object map)?
- Burst + `IJobEntity` / `ScheduleParallel` (default per project rules), or main-thread for structural changes?
- Does it need an ECB, and from which `EntityCommandBufferSystem`?

## 3. Data model
- Is there authored config data? If so → **SO→Blob library** (`FooSO` → `FooLibrarySO` → `FooLibraryBlob`, enum-indexed, baked in `PostBakingSystemGroup`). Use `dots-blob-library`.
- What is config (baked, immutable) vs runtime context (per-entity, mutable)?
- Any managed references (AudioClip, Sprite, prefab) that **cannot** live in a Blob and need a parallel managed registry keyed by enum?
- New enums needed (e.g. `FooType`)? Where do they live (`_Scripts/Data/Enums/`)?

## 4. Authoring
- What new MonoBehaviour + `Baker` pairs are needed (use `dots-authoring-baker`)? What `TransformUsageFlags`?
- Cross-entity baking (touch child entities in `PostBakingSystemGroup`)?
- Which existing prefabs/scenes need the new authoring wired in?

## 5. AI integration *(only if the system drives unit behaviour)*
- New `ActionType` / `MotivationType`? New awareness system emitting `ActionOption`/`UtilityActions` entries? At what priority tier?
- New `BehaviorCommand` for the execution sequence, or reuse existing commands?
- Does it need an interrupt (`ActionInterruptRequest`) for urgent reactions? Use `dots-unit-ai`.

## 6. Scope & phasing
- What's in **v1** vs explicitly deferred? (Keep the demo in scope; cut the rest.)
- What's the minimum end-to-end slice that proves the architecture?
- Suggested build phases (data layer → one path end-to-end → breadth → polish).

## 7. Performance & scale
- Expected entity counts (horde sizes)? Any budget/cap (voices, instances, draw calls)?
- Dedup / pooling / virtualization needed at scale?
- Profiling target (e.g. "200+ at 60fps") and when to profile.

## 8. Integration points
- Which existing systems/components does it read or write — animation, combat (`AttackRequest`/`ThreatEntry`), save (`GameSettings`/DTO), narrative, camera, items, movement?
- Does it extend a shared asset (e.g. `AnimationClipSO`, `GameSettings`) — and does that ripple into save/baking?
- Does it need a new `SystemGroup` declared in `SystemGroups.cs`?

## 9. Verification
- How is it tested end-to-end? (Usually: Play `DOTSTestScene`, trigger via a debug key / placed authoring, inspect components in the Entities window.)
- What's the observable success signal per build phase?
- What can only Spencer verify in the Editor (→ a future review/verify handoff)?

## 10. Open decisions
- Which sub-choices should be left to Spencer as `← DECISION` markers rather than decided now? (Tuning values, detection thresholds, exact counts, naming.)
- Collect them all into the spec's closing Open-Decisions checklist.
