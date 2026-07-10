# System Design Plans

This folder holds one **self-contained design-doc `.md` per system** Stitch Punk still needs. Each doc is detailed enough that Spencer can edit it inline and hand it back as an **executable spec** — the C# is only written after a doc is approved.

## Workflow

1. Pull the next system from the raw braindump in [`../futureneedsplan.md`](../futureneedsplan.md).
2. Claude asks a Q&A round to flesh out the architecture and lock the foundational decisions.
3. Claude drafts `Plans/<System>.md` with the spec + inline **← DECISION** markers for any sub-choices left open.
4. Spencer edits the doc (resolves the markers, tweaks scope).
5. Spencer hands the doc back → Claude builds it in the suggested phases.

Every system shares the codebase's architectural identity: **accessible from outside via data components (the "request model")**, and **entered either by a component on the entity it acts on, or by another system spawning a one-frame signal entity** — the `LoggingSystem` pattern (spawn `LogMessage` entity → presentation system reads + acts + destroys).

## Authored by `dots-task-creator`

The planning workflow below is codified in the **`dots-task-creator`** project skill (`.claude/skills/dots-task-creator/`). Invoke it (or just say "plan the X system") to run the Q&A and generate a new spec in this folder from the standard template. See the [Skills index](../../Memories/Code/Skills.md).

## Skills Needed convention

Each plan doc lists, near the top under a **`Skills Needed`** heading, the **project skills** (in `.claude/skills/`) relevant to building it — by name (e.g. `dots-blob-library`, `dots-system-scaffold`, `dots-authoring-baker`, `dots-unit-ai`). This tells the build step which scaffolding skills to invoke. See the [Skills index](../../Memories/Code/Skills.md) for what each one does.

## Status legend
⬜ not started · 📝 spec drafting · ✅ spec ready · 🔨 building · ✔️ done

## Systems

