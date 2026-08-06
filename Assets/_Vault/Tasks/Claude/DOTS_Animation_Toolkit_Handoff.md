# DOTS Animation Toolkit — session handoff

**Written:** 2026-08-05 (C4.7 closed and gated)
**State:** Phases done: A, B, C0, C1, C2, C3. **C4 in progress — C4.1 through C4.7 all done and verified. C4.8 (LOD) is next.**

**Baseline, verified through the MCP 2026-08-05 at commit `078ce31` (pushed):** Console clean of `error CS` / `BC`; **210 EditMode + 149 PlayMode, all passing, each in its real mode.** Project-wide EditMode is 266 — the extra 61 belong to the host game's own `StitchPunk.Tests`, so pass `assembly_names` to `run_tests` when comparing against these numbers. Discrimination for C4.3–C4.7 verified by fourteen mutation runs (tables in the C4 plan). **Nothing is owed except the C4.8 ordering edge below.**

---

## Start here

1. `Docs/AnimationToolkit/Phase_C4_Plan.md` — the 14 pieces, the nine phases, the traps carried in from C3, the test-integrity standard, and a per-phase record of what each build step actually found. **Read this before the architecture doc.**
2. `Docs/AnimationToolkit/Phase_B_Architecture.md` — the normative spec. **111 KB — never read whole.** Grep headings, then Read with offset/limit. §5 is C4's territory; §5.10 is C4.8's.
3. `Docs/AnimationToolkit/Phase_C3_Gate4.md` — the last gate: verdict, the 10 blocking items, the Resolution table, and the "verified clean — do not re-litigate" list. Read that list before re-auditing anything in C3.

Earlier gate docs (`Phase_C3_Review.md`, `Phase_C3_ReReview.md`, `Phase_C3_Gate3_Incomplete.md`) are history, superseded by Gate 4.

---

## Environment — what is true as of 2026-08-05

**The Unity MCP is UP and was used for the whole of C4.7.** One instance: `Stitch_Punk@852da23e19ef0320`.

**The MCP tools may not be in your tool list at session start.** This session began with no `mcp__UnityMCP__*` tools visible and `ToolSearch("select:mcp__UnityMCP__refresh_unity,…")` returning *no matching deferred tools*. They appeared as deferred tools only after the server was probed directly over HTTP. If that happens again:

- The server speaks MCP over streamable HTTP at `http://127.0.0.1:8080/mcp`. `POST` an `initialize` JSON-RPC call, keep the `Mcp-Session-Id` response header, `POST` `notifications/initialized`, then `POST` the real call. Responses come back SSE-framed (`event: message\ndata: {…}`).
- A working PowerShell driver was written this session; recreate it in the scratchpad if needed — it is three `Invoke-WebRequest` calls.
- Once the tools *do* appear in a `<system-reminder>`, load them properly with `ToolSearch("select:mcp__UnityMCP__refresh_unity,mcp__UnityMCP__read_console,mcp__UnityMCP__run_tests,mcp__UnityMCP__get_test_job,ReadMcpResourceTool")` and use them instead of raw HTTP.

**The compile gate, in order:**

1. `refresh_unity(mode="force", scope="all", compile="request", wait_for_ready=false)`.
2. Poll `mcpforunity://editor/state` (via `ReadMcpResourceTool`) until `is_compiling: false`, `is_domain_reload_pending: false`, and `last_domain_reload_after_unix_ms` is **newer than the edit you just made**.
3. `read_console(types=["error"])`.
4. `run_tests` + `get_test_job(wait_timeout=120)`.

**Gate gotchas, all hit this session:**

- **`refresh_unity` returning `success` is not a readiness signal.** Calling `run_tests` straight after it wedged the Editor mid-play-mode-transition in an earlier session (job died with `total: null`), needed `manage_editor action=stop` to recover, and left an `Assets/InitTestScene<guid>.unity` behind.
- **`blocking_reasons: ["stale_status"]` is snapshot freshness only.** It sat true for two of the six mutation runs while every substantive field read idle and the tests ran fine. Do not wait on `ready_for_tools` when `stale_status` is the only reason.
- **`refresh_unity` sometimes reports `"Refresh recovered after Unity disconnect/retry"` or times out at 60 s** and the compile still happens. Judge by `editor/state`, not by the tool's message.
- **If you script the poll in PowerShell, the resource text is JSON-escaped inside the outer JSON** — it reads `\"is_compiling\":false`, so a naive `-match '"is_compiling":false'` never matches and the loop runs to its full timeout. `.Replace('\"','"')` first. This silently cost two 400-second waits.
- **Always check the discovered test count, not just pass/fail.** `resultState: "Passed"` with `total: 0` is what a vanished suite looks like, and it is how the C3 PlayMode defect survived a whole build step and three static reviewers.
- **`Logs/Editor.log` (project-relative) was the live log this session**, not the `%LOCALAPPDATA%` copy. Which one is live depends on how the Editor was launched — check `LastWriteTime` on both before trusting either. Only relevant as a fallback when the bridge is down.
- **Grep `Library/PackageCache/<pkg>@<hash>/` before calling any Unity API.** The two worst bugs this package shipped both came from recalling semantics instead of reading them.

