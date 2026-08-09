# DOTS Animation Toolkit — session handoff

> ## ✅ CURRENT — 2026-08-09
>
> **`main` is at the c10 merge: 255 EditMode + 178 PlayMode green, compile clean, verified through the MCP.** The bridge dropped for most of 2026-08-08 and came back on the 9th; the gate is live again, so verify through `run_tests` rather than asking the owner to compile.
>
> Shipped since the block below: sockets (own blob, `RigTarget` + `Bone` modes), the clip-editor preview with socket markers, `FacingResolver` (A38 tables), Mirror Clip, all four §7.1 inspectors, `shader-contract.md`, `VatMeshPreparer`, the §11.1 disk round-trip tier, the Quick Start sample, and **multi-source VAT tracks (schema 4)**.
>
> **Two traps this stretch, both worth remembering:**
> 1. A killed subagent's partial work got committed in `6bf0b3e`, leaving `ClipBlob.vatTargetRanges` referencing an undefined `VatTrackRangeBlob` — the package did not compile. I had checked `git status`, seen a clean tree, and concluded nothing was written. **A clean working tree proves nothing when someone else may have committed.** Diff against the pre-agent commit instead.
> 2. `ClipBlob.debugName` is baked from the asset's *file* name (`CreateAsset` renames the object) and is folded into the content hash. A fixture that names an object one thing and saves it as another gets a different hash across a round trip, which presents as a serializer defect. `SaveAsset` in `DiskRoundTripTests` now derives the file name from the object name so the two cannot drift.
>
> **Schema is now 4.** A bump must land together with a re-recorded `ExpectedContentHash` *and* the paired schema literal in `ContentHashGoldenTests` — the failing assertion prints the value to record, so never invent one.
>
> Remaining: host migration §13.2 step 3 (game-side, not package), and `Documentation~/getting-started.md` has not been re-read since the C10 authoring changes.

<details>
<summary>Superseded — verification state as of 2026-08-08</summary>

> ## ⚠️ READ FIRST — verification state as of 2026-08-08
>
> **`main` is at `11ecdbe` and is the last state with a verified clean compile.** It contains C5–C7 (VAT bake, shaders, clip editor incl. preview pane) and the socket system. Verified by a real Unity compile — ScriptCompilation ran, ILPP post-processed every assembly, zero `error CS`/`BC` in fresh log output.
>
> **The test suite has NOT run since `c6ab736`** (239 EditMode green). The Unity MCP bridge died mid-session and never re-registered (`instance_count: 0` with Unity running). So `11ecdbe` is *compile-verified but not test-verified*. **First action next session: run EditMode + PlayMode.** Watch `PackagingConformanceTests` (new Editor files) and `ActorBakingAcceptanceTests` (`ActorBaker` gained a socket-registry call). `SystemGroupStructureTests` was checked by hand — it asserts named systems are in named groups rather than enforcing a closed set, so the new `SocketResolveSystem` does not trip it.
>
> **Branch `c8-authoring-tools` is UNVERIFIED — never compiled at all.** The Editor was closed for that entire stretch. Do not merge it without a compile pass. See "C8 — built blind" below.

</details>

**Written:** 2026-08-07, updated 2026-08-09
**State:** Phases done: A, B, C0–C7, plus sockets, multi-source VAT and packaging. Host migration steps 1–2 done, step 3 not started.

**Baseline, verified through the MCP 2026-08-07:** Console clean of `error CS` / `BC`; **232 EditMode + 178 PlayMode, all passing, each in its real mode.**

## What the migration proved, and the one finding that changes priorities

The host's 20 clips are converted, and a pilot face built entirely from converted data is **owner-verified on screen for all three techniques: flipbook slice changes, crossfading, and mirroring.**

**The package drives the host's existing shaders with no shader work at all.** `SpriteSliceProperty` is `[MaterialProperty("_ImageIndex")]` — the exact name the host's array shaders already read. §10 answer 11 ("hosts keep their own graphs and consume the property names") holds in practice, not just on paper.

**Therefore C5 is not on the critical path for Stitch Punk.** C5 builds the *sellable package's own* reference shaders; the game already renders through its own. Migrating the game fully (§13.2 steps 3–4) can happen before C5, after it, or in parallel. That is a real sequencing choice and it was not obvious before the pilot.

## Amendments A35–A40, all from this stretch