| System | Doc | Status |
|---|---|---|
| **Player Attack** (melee swing → AttackRequest; separate CombatTarget; revives PlayerAttackSystem) | [PlayerAttack_System.md](../Completed/PlayerAttack_System.md) | ✔️ done |
| **Sound** (SFX / ambient loops / layered music) | [Sound_System.md](../Completed/Sound_System.md) | ✔️ done — built + verified |
| **DamageEvent** (attack/damage refactor → one-frame signal entity, deletes Hurt buffer) | [DamageEvent_System.md](../Completed/DamageEvent_System.md) | ✔️ done — superseded by v2 |
| **DamageEvent v2** (NativeQueue bus + source-agnostic DamageSource + AOE friendly-fire + spike hazard) | [DamageEvent_v2_System.md](../Completed/DamageEvent_v2_System.md) | ✔️ done |
| Dialogue System + UI | — | ✔️ built (pre-dates this planning workflow — no spec doc; editor + runtime live) |
| **Save System** (generic `IPersist` serializer, minion design, travel + manual save) | [Save_System.md](Save_System.md) | ✅ spec ready |
| Building System (structures, storage) | — | ⬜ not started |
| **Despawn System** (central `Despawn` funnel: pool-vs-destroy via `DespawnMode` + `PoolOwner`, per-type cap, `Lifetime` TTL producer) | [Despawn_System.md](Despawn_System.md) | ✅ spec ready |
| **Player Resource System** (`ResourceStack` ledger + delta-buffer mutation + IPersist snapshot + HUD) | [PlayerResource_System.md](PlayerResource_System.md) | ✅ spec ready |
| Game UI — Health | — | ⬜ not started |
| Minion Systems → new state machine | — | ⬜ not started |
| **Behavior Bake Validation** (shared command catalog; bake warns on unimplemented BehaviorSO commands) | [BehaviorBakeValidation_System.md](../Verification/BehaviorBakeValidation_System.md) | 🔨 built — awaiting Editor compile + verify |
| **Direction System** (facing representation — DECISION-FIRST, blocks part-SO authoring) | [Direction_System.md](Direction_System.md) | ✅ spec ready |
| **Directional Texture Packing + Recolor** (4 facings → RGBA channels + mirror-flip 8-way; grayscale mask → palette ColorRamp recolor; implements Direction §Option B) | [DirectionalTexturePacking_System.md](DirectionalTexturePacking_System.md) | ✅ spec ready |
| **Painterly Gradient-Map** (64×64 gradient atlas as UV palette; mesh UV picks colour, lighting shades; PainterlyGradientMap node + PainterlyPaletteShader + LUT generator) | [PainterlyGradientMap_Shader.md](../Verification/PainterlyGradientMap_Shader.md) | 🔨 built — Editor verify pending |
| **Zombie Conversion** (ZombifyRequest composes SwapBrainRequest + ChangeDesignRequest) | [ZombieConversion_System.md](ZombieConversion_System.md) | ✅ spec ready |
| **Minion Order Robustness** (order-time attack resolution from AvailableAttack; Stop/ReturnToPlayer verbs) | [MinionOrderRobustness_System.md](MinionOrderRobustness_System.md) | ✅ spec ready |
| **Behavior Command Split** (extract interpreter switch arms into Utils/BehaviorCommands; pure refactor) | [BehaviorCommandSplit_System.md](BehaviorCommandSplit_System.md) | ✅ spec ready |
| **Ranged / Projectile Combat** (SpawnEntity arm + pooled projectile → DamageBus; needs Despawn + Split + MinionOrders first) | [RangedCombat_System.md](RangedCombat_System.md) | ✅ spec ready |
| **Factory Minimal Loop** (un-park ProductionSystem; 1 product / 1 line / 1 buyer; → ResourceStack sink) | [FactoryMinimalLoop_System.md](FactoryMinimalLoop_System.md) | ✅ spec ready |
| **Schedules + Waypoints** (WorldClock + time-of-day consideration curves + waypoint awareness) | [SchedulesWaypoints_System.md](SchedulesWaypoints_System.md) | ✅ spec ready |
| **Crowd-Scale Awareness** (unit spatial cells for Enemy/Social awareness + first profile pass) | [CrowdScaleAwareness_System.md](CrowdScaleAwareness_System.md) | ✅ spec ready |
| **Cleanup Batch 2026-07** (Thirst NeedType, EffectLibrary collision, typo renames, docs truth pass) | [CleanupBatch_2026-07.md](CleanupBatch_2026-07.md) | ◐ rows 3/4/7/8/10 done; rows 1/2/5/6/9 remain |
| **Feature Isolation Follow-ups** (wire FeatureConfig plugs, single-feature World tests, strip legacy gating) | [FeatureIsolation_System.md](FeatureIsolation_System.md) | ✅ spec ready |
| **CharacterRig Finish + Hardening** (verify-doc rewrite, bake warnings, palette guard, PartDefId decision) | [CharacterRigHardening_System.md](CharacterRigHardening_System.md) | ◐ code items 2/3/5/6 built; verify-doc rewrite + PartDefId decision remain |
| **Minion Revival & Life-State** (revive→zombie minion via `SwapBrainRequest`, `Alive` deprecation) | [MinionRevival_System.md](../Verification/MinionRevival_System.md) | 🔨 built — code landed (Ph1–4), verify pending |
| **Brain Control Split** (UtilityBrain=decision / StateMachine=execution; death blank-slate, player-controlled revive, minion self-defence) | [BrainControlSplit_System.md](../Verification/BrainControlSplit_System.md) | 🔨 built — code landed, verify pending |
| **Ragdoll2D Rework** (procedural 2D ragdoll on real 3D trajectory: float3 launch + CollisionWorld raycast landing/bounce, plane-space pendulum flail → authored-zone settle, RagdollProfileSO→AttackBlob, corpse-cell stacking) | [Ragdoll2D_System.md](../Verification/Ragdoll2D_System.md) | 🔨 built — awaiting compile + rebake + play verify ([verify-ragdoll2d.md](../Verification/verify-ragdoll2d.md)) |
| **Cutscene & Animation Stage** (edit-mode PreviewSceneStage editor replaces hybrid preview scene; CutsceneSO multi-actor tracks → blob → CutscenePlaybackSystem) | [Cutscene_System.md](Cutscene_System.md) | ✅ spec ready |
| **Texture Channel Packer** (Editor GraphView tool: drag greyscale images → wire R/G/B/A ports into an output node → bake packed PNG in place; invert toggles, defaults, isolate preview, recipe SO; replaces `PainterlyMaskPacker`) | [TextureChannelPacker_Tool.md](../Verification/TextureChannelPacker_Tool.md) | 🔨 built — awaiting Editor compile + verify ([verify-texturechannelpacker.md](../Verification/verify-texturechannelpacker.md)) |
| Animations (content) | — | ⬜ not started |
| **Unit Design** (per-part random texture indices, minion persistence) | [UnitDesign_System.md](UnitDesign_System.md) | 🔨 built — code landed, verify pending |
| **Character Rig** (unified `BodyPart` registry + `PartLibrary` blob + shape×color design grid; replaces Design/Ragdoll2D/AnimationTarget/Animator authorings; enables palette-swap zombification) | [CharacterRig_System.md](../Verification/CharacterRig_System.md) | 🔨 built — code landed, verify + Editor migration pending |
| Human → Zombie Conversion | — | ⬜ not started |
| Menu UI | — | ⬜ not started |
| Interactions/Behaviors (bulk, AI-assisted SO setup) | — | ⬜ not started |
| Trade System Group | — | ⬜ not started |
| Vehicle System (driving, caravan base) | — | ⬜ not started |
| Direction System (multi-facing characters) | — | ⬜ not started |

> Build-order notes for narrative/scene-driven systems (Dialogue, Narrative Events, Cinematic Camera, Feral Zombie AI, etc.) live in the lower half of [`../futureneedsplan.md`](../futureneedsplan.md) and will graduate into their own docs here as they're picked up.