---

## C4 progress

- ✅ **C4.1 skeleton** — four system groups, `ToolkitWorldControl`, `ConfigBootstrapSystem`, 12 tests.
- ✅ **C4.2 binding** — `RigBindingSystem` (§5.3), 7 tests, mutation-verified.
- ✅ **C4.3 playback core** — `AnimationCommandUtil`, `PlaybackQuery`, `CommandApplySystem`, `PlaybackTimeSystem`, `PlaybackLayer.advanceStartTime` (A27). Mutation-verified.
- ✅ **C4.4 events** — `EventEmissionSystem`. Building it exposed a defect in C4.3's queue promotion → **A30**.
- ✅ **C4.5 transform technique** — `TransformSampleSystem` + `TransformApplySystem`, 11 fixtures. Produced **A31** (owner-approved) and **A32**.
- ✅ **C4.6 flipbook** — `SpriteMaterialSystem`, 6 fixtures.
- ✅ **C4.7 VAT + bounds — closed 2026-08-05.** `VatMaterialSystem` (8 fixtures), `RenderBoundsUpdateSystem` (6 fixtures), 2 group-placement rows. **Mutation-verified: 13 mutations over six runs, each producing exactly the predicted failure and no others.**
- ⬜ **C4.8 LOD** — next. See the brief below.
- ⬜ **C4.9 acceptance + smoke scene** — **STOP HERE.** Its DoD needs the owner to confirm on-screen clip playback, which Claude cannot verify. The owner has asked to go through C4.9 together.

### What C4.7 settled

- **Amendment A33 — the PlayMode asmdef references `Unity.Entities.Graphics`.** `Unity.Rendering.RenderBounds` is defined there, not in `Unity.Entities`, and C4.7 was the first fixture in the package to read a `RenderBounds` back. Caught twice: once by the compiler, then again by `PackagingConformanceTests` when the asmdef was updated and §1.3 was not. **That second catch is the conformance test working as designed — if you touch any asmdef, update §1.3 and `PackagingConformanceTests.AsmdefExpectations` in the same commit.**
- **The `BoundsDirty` *disable* is finally pinned by mutation** — outstanding since C4.3, where the tag's *raising* was covered but its reset was not. Deleting `boundsDirtyEnabled.ValueRW = false` fails `TheDirtyTag_IsClearedByTheWrite`; swapping `[WithAll(BoundsDirty)]` for `[WithPresent(BoundsDirty)]` fails `AFrameThatOnlyAdvancesTime_LeavesBoundsUntouched`. Both defects produce *correct bounds at permanent cost*, so nothing but a test was ever going to find them.
- **A second non-discriminating fixture, caught before the gate rather than after.** `ABlendingLayer_KeepsBothClipsInTheUnion` originally made the *incoming* clip the large one — whose box swallows the outgoing clip's on every axis, so the expected union was the same number whether or not the crossfade source was folded in. Reversed so the outgoing clip is the large one.
- `VatMaterialSystem` iterates **parts**, not actor roots, reaching the layer buffer through `RigPartBinding.actorRoot` — `VatDriven` *is* the VAT archetype, and a torso and a cape on one actor may follow different layers.
- `_VatFrameB` defaults to `_VatFrameA` rather than 0: with `_VatBlend = 0` a correct shader ignores B, but a 0 there points at the first row of the whole texture, so any shader that lerps before testing the weight would snap the mesh to an unrelated clip's pose.

---

## C4.8 brief — LOD

The plan line reads "**C4.8 — LOD.** `AnimLodDistanceSystem`", but §8 M3's acceptance list (which C4.9 tests) includes *"LOD 2 mid-blend swap → blend completes on schedule (§5.10)"*. **The level *effects* therefore have to exist by the end of C4.8, or C4.9 has nothing to test.** Shipping only the writer would give the package a config flag that computes a number nothing reads.

**§5.10 as written:**