| # | What |
|---|---|
| A35 | §5.3's rationale for `RigBindingSystem` was false — `Instantiate` *does* remap buffer entity refs |
| A36 | **Shipping-blocker.** V07 failed every non-VAT project: a `[Serializable]` class field cannot be null, so every saved clip read as VAT-sourced → no registry baked → every actor frozen |
| A37 | The slice sum: `restSliceIndex` (variant) + `PartFacing.viewOffset` (view) + clip key (frame), wrapped inside the variant block |
| A37a | Mirror ≠ alt view. `mirrorX` reflects position, rotation **and** scale; `viewOffset` changes the frame |
| A38 | The five direction sets, all owner-supplied. Every set is closed under mirroring |
| A39 | Screen-aligned billboarding is supported — **the host's look must not change** |
| A40 | A seeded starting layer activates regardless of the rig's `defaultActive` |

**Three of these were found by the owner looking at the screen, not by tests.** A36, A40 and the `TargetKind.Quad` converter bug were all invisible to a green suite. That is the strongest argument in this project's history for the §11.4 human-verification step being load-bearing rather than ceremonial. Project-wide EditMode is higher — the extra ~61 belong to the host game's own `StitchPunk.Tests`, so pass `assembly_names` to `run_tests` when comparing against these numbers. Discrimination verified by twenty-three mutation runs across C4.3–C4.9 (tables in the C4 plan). **Nothing is owed for C4.**

**C4's DoD is fully discharged**, including the one line Claude cannot verify: the owner confirmed on-screen clip playback of `Assets/Scenes/AnimationToolkitSmoke.unity` on 2026-08-06.

## Read this before starting C5

Two amendments landed in C4.9 and both are about *how this package is tested*, not about C4:

- **A35** — `Instantiate` remaps entity references inside dynamic buffers for `LinkedEntityGroup` members, so §5.3's rationale for `RigBindingSystem` was false. The rebuild was kept (load-bearing for pooling and non-baked routes; `phase01` must be re-derived there regardless) and the *rationale* corrected.
- **A36 — the serious one.** V07's `clip.vatSource == null` test could not distinguish "no VAT" from "has been saved once", because a plain `[Serializable]` class field cannot serialize null. Every real clip asset read as VAT-sourced → V07 Error → `ClipRegistryBuilder` throws → **no registry baked at all** → every actor holds its rest pose. Shipping-blocking, and invisible to all 221 fixtures.

**Their shared lesson, which C5 should act on: a suite that constructs every input in memory has no coverage of the serializer, and the serializer is part of the authoring contract.** Both defects appeared the instant something built *real, saved* assets rather than fixtures. §11.1 is owed a small disk-round-trip tier (M1/M6 scope). **This matters more than usual for C5**, whose subject — ShaderGraph assets, materials, generated shader code — exists *only* as serialized files. An in-memory fixture cannot see a `.shadergraph` at all.

---

## Start here

1. `Docs/AnimationToolkit/Phase_C4_Plan.md` — the 14 pieces, the nine phases, the traps carried in from C3, the test-integrity standard, and a per-phase record of what each build step actually found. **Read this before the architecture doc.**
2. `Docs/AnimationToolkit/Phase_B_Architecture.md` — the normative spec. **111 KB — never read whole.** Grep headings, then Read with offset/limit. §5 is C4's territory; §11.2 and §8 M3 are C4.9's.
3. `Docs/AnimationToolkit/Phase_C3_Gate4.md` — the last gate: verdict, the 10 blocking items, the Resolution table, and the "verified clean — do not re-litigate" list. Read that list before re-auditing anything in C3.

Earlier gate docs (`Phase_C3_Review.md`, `Phase_C3_ReReview.md`, `Phase_C3_Gate3_Incomplete.md`) are history, superseded by Gate 4.

---

## Environment — what is true as of 2026-08-05

**The Unity MCP is UP and was used for the whole of C4.7 and C4.8.** One instance: `Stitch_Punk@852da23e19ef0320`.

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
- ✅ **C4.8 LOD — closed 2026-08-05.** `AnimationLodPolicy` (9 EditMode fixtures), `AnimLodDistanceSystem`, and the three level effects wired into `TransformSampleSystem` and `VatMaterialSystem` (11 fixtures in `AnimationLodTests`, 2 in `LayerCompositionTests`, 2 group-placement rows). **Mutation-verified: 13 mutations over five runs.**
- ✅ **C4.9 acceptance + smoke scene — closed 2026-08-06. C4 IS COMPLETE.** 7 new fixtures in `RuntimeAcceptanceTests` closing the 4 uncovered §8 M3 clauses and 2 §11.3 runtime rows; the smoke scene at `Assets/Scenes/AnimationToolkitSmoke.unity` + its subscene, built by a re-runnable menu item (**Tools ▸ DOTS Animation Toolkit ▸ Build Smoke Scene**). **Owner-confirmed on screen 2026-08-06.** Produced A35 and A36. Mutation-verified: 6 mutations over four runs, M1 and M2 each failing exactly one fixture. See `Docs/AnimationToolkit/Phase_C4_9_Acceptance.md` for the clause-by-clause map.

