You are the **stage orchestrator** for a parallel batch on the DOTS Animation Toolkit package
(Packages/com.dotsanimationtoolkit, version 0.48.0 after the A96–A98 batch, integration `e7ae55f9`). Three specs run at once in
their own git worktrees, each under a `spec-lead`, through the Worktree Toolkit (`/worktree-run`, Packages/com.worktreetoolkit).
You alone touch mcp__UnityMCP__*, merge, integrate, drive and close.

| Spec | Path | Version | Tab slot |
|---|---|---|---|
| a96f | Assets/_Vault/Tasks/AnimationPackage/A96F_CreateAssignsMaterial_Spec.md | 0.49.0 | none (Materials exists) |
| a97f | Assets/_Vault/Tasks/AnimationPackage/A97F_SkippedRowAddsTag_Spec.md | 0.50.0 | none (Retarget exists) |
| a99 | Assets/_Vault/Tasks/AnimationPackage/A99_RagdollTab_Spec.md | 0.51.0 | `ClipEditorTab.Ragdoll = 13` |

Why these three:
- **A96F and A97F** are the owner's A96 T13 and A97 T11 answers (2026-09-14, both "yes"): Create also assigns the material, and a
  Skipped row can add its tag to a rig part. Small waves, disjoint files (`Editor/Materials`, `Editor/Retarget`, their docs pages).
- **A99** takes `0.51.0` (its status line says `0.46.0`; correct it). Its three ragdoll checkpoints (RG-T4 limits, RG-T7 launch
  feel, RG-T10 in game) are open, but the owner's standing rule closes unseen checkpoints unless game breaking and A99-D7 re-asks
  them at the tab's own checkpoint. Change no limit default, solver parameter or launch behaviour (A99 §6).
- **A99's move task (T7)** takes code out of `ClipEditorWindow.RagdollHandles.cs`, a stage-owned `ClipEditorWindow*.cs` file. Read
  A99 §5 T7 first. If it edits only that partial, grant a99 exclusive ownership of it in its prompt (a96f and a97f touch no window
  file); otherwise T7 is stage work after the merge.
- **A100 (`0.52.0`) waits:** its T0 needs Play mode on `DOTSTestScene`. Run it alone afterwards, only with the owner's word.

Read, in order:
1. `.claude/skills/worktree-run/SKILL.md`, `.claude/agents/spec-lead.md`, `Assets/_Vault/Memories/Code/WorktreeToolkit.md` (traps 17–26).
2. `Assets/_Vault/Tasks/AnimationPackage/AnimationPackage_Roadmap.md` §3.
3. The three specs' §0, §2 and §5 only.
4. The newest sections of `Assets/_Vault/Memories/Code/AnimationToolkit.md`: Materials (A96), Retarget (A97), Capture (A98) and all
   three "Parallel batch" lessons.
5. The previous prompt, `Assets/_Vault/Spencer/next-session-parallel-a96-a98-prompt.md`: its lead contract, gate syntax, cautions and
   inherited mechanics carry over unless this file says otherwise. Copy the lead contract into each lead's prompt verbatim.

**Owner pre-answers:**
- Models: lead `opus`, worker `sonnet` for all three.
- Merges: `worktree.py merge` authorized for a96f, a97f and a99 once each reports ready with its gates green.
- A96 T13, A97 T11 and A98 T13 were answered 2026-09-14 and are already recorded (A98 accepted; A96, A97 reworked as the F specs).

## Phase 0 — stage prep

1. **Preflight:** `worktree.py doctor --json` (stop on git failure, missing hooks or `brokerAlive` false); `git status` must show
   nothing that names a type any spec removes or renames (trap 21); delete any empty `.claude/worktrees/*` folder that is no
   longer locked.
2. **Baseline:** compile gate, then full suites. Expected: EditMode 857 (856 passed, standing Conformance_A only), PlayMode 285.
3. **CHANGELOG** top section is `## [0.48.0]`.
4. Record both registry sha256s (`DotsAnimationToolkitAnimEventKeyRegistry.asset`, `DotsAnimationToolkitTargetTagRegistry.asset`)
   and the Phase 0 results in each spec's §7; correct A99's status line to `0.51.0`; commit only those files; push.

## Phases 1–3

As the A96–A98 prompt, with these differences:
- **PlayMode fixtures:** the broker refuses them (trap 25). Tell all leads to send "gate needed: <id> <sha> <fixtures>" for any
  PlayMode fixture (a96f's prefab write may reach `ActorBakingAcceptanceTests`; a99 touches ragdoll code, so likely). Gate it by
  hand (`stage-commit`, compile, fixture, `restore-trunk`) only while `stage.busyWith` is null, then `git status` for stray folder
  metas (trap 26).
- **Merge order:** a96f → a97f → a99. Integration: only a99 adds a tab (enum 13, toggle and pane after Capture and before Health, which stays
  last per the owner's 2026-09-14 strip order; `tabToggles` sized 14; layout test lists, `index.md`); CHANGELOG 0.51.0, 0.50.0 and 0.49.0 newest on top; `package.json` and the conformance pin at
  0.51.0; allowlist.
- **Drives:** each spec's drive task, one at a time, scratch only: `Assets/A96FScratch/` (rig and prefab copies; never
  `MaleCitizen.prefab` or `NewRig.asset`), `Assets/A97FScratch/` (clip and rig copies, a `CreateInstance` registry copy),
  `Assets/A99Scratch/` (a rig copy; the drop simulation in the preview only). Delete scratch after each drive and re-check both
  registry sha256s. No Play mode, no scenes, no `SaveAssets`, no player build, never drive the docked Clip Editor.
- **Close:** status lines, ticked boxes and §7 close logs; HANDOFF §4 paragraphs (A99 on top); AnimationToolkit.md sections; the
  roadmap; commit and push. Write close texts to a scratchpad Python file and run it.
- **End** with the next prompt (A100 alone, with a Play-mode authorization slot, plus any reworks the answers open) and one owner
  checkpoint message covering A96F T7, A97F T7 and A99 T12, naming real assets and what to look at.

## Settled owner calls (do not re-ask)

No sound mixing; no package-side event handlers; names never numbers; sprite sheets are Texture2DArrays; nobody auto-writes arrays
into materials; unseen checkpoints close as accepted unless game breaking; A93F T10, A94F T12, A95F T10 and A98 T13 accepted
2026-09-14; Create assigns (A96F) and Skipped rows add tags (A97F) as specced.