| Level | Effect |
|---|---|
| 0 | Full quality. |
| 1 | Effective sample rate halved (`rateHz / 2`, or a 30 Hz cap when rate = 0/uncapped). |
| 2 | Quarter rate; transform blending visually snapped (weights clamp to 0/1; blend **timers** still advance normally — so a LOD swap mid-blend rejoins the correct weight). |
| 3 | Pose frozen unless the layer's clip changes; VAT properties still update at quarter rate. |

### Four things to resolve before writing code

**1. `AnimLod` is opt-in and its absence is conformant (amendment A23).** `ActorBaker` adds it only when `ActorAuthoring.addDistanceLod` is set. So the sampling job cannot take it as an ordinary `in` parameter — that would silently exclude every actor without it, which is most of them. Reach it through a `[ReadOnly] ComponentLookup<AnimLod>` with `HasComponent` (absent = level 0), and **write a fixture where the actor has no `AnimLod` at all**, because "LOD code accidentally requires the optional component" is exactly the shape of defect this module keeps producing.

**2. What world position does the actor root carry?** `AnimLodDistanceSystem` needs one to compare against `AnimationToolkitCameraData.position`. Check `ActorBaker`'s `TransformUsageFlags` and the 13-component baseline in `DataContractTests` before assuming `LocalToWorld` is there. If it is not, that is an archetype question, not an implementation detail.

**3. Level 3's "frozen unless the layer's clip changes" has no existing signal — this is the real open question.** Options considered:
   - *Reuse `BoundsDirty`.* It is already on the archetype and is enabled on exactly Play / queue-promotion / Once-completion / blend-completion — a **superset** of "the clip changed", so it is safe (you re-sample more often than needed, never less). But it is cleared by `RenderBoundsUpdateSystem`, which sits `[UpdateAfter(TransformSampleSystem)]`, so reading it during sampling works *today* and couples two systems through a tag that means something else. A future reorder breaks level 3 silently.
   - *Store a per-actor last-sampled clip signature.* Honest and self-contained, but it is a new field on a §5.2 root component → an archetype change → **an amendment plus a `DataContractTests` row.**
   - **Recommendation: the stored signature, with an amendment.** The coupling option is the kind of shortcut this package has already paid for twice (A28, A30 were both "system A's output means something different by the time system B reads it").

**4. Level 2's blend snapping lives inside `ClipSampler.CompositeLayers`**, which §5.11 makes the single sampler shared with the editor preview path. Snapping the weight anywhere else is not possible without mutating the layer buffer, so this is a **signature change on `CompositeLayers`** (an added `bool snapBlendWeights`) that touches every caller and the EditMode fixtures. Budget for it; do not try to avoid it by special-casing in the system.

### Recorded debt C4.8 must discharge

**`TransformSampleSystem` carries no `[UpdateAfter(typeof(AnimLodDistanceSystem))]`** because that type did not exist. §5.1's diagram orders sampling after LOD. Add the attribute when the system lands, and add a row to `SystemGroupStructureTests` pinning it — an unpinned ordering edge is how a LOD level written *this* frame gets consumed *next* frame, which looks like nothing at all.

`AnimLodDistanceSystem` should be `OrderFirst` in the presentation group, or explicitly before `TransformSampleSystem`; it must be a no-op when `AnimationToolkitConfig.distanceLodEnabled` is false (the default) and when `AnimationToolkitCameraData` is absent. `ConfigBootstrapSystem`'s defaults are already pinned by three fixtures — ascending non-zero `lodDistancesSq`, LOD disabled, 0 Hz sampling — so do not change them without reading `SystemGroupStructureTests`.

---

## Standing rules for this package

### Hard code rules (from `CLAUDE.md` and owner memory — these override defaults)

- Never `var`; never single-letter names; explicit types everywhere; names read like documentation.
- Never `.Run()` a job — `.Schedule()` / `.ScheduleParallel()` assigned to `state.Dependency`.
- `[ReadOnly]` from `Unity.Collections`, never `Unity.Entities`.
- Prefer `ISystem` + `[BurstCompile]`; no managed allocations in Burst jobs.
- Burst log strings: only `G/g/D/d/X/x` specifiers (BC1343); no `+` concatenation (BC1016). `FixedStringNBytes` interpolation **is** supported (Burst 1.8.29).
- **An `EnabledRefRW<T>` parameter enrols `T` in the query as an `All` (enabled-filtered) component.** If the job ever turns the bit **on**, that component needs `[WithPresent(typeof(T))]`. Recorded in `_Vault/Memories/Code/Gotchas.md`. `BoundsDirty`, `AnimEventsPending` and `AnimVisible` have all hit this.
- Reading a `ref`-returning property through an `in` parameter compiles into a defensive copy. The idiom used throughout the package is two locals first:
  ```csharp
  BlobAssetReference<ClipRegistryBlob> registryReference = clipRegistry.Value;
  ref ClipRegistryBlob registry = ref registryReference.Value;
  ```