### What C4.7 settled

- **Amendment A33 — the PlayMode asmdef references `Unity.Entities.Graphics`.** `Unity.Rendering.RenderBounds` is defined there, not in `Unity.Entities`, and C4.7 was the first fixture in the package to read a `RenderBounds` back. Caught twice: once by the compiler, then again by `PackagingConformanceTests` when the asmdef was updated and §1.3 was not. **That second catch is the conformance test working as designed — if you touch any asmdef, update §1.3 and `PackagingConformanceTests.AsmdefExpectations` in the same commit.**
- **The `BoundsDirty` *disable* is finally pinned by mutation** — outstanding since C4.3, where the tag's *raising* was covered but its reset was not. Deleting `boundsDirtyEnabled.ValueRW = false` fails `TheDirtyTag_IsClearedByTheWrite`; swapping `[WithAll(BoundsDirty)]` for `[WithPresent(BoundsDirty)]` fails `AFrameThatOnlyAdvancesTime_LeavesBoundsUntouched`. Both defects produce *correct bounds at permanent cost*, so nothing but a test was ever going to find them.
- **A second non-discriminating fixture, caught before the gate rather than after.** `ABlendingLayer_KeepsBothClipsInTheUnion` originally made the *incoming* clip the large one — whose box swallows the outgoing clip's on every axis, so the expected union was the same number whether or not the crossfade source was folded in. Reversed so the outgoing clip is the large one.
- `VatMaterialSystem` iterates **parts**, not actor roots, reaching the layer buffer through `RigPartBinding.actorRoot` — `VatDriven` *is* the VAT archetype, and a torso and a cape on one actor may follow different layers.
- `_VatFrameB` defaults to `_VatFrameA` rather than 0: with `_VatBlend = 0` a correct shader ignores B, but a 0 there points at the first row of the whole texture, so any shader that lerps before testing the weight would snap the mesh to an unrelated clip's pose.

---

## What C4.8 settled

- **Amendment A34** — three decisions, each with a plausible cheaper alternative that was rejected in writing:
  1. §5.10's table is **pure functions** in `AnimationLodPolicy`, not logic inside the systems that obey it. Three consumers read it; a level meaning something marginally different in one of them is a divergence nobody could ever see, because both readings look plausible in motion.
  2. **An uncapped actor gets an outright cap from the level** (30 Hz at 1, 15 Hz at 2 and 3), and **level 3 reports the quarter rate rather than 0**. Halving a `rateHz` of 0 is still 0, so a LOD system that only scales explicit rates is a no-op on essentially all content while appearing to work; and `ClipSampler.ShouldSample` reads 0 as "every frame", so expressing the freeze as a rate would make the most expensive level the only unquantized one.
  3. **`AnimSampleState` is a new root component** carrying an int fold of the clips the last sample came from — the only thing in the archetype that can answer level 3's "unless the clip changes". **The §5.2 root archetype is now fourteen components**, and `ActorBakingAcceptanceTests`' exact-archetype assertion moved with it.
- **`BoundsDirty` was the tempting substitute for `AnimSampleState`, and was rejected on the A28/A30 pattern.** It is enabled on a superset of the right moments, but `RenderBoundsUpdateSystem` clears it — so reading it during sampling works only while those two systems keep their current order, and a reorder would freeze distant actors on the wrong pose in silence.
- **`ClipSampler.CompositeLayers` gained a `bool snapBlendWeights` parameter with no defaulting overload.** One production caller exists; a silent default is how half the callers would stop honouring LOD.
- **Burst rejects a `float4` passed by value into a `[BurstCompile]` static method** (BC1064 + BC1067) — the attribute makes it a direct-call entry point and vectors must cross by reference. `LevelForDistanceSq` takes `in float4`. First time the package has hit this, because every other direct-call entry point takes scalars or `ref`/`in` structs.
- **A fixture whose stated rationale was wrong, found by trying to mutate it.** `DroppingBackFromLevelThree_ResumesSamplingImmediately` was documented as catching "not recording the signature at lower levels" — but that is not what makes the actor resume; `FreezesPose(0)` being false is. Both the test comment and the runtime comment it echoed were corrected and the fixture re-aimed. **Writing the mutation exposed it; re-reading the test would not have.**

