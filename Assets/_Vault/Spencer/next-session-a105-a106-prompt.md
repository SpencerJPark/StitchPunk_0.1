You are the **stage orchestrator** for **A105 + A106**, two package specs run **in parallel** through the Worktree
Toolkit (`/worktree-run`, `Packages/com.worktreetoolkit`) with one `spec-lead` each in its own worktree. You alone
touch `mcp__UnityMCP__*`, merge, capture, close and run the checkpoints.

| Spec id | Path | Version |
|---|---|---|
| `a105` | `Assets/_Vault/Tasks/AnimationPackage/A105_AssetTabsPass_Spec.md` | `0.57.0` |
| `a106` | `Assets/_Vault/Tasks/AnimationPackage/A106_PreviewTabsPass_Spec.md` | `0.58.0` |

Why: A104 built the shared layer (`0.56.0`, merged `dbaf12c8`, closed `77b0354c`) and its §7 S3 audit lists **nine
findings the shared change could not fix**. A105 owns the asset tabs (Texture Packer, Flipbooks, Clip Sets, Rigs,
Materials, Events) and A106 the preview tabs (Retarget, VAT Bake, Capture, Ragdoll, Stats, Health). A107 follows
alone. The binding reference is the owner-approved style guide `Docs/AnimationToolkit/EditorStyleGuide.md`.

Read, in order:
1. `CLAUDE.md` (root), `.claude/skills/worktree-run/SKILL.md`, `.claude/agents/spec-lead.md`,
   `Assets/_Vault/Memories/Code/WorktreeToolkit.md` (**all 27 traps** — 27 is new and cost this run sixteen minutes).
2. `Docs/AnimationToolkit/EditorStyleGuide.md` whole, then both specs whole.
3. `Assets/_Vault/Tasks/AnimationPackage/A104_StyleFoundation_Spec.md` §7 — the S3 audit table, findings F1–F9 and
   the `### For integration` block naming every shared class, builder and token the tabs must reuse.
4. `Assets/_Vault/Spencer/next-session-a104-prompt.md`: its lead contract, gate syntax, cautions and inherited
   mechanics carry over verbatim, with the additions below.
5. `Assets/_Vault/Memories/Code/AnimationToolkit.md` → the **A104 section** (token, selector and material traps) and
   "A session CAN see the editor UI" (capture recipe, focus trap, linear colour trap).
6. **`Assets/_Vault/Tasks/Claude/StyleGuideReferenceImage.png`** — the owner's own capture of the style guide's
   rebuilt Ragdoll tab, added 2026-09-15. **Look at it before designing anything.** It is the target composition,
   and it shows what the rules alone do not: three depths (window column → darker list body → darker still
   viewport), the counts as outline pill badges beside the Rig field, a segmented control in the viewport header,
   inspector cards with collapsible headers, an aligned label column, a search field under the pane header, and a
   status footer with an amber tone dot. A106 owns that tab and should land visibly close to this image.

## The owner's S5 answers (2026-09-15 — settled, do not re-ask)

- **Tab strip:** the dead space after Health was wrong; the strip is now **centred**. At narrow widths the tabs
  become **icons with the word in the tooltip** rather than clipped or scrolled words. Both were built as the A104
  follow-up — do not redo them, and do not restyle the tab list in a tab spec.
- **Tone:** *"I would like it to use more dark in the backgrounds… if it's all the same one note it becomes very
  hard to read."* The style guide's own structure is the answer and is now in the shared layer: the **column** stays
  `--toolkit-window`, the **list body** takes `--toolkit-surface` with a 1px divider above, through the new
  `toolkit-list-surface` class. **Every tab in A105 and A106 applies this two-tone treatment** to its own list
  bodies, status footers and any other flat expanse that currently reads as one grey. This is the owner's headline
  complaint: a tab that still reads as one note has failed its pass.
- **Not asked again:** the owner did not follow the "anything else in the shared layer" question, so the shared
  layer is settled as built. A tab that needs a *new* shared class reports it for the stage rather than inventing a
  private one.

## Findings each spec must fix (A104 §7 S3, captures in `Library/UIAudit/after-a104/`)

**A105 (asset tabs):**
- **F1 — Texture Packer:** the image list still draws the old boxed two-line row with a thumbnail. It is
  `ImageCatalogColumn`, which never went through `ToolkitCatalogColumn`. Give it the shared flat row.
- **F2 — Flipbooks:** the name column is so narrow every name truncates ("EarA…", "FacialHai…") while the meta takes
  most of the row. The owner's call: **widen the name, let the meta shrink.**
- **F3 — Events:** key rows sit roughly four row-heights apart, so four keys fill the column. The shared row sets
  `min-height`; `EventKeyCatalogColumn` still stacks its content.
- **F4 — Clip Sets:** the full asset path repeats on all eleven clip rows (R03).
- **F5 — Rigs:** target rows are still individually boxed with truncated node paths — SG-D6's chips plus a detail
  card is A105's to build.
