You are the **stage orchestrator** closing **Phase 6** of the DOTS Animation Toolkit roadmap: A105, A106 and A107,
the three editor UI passes still unticked in `Assets/_Vault/Tasks/AnimationPackage/AnimationPackage_Roadmap.md`. The
package reads `0.57.0`, trunk HEAD is `f1e4f533` or later. You alone touch `mcp__UnityMCP__*`, merge, capture and close.

**The trap this prompt exists for: all three specs are stale.** They were written 2026-09-15 against the *pre-A104*
captures. Since then eight trunk passes (`A105-1` … `A105-8`, commits `eb8869e3` → `29e9ba15`) and A108 (`0.57.0`)
restyled all fifteen tabs by hand and closed many of their findings without ever touching the specs. **A lead that runs
a spec as written will rebuild finished work.** So this session reconciles first, then builds only the residual.

| Spec id | Path | Version (A108 took `0.57.0`, so every spec moves up one) |
|---|---|---|
| `a105` | `Assets/_Vault/Tasks/AnimationPackage/A105_AssetTabsPass_Spec.md` | `0.58.0` |
| `a106` | `Assets/_Vault/Tasks/AnimationPackage/A106_PreviewTabsPass_Spec.md` | `0.59.0` |
| `a107` | `Assets/_Vault/Tasks/AnimationPackage/A107_TimelineTabsPass_Spec.md` | `0.60.0` |

## Read, in order

1. Root `CLAUDE.md`, `.claude/skills/worktree-run/SKILL.md`, `.claude/agents/spec-lead.md`,
   `Assets/_Vault/Memories/Code/WorktreeToolkit.md` (all the traps; 17, 18, 19 and 27 bite this run).
2. `Assets/_Vault/Tasks/Claude/StyleGuideReferenceImage.png` — look at it first — then
   `Docs/AnimationToolkit/EditorStyleGuide.md` whole.
3. `Assets/_Vault/Spencer/ui-pass-handoff-2026-09-15.md` whole: what the trunk passes already built (§1–§3b), the
   facts that cost real time (§5), and my standing directions (§6). Then `HANDOFF.md` §4's A108 paragraph only.
4. The three specs whole.
5. `Assets/_Vault/Spencer/next-session-a105-a106-prompt.md` — reuse its lead contract, gate syntax, batch additions
   and "already done" list verbatim; this prompt only adds what changed since.
6. `Assets/_Vault/Memories/Code/AnimationToolkit.md` → the A104 and A108 sections and the capture recipe.

## Owner answers for this run (fill before starting)

- I am at the PC. Captures need the Editor **focused** — if `EditorApplication.isFocused` is false, say so
  and do not save a capture; an unfocused grab is a stale frame.
- Models: lead `opus`, workers `sonnet`, verifiers `sonnet`.
- Merge of each spec is authorized once its wave gates are green and the one before it has merged.
- Play mode: not authorized. Leave the Editor open.
- Checkpoints: No check points, check in at the very end for verification. it will run smoother this way, if you have questions ask them upfront.

## Phase 0 — reconcile (trunk, no worktrees open)

1. **Preflight:** `ListAgents`; `python Packages/com.worktreetoolkit/Tools~/worktree.py doctor --json`; `git status`
   clean; `git worktree list` shows the stage only.