---

## Which way next

Four candidates, none blocking the others:

1. **§13.2 step 3 — the call-site rewrites.** Rewrites `BehaviorExecutionSystem`, `PlayerAttackSystem`, `UnitAnimationAssignmentSystem`; adds the `CameraVisible → AnimVisible` bridge; reorders the combat event consumer. **This is what turns four finished modules into a game that actually runs on the package.** Invasive — it touches live systems — which is why the owner stopped before it.
2. **C5 — M4 shaders.** The package's own reference graphs. Sellable-package work, *not* needed for the game (see above).
3. **Mirror Clip utility.** Pulled forward from C7; useful the moment direction clips get authored.
4. **The facing helper.** Fully specified by A38 — five tables, mirror derivation, and sign-of-horizontal-movement quantization. Contained.

### Owed, recorded rather than silently dropped

- **§11.1 needs a disk-round-trip test tier (A36).** A suite that builds every input in memory has no coverage of the serializer, and the serializer is part of the authoring contract. This is how a shipping-blocker survived 221 fixtures.
- **`RigBindingSystemTests` needs a fixture starting from a *populated* `RigPartRef` buffer (A35).** Its seven fixtures all start empty, so they test first-time binding, not re-binding.
- **C5 owes, from A39:** a screen-aligned value in `_BillboardParams`, `BillboardTransform` taking the camera forward, and a verification row "screen-aligned reproduces the host's pre-migration framing".
- **C5 owes, from the camera correction:** billboard-under-orbit verification needs a scratch scene — the game camera tilts and rotates but does not orbit freely.
- **Delete `Assets/AnimationToolkitMigration/Runtime/PilotDriver*.cs`** when step 3 lands. It is review scaffolding; the real host drives playback from behaviour systems.

## C5 brief — M4 shader slice 1

**DoD (§9 row C5):** M4 compile + instancing-block + pass-grep tests green for the sprite graph; billboard modes human-verified. **Evidence:** generated-code excerpts; screenshots (billboard modes, flipbook anim, batch count).

**Read first:** §6 in full (it is short and entirely normative — §6.1 inventory, **§6.2 the property table, which is the CPU↔GPU contract M3 already implements**, §6.3 displacement in all passes, §6.6 batching rules), then §8 M4, then §9's C5 row. §6.2 is jointly owned with M3, so a property name changed on the shader side silently breaks a component that already ships.

**What C4 leaves ready for it.** The material-property components of §6.2 are built, tested and mutation-verified: `SpriteSliceProperty` / `AtlasFrameProperty` (`SpriteMaterialSystem`, C4.6) and `VatFrameAProperty` / `VatFrameBProperty` / `VatBlendProperty` (`VatMaterialSystem`, C4.7). **The CPU half of the contract is done and the shaders must match it, not the reverse.**

**Three things to carry in:**

- **`_VatFrameB` defaults to `_VatFrameA`, not 0** (C4.7). A shader that lerps *before* testing `_VatBlend` is therefore still correct. Do not "optimise" that default away.
- **Flipbook frames never blend** — nearest wins at the blend midpoint, snapped and documented (§10 answer 2).
- **The billboard facing rule is normative and testable by grep:** `_WorldSpaceCameraPos` only, `UNITY_MATRIX_V` forbidden (§6.3). The observable consequence the owner checks is that a billboarded quad's **shadow re-orients with camera orbit, not with the light**.

**And the C4.9 lesson, which lands hardest here:** every artefact C5 produces — `.shadergraph`, `.mat`, generated shader code — exists **only** as a serialized file. In-memory fixtures cannot see one. A36 is exactly what that blindness costs. Prefer tests that load the real asset from disk (`AssetDatabase.LoadAssetAtPath`) and grep real generated output, which is what §8 M4's acceptance already asks for.

**Tooling note:** the host repo has a `shader-edit` skill for Unity 6.5 reflection-API HLSL nodes and programmatic `.shadergraph` surgery. It is host-game tooling, but the graph-editing mechanics transfer.

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
5. **A fixture's stated rationale can be wrong while the fixture is fine.** C4.8's `DroppingBackFromLevelThree` claimed to catch something that in fact holds trivially; only writing the mutation revealed it, and the fix was to re-aim the comment, not to delete the test. **Write the mutation for every fixture, including the ones you are sure about.**

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