### Test-integrity standard — the whole reason this package is trustworthy

**Every fixture's doc comment names the mutation it catches, and every mutation is actually compiled and run.** Four distinct shapes of "a test that passes under both the correct and the broken implementation" have now been found here:

1. **The fixture seeds state the way production seeds it.** Three C4.6 sprite fixtures seeded the shader properties from the rest pose — exactly as the baker does — and would have passed against a system that published nothing. Scribble sentinels first (`-999`, `-777`).
2. **The fixture picks the identity value for the thing the code branches on.** Every `LayerCompositionTests` fixture used an origin rest pose with unit scale, where `rest + key` and `key` are the same number — the entire `Override` semantics were untested while looking thoroughly tested (A31).
3. **The expected value is the same under both branches.** C4.7's blend-union fixture made the incoming clip the large one, so the outgoing clip's contribution could not change the answer.
4. **Batched mutations mask each other.** Removing C4.6's property writes *and* the `AnimVisible` gate together left the invisibility fixture passing, because with no write its own sentinel survived. **When a predicted failure does not appear, suspect the batch before suspecting the test.**

### Process

- Modules **C0–C8** in dependency order, each gated by an adversarial reviewer producing PASS/FAIL. **Gates are launched only when the owner asks.**
- **Commit and push to `main` whenever it makes sense — do not wait to be asked** (owner, 2026-08-02). What has not changed: **stage paths explicitly, never `git add -A`.** The working tree carries substantial unrelated host-game shader work (Painterly graphs, colour ramps, `Assets/Shaders/`) that must never ride along. Stage `Packages/com.stitchpunk.dotsanimationtoolkit`, `Docs/AnimationToolkit`, and this file by name.
- The owner delegates architecture and process calls (stated 2026-08-01) — decide, record the decision with its reasoning and an explicit "what to revert" note, and keep moving. A spec/reality conflict still gets a **written amendment**, never a silent doc edit: that discipline is what three failed gates bought.
- If a gate is needed: three narrow agents in parallel, one lens each (spec conformance / test integrity / code correctness), each appending to its own scratchpad file **as it goes**, results copied into `Docs/AnimationToolkit/` before the session ends. Two monolithic reviewers were killed by a watchdog and a third died on a usage limit. **Then run the suite** — Gate 4's most serious finding was invisible to all three readers and took ninety seconds of execution to surface.

---

## Lessons this package keeps re-teaching

1. **Closure is a property of the code, not of the note saying the code changed.** Verify against the shipped tree — never the CHANGELOG, a review doc's own closure table, or a previous session's summary.
2. **Reading the diff is not enough either. Run the thing.**
3. **A defect is usually invisible from inside the system that contains it.** A28, A30 and A31 were all found by writing the *consumer*, not by re-reading the producer. When starting a phase, write down what the phase after it will need from you.
4. **An amendment can be self-defeating.** A17 was well-reasoned, owner-approved, and its implementation produced the exact outcome it rejected. Check what an amendment *does*, not only what it argues.
5. **A change that costs nothing to make is not the same as a change that costs nothing.** A31 was one sampler branch and one assertion; it changed what every clip an animator authors *means*.

---

## Known limitations, recorded deliberately (not bugs)

- **No per-layer weight.** Layers are binary; a layer's strength over time is its own crossfade, and per-part masking covers the common case for free (an upper layer's clip simply carries no track for targets it should not touch). A *sustained* partial weight — a permanent 30% additive breathing layer — is not expressible and would need a `weight` field on `PlaybackLayer` plus a weight argument through `CompositeLayers`. **If a design turns up that needs it, that is the change, not a bug.** (A32)
- **Queue promotion costs one extra frame of the final pose** on hard-cut queues only, which is the price of A30's deferral.
- **Per-part bounds tightening is an explicit non-goal** (§5.8). Parts receive the actor's union.

## Unrelated host-game bug, still open

`Assets/_Scripts/Editor/StitchPunk.Editor.asmdef` has `"includePlatforms": []`, so editor code compiles into player builds and any player test run fails with ~58 compile errors. One-line fix: `["Editor"]`. Offered twice; the owner has not taken it. Not a package issue — and note the irony that the *correct* fix there is what broke the toolkit's PlayMode suite when applied to a test assembly (A17/A25).
