# Phase A Review — Verdict on the Auditor's Audit

**Reviewer:** Reviewer agent · **Date:** 2026-07-26 · **Deliverable:** `Docs/AnimationToolkit/Phase_A_Audit.md` · **Method:** full read + 16 independent spot-checks against the working tree (commit `c95a796`, clean).

---

## Checklist (charter criteria)

| # | Criterion | Verdict | Justification |
|---|---|---|---|
| 1 | Techniques implemented documented (keyframe, flipbook, billboard, VAT, 2.5D) | **PASS** | §2 covers all five, including a correct ABSENT finding for VAT with search locations, plus the orphaned `KeyframeSO` legacy path. |
| 2 | Data flow: authoring → SO→blob bake → runtime systems | **PASS** | §3 traces SO fields → `AnimationLibraryBakingSystem` → blob layout → the full 8-system per-frame pipeline with a complete runtime component table; verified accurate. |
| 3 | Editor tooling documented | **PASS** | §4 covers all 9 files, the live-preview mechanism, Undo gaps, and the non-editor-only asmdef consequence. |
| 4 | Shader conventions (naming, DOTS instancing, passes) | **PASS** | §5: per-graph Hybrid-Per-Instance property tables (counts verified exactly: 2/3/5/4), `[MaterialProperty]` mapping verified complete, shadow/motion-vector/batching status stated with honest "unverified" markers. |
| 5 | Game-specific assumptions/couplings | **PASS** | §6 lists 12 concrete couplings, each with evidence; directly consumable by an Architect defining a package boundary. |
| 6 | Versions DETECTED, not assumed | **PASS** | §1 table verified line-for-line against `ProjectVersion.txt`, `Packages/manifest.json`, `packages-lock.json` — including the transitive Burst 1.8.29 / Collections 6.5.0 / Mathematics 1.4.0 pins, which only exist in the lock file. |
| 7 | Strengths / weaknesses | **PASS** | §7 is specific and evidence-backed on both sides; no hand-waving found. |
| 8 | Preserve/absorb/replace verdicts actionable | **PASS** | §8 covers 24 elements with per-element rationale and the required change for each Absorb; an Architect can design from this table without re-reading the codebase. |
| 9 | Open questions for the Architect | **PASS** | §9's 14 questions each trace to a body finding (checked: Q3↔§3.4 additive, Q4↔§2.1 scale, Q12↔§5 `_UseAltShape` gap). |
| 10 | Every substantive claim carries file:line evidence | **PASS** | 16/16 spot-checks verified; line cites accurate (one off-by-one in a secondary cite, noted below — the primary cite for the same claim is a correct range). |
| 11 | Claimed absences state where the auditor looked | **PASS** | VAT, blend-flag readers, `AnimationLayerType.Direction`, `KeyframeSO` refs, motion vectors, bounds writes, `[MaterialProperty]` inventory — all state search scope; I reran the greps and got the audit's results. |
| 12 | Inferences clearly marked | **PASS** | `**[inference]**` markers used consistently (§2.1 scale regression, §3.2 double-dispose, §3.4 additive intent, §5 shadow pass, §7 bounds). |
| 13 | Internal consistency (verdicts cover body; questions follow from findings) | **PASS** | Every element discussed in §§2–6 maps to a §8 row or is explicitly scoped out (`UnitAnimationAssignmentSystem` "stays game-side"); no verdict contradicts a body finding. |

---

## Spot-checks