---

## C8 — built blind (branch `c8-authoring-tools`, 2026-08-08)

The owner went remote with the Editor closed and asked for maximum progress. Everything in this section was written **without a compiler**. APIs were verified by reading `Library/PackageCache` and existing package source rather than from memory, but reading is not compiling. **Assume it does not build until it does.**

### The correction that reshaped the roadmap

I told the owner clip-level `vatSource` was the keystone blocker for hybrid flipbook+VAT, and he authorised work on that premise. **It was wrong.** `ClipValidation` has no exclusivity rule, and `VatMaterialSystem` iterates *parts* (`VatDriven` + `RigPartBinding.actorRoot`), so **a VAT torso and a flipbook head on one actor already worked.** The real limit of clip-level `vatSource` is narrower — *one VAT source per clip*, so you cannot bake a torso and a cape from two different source clips into one `ClipAsset`.

**Lesson, recorded because it cost a wrong recommendation:** before proposing a format change, check whether the runtime already does the thing per-part. This toolkit's runtime spine is consistently more capable than its authoring surface exposes; the gaps are in authoring and preview, not in the data model.

### What landed

| Piece | Where | Notes |
|---|---|---|
| Socket system | `Runtime/{Identity,Blobs,Components,Systems}`, `Authoring/{Assets,Build,Baking}`, `Editor/VatBaking` | Own blob, not `ClipRegistryBlob` — keeps the clip schema and golden hash untouched, and makes sockets opt-in |
| Clip editor preview | `Editor/ClipEditor/Preview/` | Poses via the runtime's own `ClipSampler` out of a `ClipRegistryBuilder` blob, so it cannot drift from what ships |
| `FacingResolver` + tests | `Runtime/Sampling/`, `Tests/EditMode/` | A38's tables longhand |
| Validation badge | `Editor/ClipEditor/ValidationBadgeElement.cs` | Renders `ClipValidation`; never decides validity itself |
| Mirror Clip utility, `RigAsset`/`ClipSetAsset` inspectors, `shader-contract.md`, composite example shader | see branch | Written by parallel subagents; **review before trusting** |

### Sockets — the design, so it is not re-litigated

A socket is a named attachment point resolving to a world transform each frame. Two modes: `RigTarget` follows a part entity and needs **no baked data** (the sampler already computes it); `Bone` follows a bone of the VAT source rig, whose motion exists only in a texture at runtime and so is sampled at bake.

**Baked samples, not a second VAT texture** — the idea the owner remembered from an earlier session. A texture is right for data the *GPU* consumes; an attachment is an entity with a `LocalTransform`, so the consumer is the CPU, and CPU-reading a texture means a readback that is slow, async, and unavailable when the texture is not readable in a build. The data is ~100KB for eight sockets over six hundred frames — a blob answers it with one indexed load in Burst. A socket texture would earn its place only if a shader needed the position directly.

**Two traps the code is shaped to avoid**, both of which fail silently:
- The world transform is **composed** (actor `LocalToWorld` × part `LocalTransform`), not read from the part's `LocalToWorld`. Unity's transform systems run after this group, so reading it leaves every attachment a frame behind.
- Attachments are **transform roots, not children**. The system writes a world transform into `LocalTransform`; parenting as well applies the actor matrix twice, which reads as a subtle scale error.

### Highest-risk spots to compile-check first

1. `Editor/VatBaking/VatTextureBaker.cs` — the only edit inside a *working* path. The socket sampling is a deliberately isolated second pass so a failure there cannot corrupt a texture, but it is still the riskiest diff.
2. `Authoring/Baking/ActorBaker.cs` — `AddSocketRegistry`, particularly the `Hash128` construction.
3. The three subagent-written Editor files — never reviewed by a compiler *or* by their author running anything.

### Next, in order

1. **Compile + full test run.** Nothing else matters until then.
2. **Socket authoring UI review** — this is what makes sockets usable; without it bone names are free text and a typo yields a silent origin-pinned attachment.
3. **Bone sockets need a VAT rebake** to populate sample tracks. Rig-target sockets should work without one.
4. Multi-source VAT tracks (the *real* `vatSource` limitation).
5. §13.2 step 3 — host call-site rewrites; then delete `Assets/AnimationToolkitMigration/Runtime/PilotDriver*.cs`.
