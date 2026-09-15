You are the **stage orchestrator** for **A104 — Style foundation**, one package spec run through the Worktree Toolkit
(`/worktree-run`, `Packages/com.worktreetoolkit`) with one `spec-lead` in its own worktree. You alone touch
`mcp__UnityMCP__*`, merge, capture, close and run the checkpoint.

| Spec id | Path | Version |
|---|---|---|
| `a104` | `Assets/_Vault/Tasks/AnimationPackage/A104_StyleFoundation_Spec.md` | `0.56.0` |

Why: the owner judged every editor tab "pretty bad looking" on 2026-09-15 and **approved** the style guide
`Docs/AnimationToolkit/EditorStyleGuide.md` (rendered: `EditorStyleGuide.html`, artifact
https://claude.ai/artifact/T5xAE4KMz6AmBAuwUihYvE). A104 builds the shared layer (tokens, tab list, segmented
control, buttons, flat rows, cards, badges, empty states, property rows, the neutral preview material). A105 + A106
(parallel) and then A107 fix each tab on top of it. The 15 before-captures are in `Library/UIAudit/`.

Read, in order:
1. `CLAUDE.md` (root), `.claude/skills/worktree-run/SKILL.md`, `.claude/agents/spec-lead.md`,
   `Assets/_Vault/Memories/Code/WorktreeToolkit.md` (all traps).
2. `Docs/AnimationToolkit/EditorStyleGuide.md` whole, then the A104 spec whole.
3. `Assets/_Vault/Tasks/AnimationPackage/AnimationPackage_Roadmap.md` §2 and §3.
4. `Assets/_Vault/Spencer/next-session-a103-prompt.md`: its lead contract, gate syntax, cautions and inherited
   mechanics carry over verbatim, with the A104 additions below.
5. `Assets/_Vault/Memories/Code/AnimationToolkit.md` → "A session CAN see the editor UI" (the capture recipe, the
   focus trap, the linear colour trap).

**Owner pre-answers:**
- Models: lead `opus`, worker `sonnet`. Skip the skill's Ask step.
- Merge of `a104` is authorized once W1–W3 gates are green.
- Checkpoint S5 is a **real stop** if the owner is at the PC; close it under the standing rule only if they are away.
- Play mode: not authorized.

## Phase 0 (A104 §6 S0)

1. **Preflight and baseline:**
   - `ListAgents`; `worktree.py doctor --json`; `git status` clean.
   - Compile gate; EditMode 873, PlayMode 285; CHANGELOG top `## [0.55.0]`; both registry sha256s.
2. **Before captures:** copy `Library/UIAudit/0*.png` and `1*.png` into `Library/UIAudit/before/`.
3. **The `var()` probe**, exactly as S0 describes: two execute_code calls, and delete the probe file after. Paste the
   verdict into the lead prompt and A104 §7.
4. Record Phase 0 in A104 §7; commit; push.

## Phase 1 — spawn

One `spec-lead`, `model: opus`, background. Its prompt carries:
- spec id and path;
- `worker model: sonnet`;
- the Phase 0 results and the probe verdict;
- the gate syntax (namespace-qualified; `EditorStyleConformanceTests` and `PackagingConformanceTests` in every gate);
- the lead contract verbatim;
- these **A104 additions**:
  - **Window ownership:** A104 runs alone, so the lead **owns** `ClipEditorWindow.uxml` (tab strip classes),
    `ClipEditorWindow.cs` (stylesheet loading and the active-tab class line) and `ClipEditorWindow.uss` (the rules
    A104-D10 names). Everything else stage-owned stays yours.
  - **Waves:** W1 is five workers (T1–T5), W2 four (T6, T7, T8a, T8b), W3 one (T9) and then T10.
    - **Token names:** paste A104-D2 into every W1 brief.
    - **Selectors:** T2's must beat `.unity-toolbar-toggle` / `.unity-button`.
  - **Revert-to-fail:** one mutation commit per wave that has fixtures (W1: F1–F3).
  - **Shared-layer rule:** no tab-specific layout fixes. A tab that looks worse after the shared change is a finding
    for A105–A107, recorded in §7, not fixed here.

## Phase 2 — while running

As in the A103 prompt: stay quiet; gates run through the broker; never refresh or run tests on the stage while
`stage.busyWith` is set; a capped lead gets a `worker`, never a second `spec-lead`.

## Phase 3 — merge, capture, close (A104 §6 S2–S5)

1. **Merge:** merge `a104`, push, remove the worktree. Compile gate; full suites.
2. **After captures:**
   - **Focus:** check `EditorApplication.isFocused`. If it is false, tell the owner "click into Unity for a minute" and
     arm the waiting capture chain (the recipe in the vault note; step through all 15 tabs, 45 repaints each, linear
     RenderTexture, flip, restore the active tab).
   - **Save:** to `Library/UIAudit/after-a104/`.
   - **Look:** at every capture. Fill A104 §7's G1–G8 × tab table. **Judge hard:** list everything that still falls
     short of the style guide's reference composition, even where a G row passes.
3. **Close:**
   - CHANGELOG `## [0.56.0] — Style foundation`; `package.json` + conformance pin `0.56.0`;
   - HANDOFF §4 paragraph; roadmap A104 box ticked; A104 status line and boxes;
   - `AnimationToolkit.md` traps (including the probe verdict);
   - the style guide §5 note.

   Close texts go through a scratchpad Python file. Commit; push.
4. **Checkpoint S5.** Show the owner four before/after pairs (Flipbooks, Ragdoll, Health, Clip Sets) and ask A104's
   Q1–Q3. Apply the answers as a follow-up commit, or record them in A105/A106 §1 before those specs run.
5. **End:** write `Assets/_Vault/Spencer/next-session-a105-a106-prompt.md` for the parallel A105 + A106 batch, in this
   file's shape with two leads and the owner's S5 answers folded in.

## Cautions

- **Owner's work:** never stage what you did not create; never call `AssetDatabase.SaveAssets()`; never drive
  `MaleCitizen.prefab`, `NewRig.asset` or a real registry.
- **Tab switching:** switching the docked Clip Editor's tabs for captures is authorized by the owner (2026-09-15);
  restore the active tab after.
- **Focus trap:** a capture taken while the Editor is unfocused is stale. Do not save it under a capture name; ask the
  owner to focus Unity.
- **Stray folder:** the stage worktree folder must not keep `Assets/Generated/DotsAnimationToolkit/A104Probe.uss`
  after S0.

## Settled owner calls (do not re-ask)

- **Product:** names never numbers; no manual asset wiring; no package-side event handlers; no sound mixing; sprite
  sheets are Texture2DArrays.
- **Process:** unseen checkpoints close under the standing rule only when the owner is away; visual-first tools.
- **The style guide:** SG-D1 shadcn tab list; SG-D2 hybrid surfaces; SG-D3 Unity-theme neutrals with blue for
  selection only and a light-neutral primary; SG-D4 mixed density; SG-D5 judge hard; SG-D6 Rigs chips + detail card;
  SG-D7 Actor Profiles hover play; SG-D8 Texture Packer never requires a recipe.