| # | Claim | Cited location | Result |
|---|---|---|---|
| 1 | `ApplyPoseJob` forces `Scale = 1f`; sampled `AnimationTargetPose.scale` never applied | `ApplyAnimatedPoseSystem.cs:29-37` | **VERIFIED** — `transform.Scale = 1f;` at line 35; job writes only Position/Rotation/Scale=1. (Note: the §7 restatement cites ":36"; the statement is at line 35 — off by one, primary §2.1 range cite is correct.) |
| 2 | `PostTransformMatrix` baked identity, never written by animation; `NonUniformScale` transform flags baked for renderers | `BodyPartAuthoring.cs:104, 54-56` | **VERIFIED** — `float4x4.identity` at line 104; `TransformUsageFlags.Dynamic \| NonUniformScale` at lines 54-56. Repo grep confirms the only `PostTransformMatrix` writer is `HealthBarSystem`. |
| 3 | `allowBlendIn/Out` baked but never read by any runtime system; `SetLayer` hard-resets `time = 0` | `AnimationLibraryBakingSystem.cs:53-54`, `AnimationUtils.cs:36-41` | **VERIFIED** — grep for `allowBlend` hits only the SO, blob struct, bake copy (53-54), and editor UI/utilities. No runtime reader. `layer.time = 0f` at AnimationUtils.cs:37. |
| 4 | Additive upper layer adds over **rest pose**, not the lower layer's output (claim-mask, reverse layer walk) | `AnimationSamplingSystem.cs:88-97,146-153,218-243` | **VERIFIED** — accumulators start at rest pose (88-91), layers walk highest-first (97), claim mask excludes lower layers (146,153), additive adds/multiplies onto the accumulated value — which at the time the top layer runs is rest pose. |
| 5 | Runtime sampler breaks after first matching track; editor sampler loops all tracks; editor quantizes time, runtime doesn't | `AnimationSamplingSystem.cs:155` vs `EditorAnimationSystem.cs:213-229,192-199` | **VERIFIED** — unconditional `break;` at 155; editor `foreach` uses `continue` (222), no break; `QuantizeTime` at 192-199 editor-side only, runtime uses raw `layer.time / clip.duration` (105). |
| 6 | `StitchPunk.Editor.asmdef` has no platform restriction | `Assets/_Scripts/Editor/StitchPunk.Editor.asmdef` | **VERIFIED** — `"includePlatforms": []`, `"excludePlatforms": []` (lines 13-14). |
| 7 | `[MaterialProperty]` inventory: `_ImageIndex`, `_BaseColor`, `_SecondaryColor`, `_TertiaryColor`, `_IsInteractable`, `_SelectionColor`; **no** `_UseAltShape` component | `AnimationComponents.cs:62,75,86,94`; `UtilityAiComponents.cs:113`; `UnitComponents.cs:47,85` | **VERIFIED** — repo grep returns exactly these seven attribute sites; no `_UseAltShape` component exists, while the property does exist in `2DViewSwitchingPackedArrayShader.shadergraph` (ref name at 3281). |
| 8 | Per-instance (Hybrid) property sets per graph: 2DShader=2, 2DArray=3, 2DPacked=5, 2DViewSwitching=4 | §5 table | **VERIFIED** — `"hlslDeclarationOverride": 3` counts per graph: 2, 3, 5, 4 respectively (exact match). |
| 9 | No VAT anywhere (bake tooling, sampler nodes, vertex displacement) | repo-wide grep per §2.4 | **VERIFIED** — word-boundary grep for `\bVAT\b\|VertexAnimationTexture\|vertex animation` under `Assets/` hits only `_Vault/Spencer/Art_Assets.md`, matching the audit's stated result. |
| 10 | Version table (Unity 6000.5.0f1 rev 88b47c5e7076; Entities/Graphics/Physics/Collections 6.5.0; URP 17.5.0; Burst 1.8.29; Mathematics 1.4.0; Input 1.19.0; Cinemachine 3.1.7; UniTask git) | `ProjectVersion.txt:1-2`, `manifest.json:3,7-16`, `packages-lock.json` | **VERIFIED** — every row matches, including the lock-file-only transitive pins (burst at lock line 68, collections 101, mathematics 189). |
| 11 | `UnitFaceDirectionSystem` fully commented out; `AnimationLayerType.Direction` referenced nowhere in `_Scripts` | `UnitFaceDirectionSystem.cs:1-17` | **VERIFIED** — 17-line file, entirely comment block; grep for `AnimationLayerType.Direction` returns zero hits. |
| 12 | `ImageIndex.onUpdate` set true at bake and per frame, never cleared; only reset is commented out in unused code | `ApplyAnimatedPoseSystem.cs:48`, `UpdateImageIndexSystem.cs:25`, `Core/Unused/ResetEventsSystem.cs:87` | **VERIFIED** — repo grep: all live writes set `true` (BodyPartAuthoring:113, ImageIndexAuthoring:20, DesignApplyUtil:317, ApplyAnimatedPoseSystem:48, EditorApplyAnimatedPoseSystem:22); the sole `= false` is the commented line at ResetEventsSystem.cs:87. |
| 13 | `KeyframeSO`/`DOTSKeyframe` orphaned — zero references outside defining file | `Data/SOs/KeyframeSO.cs` | **VERIFIED** — grep hits only the defining file. |
| 14 | Stale `AttackHitFrameSystem` comment; system no longer exists | `AnimationTimeSystem.cs:49-56` | **VERIFIED** — duration-0 `float.MaxValue` completion hack at 49-56 with the stale comment at line 51; no `AttackHitFrameSystem` type exists anywhere in `_Scripts`. |
| 15 | `BillboardSystem.OnUpdate` not Bursted (commented attribute) due to `Camera.main`; job Bursted; dead-yaw-freeze; parent `CameraVisible` gate | `BillboardSystem.cs:24-29,47,60-63,79-99` | **VERIFIED** — `//[BurstCompile]` at 24, `Camera.main` at 27-28, `[BurstCompile]` job at 47, gate at 62-63, yaw/pitch decomposition at 81-99. |
| 16 | Multi-holder blob double-dispose latent bug; first-library-wins; bake-resolved per-key interpolation | `AnimationLibraryBakingSystem.cs:20-25,92,101-121` | **VERIFIED** — single shared `blobReference` assigned to every holder (101-109) and disposed per-holder in `OnDestroy` (112-121) → double-dispose with >1 holder; `break` after first reference at 24; interpolation override resolved at bake line 92. |

Refuted: **none.** Could-not-verify: **none** (on-screen visual behaviors are marked "unverified" by the audit itself, which is the honest and correct treatment).

---

## Minor observations (non-blocking, no action required)

1. §7 "sampled, then discarded (`ApplyAnimatedPoseSystem.cs:36`)" — the statement is at line 35; the primary cite in §2.1 (`:29-37`) is correct. Off-by-one in a secondary restatement only.
2. The §5 per-instance table enumerates the four sprite graphs; `PainterlyShader`/`PainterlyPaletteShader`/`3DShader` also each carry one per-instance declaration. These are not animation-relevant and the section scopes itself to the unit graphs, so this is not a completeness defect.

---

## Final verdict

**APPROVED**

The audit meets every charter requirement. Sixteen adversarial spot-checks — deliberately targeting the riskiest and most falsifiable claims — all verified against the working tree, absences were independently reproduced by re-running the stated searches, inferences are labeled, and the preserve/absorb/replace table plus open questions are directly actionable by the Architect.