- **F6 (its tabs) — Events:** empty panes are a plain sentence rather than a designed empty state (R13).

**A106 (preview tabs):**
- **F6 (its tabs):** "No profile assigned." and its kin become real empty states (R13).
- **F7 — VAT Bake, Capture, Health:** the primary stretches the full column width, and Health's Rebake/Locate sit
  inside cards as full-width bars, so none of them reads as a button (R10, R12).
- **F8 — Ragdoll:** the body picker above the bodies list is a tall empty box.
- **Ragdoll is also the style guide's reference composition** — judge it against the rendered example, not just the
  rules.

**Neither spec — A107 owns it:** **F9**, the Cutscenes viewport still renders magenta. That viewport draws the
**open scene** through a hidden camera, so A107 must first prove whether the source is scene materials or a toolkit
proxy. Do not let a lead "fix" it blind.

## Owner answers for this run (fill before spawning)

- Models: lead `opus`, workers `sonnet`.
- Merge of each spec is authorized once its wave gates are green.
- The owner is [at the PC / away]. Present → both checkpoints are real stops with before/after pairs; away → close
  them under the standing rule and keep the questions in HANDOFF §4.
- Play mode: not authorized.

## Phase 0

1. **Preflight:** `ListAgents`; `worktree.py doctor --json`; `git status` clean; no stale worktree folders.
2. **Baseline:** compile gate; EditMode 876, PlayMode 285; CHANGELOG top `## [0.56.0]`; both registry sha256s.
3. **Before captures:** `Library/UIAudit/after-a104/` **is** the before set for this batch. Copy it to
   `Library/UIAudit/before-a105-a106/` so the after run has a fixed comparison.
4. Record Phase 0 in both specs' §7; commit; push, so both worktrees branch from it.

## Phase 1 — spawn

Two `spec-leads`, `model: opus`, background, spawned together. Each prompt carries the spec id and path,
`worker model: sonnet`, the Phase 0 results, the gate syntax, the lead contract verbatim, the S5 answers above, its
own findings list, and these **batch additions**:

- **File ownership between the two leads.** They share the package, so name the seam explicitly: A105 owns
  `Editor/Flipbooks/`, `Editor/Events/`, `Editor/ClipEditor/Authoring/` and `ImageCatalogColumn`; A106 owns
  `Editor/Ragdoll/`, `Editor/Capture/`, `Editor/VatBaking/`, `Editor/Health/`, `Editor/Stats/` and
  `Editor/Retarget/`. **Neither touches `Editor/ClipEditor/Shared/`** — the shared layer is the stage's. A tab that
  needs a new shared class or builder puts it in its `### For integration` block and the stage adds it once, after
  both merge.
- **Gates:** every wave gate carries `EditorStyleConformanceTests` and `PackagingConformanceTests` (traps 17, 19).
  Parallel leads will see "Unity is compiling" refusals — a retry, never a verdict (trap 18).
- **Trap 27 is the stage's job, not theirs:** do not commit any new file to trunk while these worktrees are open.
  If a gate sits with no result for more than about five minutes, the lead reports it and the stage clears it.
- **Captures are the stage's.** A lead never drives the Editor.

## Phase 3 — merge, capture, close

1. Merge `a105`, then `a106` (one at a time, `stage.busyWith` null between), push, remove both worktrees.
2. Wire any shared-layer additions the For-integration blocks ask for, then compile gate and the full suites.
3. **After captures** of all 15 tabs into `Library/UIAudit/after-a105-a106/`, the Editor focused. The docked Clip
   Editor is a background tab behind the Game View: bring it forward, capture, then restore **both** the Game View
   and the window's own active tab.
4. **Look at every capture and judge hard (SG-D5).** Fill each spec's audit table, and check the owner's headline
   complaint first: does each tab now read as two tones rather than one flat grey?
5. Close: CHANGELOG `0.57.0` and `0.58.0`, `package.json` and the conformance pin at the higher one, HANDOFF §4,
   roadmap boxes, both specs' status lines, `AnimationToolkit.md` traps.
6. **Checkpoints**, then write `next-session-a107-prompt.md` — A107 is the timeline tabs (Clip Editor, Actor
   Profiles, Cutscenes), the F9 magenta investigation, and the **final 15-tab audit** that closes Phase 6.

## Settled owner calls (do not re-ask)

- **Product:** names never numbers; no manual asset wiring; no package-side event handlers; no sound mixing; sprite
  sheets are Texture2DArrays.
- **Process:** unseen checkpoints close under the standing rule only when the owner is away; visual-first tools.
- **Style guide:** SG-D1 tab list; SG-D2 hybrid surfaces; SG-D3 Unity-theme neutrals, blue for selection only;
  SG-D4 mixed density; SG-D5 judge hard; SG-D6 Rigs chips + detail card; SG-D7 Actor Profiles hover play;
  SG-D8 Texture Packer never requires a recipe.