2. **Baseline:** compile gate; full EditMode and PlayMode once (962 / 304 at A108's close — the totals must not drop).
3. **Fresh before-captures** of all fifteen tabs into `Library/UIAudit/before-phase6-close/`. These replace
   `Library/UIAudit/before/` as the comparison set; the old ones show a UI that no longer exists.
4. **One `verifier` per tab, all fifteen spawned together.** Each brief carries: that tab's findings pasted verbatim
   from its spec's §1.1 (TP1–TP6, FB1–FB5, … CT1–CT5), the spec's §2 file list for that tab, and the instruction
   "for each finding report DONE / PARTIAL / OPEN with one `file:line` of evidence; grep the finding's strings and
   classes, read 40 lines either side, never a whole file; report ≤ 30 lines; never call Unity MCP". Decisions
   A105-D2/D3, A106-D2/D3/D4 and A107-D2/D3 get the same verdict.
5. **Cross-check every DONE against its capture yourself** — a class in the code is not a result on screen.
6. **Rewrite each spec:** strike the DONE findings in §1.1 with the closing commit, keep OPEN and PARTIAL, fix the
   status line and version, prune §5's tasks to the residual, and re-mark `[parallel-safe]`. Known open going in:
   Texture Packer's SG-D8 (never require a recipe), its second Bake (R12) and violet node header (R16); Actor
   Profiles' hover play (SG-D7) and `BuildAnimationBlock`'s cards; the cutscene panel (untouched by every pass,
   including its four icon+word buttons A108 left for A107). The Cutscenes magenta is **closed as a scene defect**
   (`Faceware.mat` names a deleted shader) — not the toolkit's, do not let a lead touch it.
7. **Fold tiny residuals.** A spec left with roughly five findings or fewer does not get a worktree: keep it for the
   Phase 3 trunk waves and say so in its §7.
8. Record Phase 0 in each spec's §7, **commit and push before anything forks** (trap 27: nothing new lands on trunk
   while a worktree is open — that includes this prompt's own edits).

## Phase 1 — three leads in parallel

A107's "runs alone" is overturned here, deliberately: its stated reason is that it edits files the other two stay out
of, which is the definition of parallel-safe. What genuinely had to wait — the final fifteen-tab audit — is a stage
step in Phase 3, after all three merge.

`/worktree-run` with the leads spawned together, models as answered above. Each prompt: spec id and path, worker
model, the Phase 0 results, the lead contract and gate syntax from the a105-a106 prompt, the handoff's §5 facts, and
this **seam**:

- `a105` owns `Editor/TexturePacker/`, `Editor/Flipbooks/`, `Editor/Materials/`, `Editor/Events/`,
  `Editor/ClipEditor/Authoring/`.
- `a106` owns `Editor/Retarget/`, `Editor/VatBaking/`, `Editor/Capture/`, `Editor/Ragdoll/`, `Editor/Stats/`,
  `Editor/Health/`.
- `a107` owns `Editor/ClipEditor/Panes/`, `Editor/ClipEditor/ActorEditor/`, `Editor/ClipEditor/Cutscene/`,
  `TimeRulerElement.cs`, `ClipEditorTransport.cs` and the window files A107-D4 grants.
- **Nobody touches** `Editor/ClipEditor/Shared/`, `ClipEditorWindow.uss`, `EditorStyleConformanceTests.cs`,
  `package.json`, `CHANGELOG.md`, `HANDOFF.md` or `Documentation~/`. A lead that needs a shared class, a token or an
  allowlist shrink writes it into its `### For integration` block; the stage adds all of them once, in Phase 3.
- Inside a lead: one worker per tab where the tab's files are disjoint (they are, in all three specs), at most two
  files per worker, **one gate per wave**, every gate carrying `EditorStyleConformanceTests` and
  `PackagingConformanceTests`. Three leads share one Editor, so "Unity is compiling" is a retry, never a verdict.
- Restyle only: remove no feature, lose no information. The one behaviour change in the batch is A105-D2.
- A lead never drives the Editor and never captures.

## Phase 2 — merge

`a105`, then `a106`, then `a107`, one at a time with `stage.busyWith` null between; push; `worktree.py remove` each.
A conflict goes to a `worker` with the worktree's absolute path, never a `spec-lead`.

## Phase 3 — integrate, look, fix, close (trunk)

1. Add every For-integration request to the shared layer, shrink the `Conformance_I` allowlist for files that left
   it, never raise a `Conformance_J` pin. Compile gate, then the full suites once.
2. **After-captures** of all fifteen tabs into `Library/UIAudit/after-phase6-close/`, plus the strip in icon mode.
3. **Judge hard against the reference image (SG-D5)** and fix what the captures show with parallel `worker` waves
   on trunk — the A108 loop: disjoint files, exact line numbers in the brief, gate once per wave, capture, look. This
   is where the folded residuals from Phase 0 step 7 get built. Measure in `execute_code` when a capture is ambiguous.
4. **The final fifteen-tab R01–R22 audit** A107 §6 asks for: a table per tab in A107's §7, every rule either met or
   named as an owner call.
5. Close: three CHANGELOG sections, `package.json` and the conformance pin at the highest version, one HANDOFF §4
   paragraph per spec (newest first) **and correct HANDOFF's header, which still says 0.53.0**, the three roadmap
   boxes, each spec's status line, `AnimationToolkit.md` traps, the `Documentation~` pages whose sentences describe
   an old layout (docs as workers).
6. Checkpoint as answered above, then write `next-session-rc1-spec-prompt.md`: the 1.0 release-candidate spec (demo
   scene, benchmark sample, bone-reparent guard, the known defects, customer-facing docs and changelog, the
   `package.json` store copy, the licence).

## Questions of mine still open — write them up with the captures, do not stop on them

- A108's three (Rigs and Ragdoll glyphs at 16px; Save grew to 28px to match Bake; D6 widened).
- R04's off-scale spacing on surfaces I already approved (handoff §3b).
- Which shader `Assets/Materials/UnitsLegacy/Faceware.mat` should use — the source of the Cutscenes magenta.
