# Next session — spec the 1.0 release candidate (RC1)

You are the **stage orchestrator**. Phase 6 is closed: the editor UI pass is done and the package reads `0.60.0`.
**This session writes a spec, it does not build one.** The deliverable is
`Assets/_Vault/Tasks/AnimationPackage/RC1_ReleaseCandidate_Spec.md` plus the session prompt that builds it.

## Read, in order

1. Root `CLAUDE.md`, then `Docs/AnimationToolkit/HANDOFF.md` §4's **two newest paragraphs** (the Phase 6 close and
   the Faceware note) and §5–§8 whole — §5 is the standing owner directives, §7 the known gaps, §8 what has never
   been judged by eye.
2. `Assets/_Vault/Tasks/AnimationPackage/AnimationPackage_Roadmap.md` — Phase 6 is ticked; everything still open
   after it is RC1's candidate scope.
3. `Assets/_Vault/Memories/Code/AnimationToolkit.md` → the **Phase 6 close** section (traps that will bite a
   release pass) and the **A102 release readiness** section (`0.54.0`), which already did the first pass at
   Conformance_A, the `Samples~` fixture, the player build and the clean-project import. **RC1 builds on A102; do
   not re-derive it.**
4. `Assets/_Vault/Tasks/Claude/Code_Audit_2026-09.md` — the live audit; it says the package needs *release
   readiness, not features*.
5. `Docs/AnimationToolkit/EditorStyleGuide.md` §1 (the owner decisions) — RC1 must not undo any of them.

## What Phase 6 left you

Built and merged 2026-09-19: A105 `0.58.0`, A106 `0.59.0`, A107 `0.60.0`. EditMode **968/968**, PlayMode 304/304,
compile clean. Captures: `Library/UIAudit/before-phase6-close/` and `after-phase6-close/`.

**Open items RC1 must decide on — these are real, not hypothetical:**

| Item | State |
|---|---|
| **CT3** — the cutscene rail's opacity | Deliberate, with a design comment in both the C# and `ClipEditorWindow.uss`. R20 wants one translucent rail everywhere. **Needs the owner's word, not a pass.** |
| **CP5** — Capture's transport | Still ad hoc; it is not one of the files using the shared `TransportCoreElement`. Its lead could not take it without colliding with another worker on `CapturePanel.cs`. |
| **FB5** — the Flipbooks "unimported" badge | Blocked on a shared-layer gap: `ToolkitCatalogColumn<TAsset>` has no per-row element hook, so a catalog row cannot carry a badge. Add the hook, then the badge. |
| **CE1** — the Clips list share | `fixed-pane-initial-dimension="150"` is set, but with the pane's own header and asset field the list still shows about two rows. Needs a real measurement, not another guess at a number. |
| **CE8 residue** — one `-10` ruler tick | The empty timeline's ruler starts at 0 now (a negative `viewPan` persisted in `EditorPrefs` was being applied against the placeholder frame count of 30), but a single `-10` label still draws into the track-header gutter. |
| **Flipbooks stray badge** | An empty meta badge renders as a lone "·" beside "No flipbook" when nothing is selected. |
| **CE4** — the Clip Editor viewport rail | Still resolves `EditorGUIUtility.IconContent` with no `ApplyIconTone`, so its icons stay mixed-colour while every other rail is one tone. |
| **Health's `V38:` prefix** | A finding's code is a meta badge now, but that family still prints a second code inside its title. |
| **Two magenta `Quad`s in `TestArea`** | Root-level renderers with a genuinely null material. Scene, not package — but they are in every Cutscenes screenshot. Owner call on what they should be. |
| `ViewportFrameElement.SetEmptyState` | Takes no action, so R13's "the action that fills it" cannot be met on any viewport empty state. A106 asked for an overload. |
| `RigsPanel.SetFocusedRowFacesDirection` | Duplicates `RigAssetUtility`'s Undo/SetDirty/SaveAssetIfDirty shape; fold it in. |
| `RecipeCatalogColumn.cs:25` | Sidebar button still says "Save" where the header now says "Save as recipe". |
| Docs | `ragdoll.md` and `capture-tab.md` are not *wrong*, but they never describe the new layouts, and **there is no VAT Bake page at all** — the nearest is `rigged-characters.md` §5. |

## What the RC1 spec must cover

The prompt that generated this list asked for: **demo scene, benchmark sample, bone-reparent guard, the known
defects, customer-facing docs and changelog, the `package.json` store copy, and the licence.** Spec each as its own
numbered task with its own fixtures and acceptance:

1. **Demo scene** — one scene a buyer opens that shows the toolkit working, with nothing from `Assets/` in it. It
   must live under `Samples~` and survive the clean-project import A102 already built a check for.
2. **Benchmark sample** — the performance claim the store page will make, measured, with the scene that produces
   the number. Decide what is measured (actor count at a frame budget is the obvious one) before writing the task.
3. **Bone-reparent guard** — the known correctness hole. Specify what happens when a rig's hierarchy changes under
   a baked clip: detect, warn in Health, and say whether a rebake is offered.
4. **The known defects table above** — each one either fixed, or listed in a shipped `KNOWN_ISSUES.md` with a
   reason. **Nothing silently dropped.**
5. **Customer-facing docs** — `Documentation~/` read end to end as a buyer, not as an author. The gaps above are
   the starting point, not the whole job.
6. **`package.json` store copy** — `displayName`, `description`, `keywords`, `documentationUrl`, `changelogUrl`,
   `licensesUrl`, author, and the Unity version floor. Note that `PackagingConformanceTests` pins `version`, so the
   RC1 build moves the pin too.
7. **Licence** — the actual decision and the file. **This is an owner call; ask it early in that session, not at
   the end**, because the licence text has to ship inside the package.

## Rules for the spec you write

- **Every task names its files and its fixtures.** The repo rule stands: if you cannot revert the fix and watch the
  test fail, delete the test. No coverage-chasing.
- **Mark `[parallel-safe]` honestly** and give a file seam, because this will be run through `/worktree-run` with
  one `spec-lead` per group of tasks and `sonnet` workers.
- Record every settled owner decision as `RC1-D<n>` in §1 so the build session never re-asks it.
- Put the questions you *cannot* settle in a short §0 "ask the owner first" list — the licence is one of them.

## Standing owner calls — do not re-ask

- Names never numbers; no manual asset wiring; no package-side event handlers; no sound mixing; sprite sheets are
  `Texture2DArray`s, never atlases.
- Follow the style guide religiously; the reference image is the target. **Restyle never removes a feature.**
- Judge by captures — do not report a visual result that has not been photographed.
- Unseen checkpoints close as accepted; escalate only what is genuinely game-breaking.
- Do not run a real player build unattended; that check happens with the owner at the PC.

## Traps that will bite this specific session

- **Do not commit anything to trunk while a worktree is open** (WorktreeToolkit trap 27) — including `.meta` files
  the Editor generates. A lead cannot make a `.meta` at all; land them at integration.
- The gate CLI's `--edit-mode` takes **one fixture per flag, repeated**. Comma-separated is silently refused as
  "no tests matched".
- A modal Unity dialog freezes the broker *and* MCP at once and looks like a dead bridge. Enumerate the Unity
  process's windows (class `#32770`) before blaming the transport.
- Before judging any tab by capture, **drive its selection first** — an unselected tab photographs its empty state.
