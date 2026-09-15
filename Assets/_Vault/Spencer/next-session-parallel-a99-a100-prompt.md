You are the **stage orchestrator** for a parallel batch on the DOTS Animation Toolkit package
(Packages/com.dotsanimationtoolkit, version 0.48.0 after the A96–A98 batch, integration `e7ae55f9`). Two specs run at once in
their own git worktrees, each under a `spec-lead`, through the Worktree Toolkit (`/worktree-run`, Packages/com.worktreetoolkit).
You alone touch mcp__UnityMCP__*, merge, integrate, drive and close.

| Spec | Path | Version | Tab slot |
|---|---|---|---|
| a99 | Assets/_Vault/Tasks/AnimationPackage/A99_RagdollTab_Spec.md | 0.49.0 | `ClipEditorTab.Ragdoll = 13` |
| a100 | Assets/_Vault/Tasks/AnimationPackage/A100_StatsTab_Spec.md | 0.50.0 | `ClipEditorTab.Stats = 14` |

Why these two:
- **Versions moved:** A99's status line says `0.46.0` and A100's `0.47.0`; take 0.49.0 and 0.50.0 and correct both (roadmap rule).
- **A99's ragdoll checkpoints:** HANDOFF lists three open ragdoll checks (RG-T4 limits, RG-T7 launch feel, RG-T10 in game). The
  owner's standing rule (2026-09-14) closes unseen checkpoints as accepted unless game breaking, and A99-D7 re-asks all three at the
  tab's own checkpoint, so A99 runs. Do not change any limit default, solver parameter or launch behaviour (A99 §6).
- **A99's move task (T7)** takes code out of `ClipEditorWindow.RagdollHandles.cs`, a stage-owned `ClipEditorWindow*.cs` file. Read
  A99 §5 T7 first. If it edits only that partial, grant a99 exclusive ownership of it in its prompt (a100 touches no window file);
  otherwise T7 is stage work after the merge.
- **A100's T0 needs Play mode** on `DOTSTestScene` (ProfilerRecorder probe, D3). That is stage work. **Enter Play mode only if the
  owner's pre-answers below authorize it;** otherwise stop after Phase 0 step 3 and ask. Never open or save a scene beyond what the
  probe needs, and never run a player build.

Read, in order:
1. `.claude/skills/worktree-run/SKILL.md`, `.claude/agents/spec-lead.md`, `Assets/_Vault/Memories/Code/WorktreeToolkit.md` (traps 17–26).
2. `Assets/_Vault/Tasks/AnimationPackage/AnimationPackage_Roadmap.md` §3.
3. The two specs' §0, §2 and §5 only.
4. The newest sections of `Assets/_Vault/Memories/Code/AnimationToolkit.md`: Materials (A96), Retarget (A97), Capture (A98) and all
   three "Parallel batch" lessons.
5. The previous prompt, `Assets/_Vault/Spencer/next-session-parallel-a96-a98-prompt.md`: its lead contract, gate syntax, cautions and
   inherited mechanics carry over unchanged unless this file says otherwise. Copy the lead contract into each lead's prompt verbatim.

**Owner pre-answers (fill before running; delete a line to be asked):**
- Models: lead `opus`, worker `sonnet` for both.
- Merges: `worktree.py merge` authorized for a99 and a100 once each reports ready with its gates green.
- A96 T13 (Create also assigns the material?): ______
- A97 T11 (Skipped row offers "add this tag to the rig"?): ______
- A98 T13 (captured PNGs uncompressed, or project defaults?): ______
- Play mode for A100 T0 on `DOTSTestScene`: authorized / not authorized.

## Phase 0 — stage prep

1. **Owner answers.** Each answered checkpoint is its own commit: its CHANGELOG version section, the spec's status line and §7,
   HANDOFF §4, the roadmap box ticked and its to-do line removed. An answer that asks for a change becomes a rework spec
   (`A96F`/`A97F`/`A98F`, next free minor) instead of a tick; spec it and ask before running it. Unanswered lines stay open.
2. **Preflight:** `worktree.py doctor --json` (stop on git failure, missing hooks or `brokerAlive` false); `git status` must show
   nothing that names a type either spec removes or renames (trap 21); delete any empty `.claude/worktrees/*` folder that is no
   longer locked.
3. **Baseline:** compile gate, then full suites. Expected: EditMode 857 (856 passed, standing Conformance_A only), PlayMode 285.
4. **Unity-bound T0 work:** A100 T0's probes (only if authorized), pasted into a100's prompt with the verdict on D3. A99's T0 is
   greps; the lead does it.
5. Record both registry sha256s (`DotsAnimationToolkitAnimEventKeyRegistry.asset`, `DotsAnimationToolkitTargetTagRegistry.asset`)
   and the Phase 0 results in each spec's §7; commit only those files; push.

## Phases 1–3

As the A96–A98 prompt, with these differences:
- **PlayMode fixtures:** the broker refuses them (trap 25). Tell both leads to send "gate needed: <id> <sha> <fixtures>" for any
  PlayMode fixture; gate it by hand (`stage-commit`, compile, fixture, `restore-trunk`) only while `stage.busyWith` is null, then
  run `git status` for stray folder metas (trap 26). A100's fixtures that need a `World` are PlayMode; expect this.
- **Merge order:** a99 → a100. Integration: enum members 13 and 14, toggles and panes after Capture, `tabToggles` sized 15, layout
  test lists, `index.md`, CHANGELOG 0.50.0 and 0.49.0 newest on top, `package.json` and the conformance pin at 0.50.0, allowlist.
- **Drives:** A99 on a scratch rig copy in `Assets/A99Scratch/` (bodies, limits, the drop simulation in the preview only; never
  `NewRig.asset` itself). A100 only in Play mode and only if authorized; otherwise prove the panel detached with its "Enter Play
  mode" state and say the numbers were not seen. Delete scratch after each drive and re-check both registry sha256s.
- **Close:** spec status lines, ticked boxes and §7 close logs; HANDOFF §4 paragraphs (A100 on top); AnimationToolkit.md sections;
  the roadmap; commit and push. Write close texts to a scratchpad Python file and run it.
- **End** with the next batch prompt (the roadmap's list ends at A100: write a prompt for whatever reworks the owner's answers
  opened, or say the roadmap is done) and one owner checkpoint message naming real assets and what to look at.

## Settled owner calls (do not re-ask)

No sound mixing; no package-side event handlers; names never numbers; sprite sheets are Texture2DArrays; nobody auto-writes arrays
into materials; unseen checkpoints close as accepted unless game breaking; A93F T10, A94F T12, A95F T10 accepted 2026-09-14.
