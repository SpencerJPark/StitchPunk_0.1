# Session prompt — Amendment A80 (paste this whole block into a fresh session)

You are running **Amendment A80 — one clip set, one rig, every tab** on the DOTS Animation Toolkit
package in this repo. The spec is `Docs/AnimationToolkit/Amendment_A80_SharedAssetSelection_Spec.md`.
Read it in full, then its §3 "Read first" list in order, then
`Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4 — that protocol is binding. The spec's §2
decisions (A80-D1…D16) are settled; the three marked ⚠ are interpretations the owner will judge at
T13, not questions to ask now. Do not re-ask whether a catalog click should set the shared value (it
does — this reverses A76-D3 on the owner's instruction), whether the VAT Bake fields should be
removed or made live (live), whether the profiles column mirrors or extracts the rig catalog
(mirrors), or where the Clip Editor's two fields go (under each pane's header, unlabeled, same
element names).

**You are the orchestrator.** You are the only process that touches `mcp__UnityMCP__*`: you compile,
run tests, drive the Editor and commit. The tasks are sized for **`worker` subagents that edit files
only** (spawn `worker`, never `general-purpose`; `verifier` for read-only checks) — each gets the
spec path, its task's text, its "Read" line, the §4 block it builds, and the hard rules (no `var`,
no single-letter names, explicit types; one `<summary>` per file, three lines, no `<remarks>`, no
spec citations in shipped code). Each must finish inside its 40-turn cap: at most two files, named
line ranges (the read guard refuses a whole-file read over 300 lines), no browsing, "at turn 30 stop
editing and write your ≤30-line report", "never call any `mcp__UnityMCP__*` tool". The ledger line
the harness injects after each agent is your budget meter — a HIGH or OVER verdict means the next
brief in that family gets split further. Run the spec's waves:

- **Wave 1, four subagents at once:** T1, T2, T3, T4 (all `[parallel-safe]`, disjoint files). Wait
  for all four, then **one** compile gate and T1's two fixtures by `test_names`.
- **Wave 2, four subagents at once:** T5a, T5b, T5c, T5d (all additive — every panel gains `Bind`
  and keeps its `SetSource` so the window still compiles). Gate, then the five fixtures they
  touched by `test_names`. T5d is the largest; if its ledger says it was capped, read its diff and
  spawn a fresh worker with only the remaining catalog wiring — never SendMessage a capped agent.
- **Wave 3:** T6 (one subagent, the window). Gate.
- **Wave 4, you:** T7 (three lines in `VatBakeWindow.cs`), T8 (gate + fixtures + commit), T9
  (delete the `SetSource` scaffolding by grep), T10 (the §4.7 string sweep by grep/sed).
- **Wave 5:** T11 (one subagent, the layout assertion) — may run while you do T10.
- **T12 yourself** — full suites, the scripted drive, captures, docs, version — then stop at
  **T13**, the ⏸ owner checkpoint, with the message the spec gives.

Commit each wave with an `A80-Tn:` prefix naming every task in the message, staging paths
explicitly, never `git add -A`. Head was `9faa224b` and the tree clean on 2026-09-09. Push when green.

## State you are building on

- **0.26.0 (A78)** is head. **A79** is specced but *not* to be started — it waits on A78's owner
  checkpoint, and A80 does not touch anything A79 will (the VAT preview element). A80 takes the
  next version the changelog header rule resolves to.
- **A75 / A76 / A77** built the two catalog tabs the selection now rides on. `ClipSetsPanel` and
  `RigsPanel` already select, create, rename and delete; A80 only makes their selection *shared*.
- **A71** built the Actor Editor as three columns over two nested `TwoPaneSplitView`s (a 2026-09-08
  follow-up replaced the flex columns). A80 adds a fourth column on the left and a third split.
- Suite baselines: A78's closing HANDOFF §4 paragraph (EditMode 814, PlayMode 283). Re-measure at
  T0 — counts must not drop, and A80 adds six EditMode tests and edits two.

## The traps that will cost you a session if you rediscover them

- **`ReferenceEquals` in `ActiveAssetSelection`'s setter guard, never `==`.** Unity's `==` treats a
  destroyed asset as `null`, so a clear after a delete would be swallowed and every tab would keep
  a dead reference.
- **Every subscriber updates its control with `SetValueWithoutNotify`.** A notifying assignment
  re-enters the selection from inside its own event. The equality guard stops the infinite loop but
  not the double refresh — and the vault's "never rebuild a pane from a value-changed callback"
  rule still applies to the panel that *raised* the change.
- **Subscribe in the window's `CreateGUI` before `BindToolbar`, unsubscribe in `OnDisable`.** The
  window's `RestoreView` runs at the end of `CreateGUI` and writes the selection; handlers bound
  after it would miss the restore, and `rootVisualElement` can outlive the instance across a domain
  reload, so a forgotten `-=` double-fires after every recompile.
- **`hasUserSelectedThisSession` in `RigsPanel` goes away, and `ClipSetsPanel`'s
  `SelectedSet == null` guard with it.** They protected a catalog selection from being overwritten
  by the toolbar; with a shared selection, the catalog *is* the toolbar. Keeping either one makes
  the Rigs tab disagree with the Clip Editor after the first pick.
- **Every wave must compile on its own.** Wave 2 adds `Bind` and keeps `SetSource`; wave 3 switches
  the window; wave 4 deletes `SetSource`. Do not let a worker "tidy up" `SetSource` early.
- **The UXML element names do not change** (`clip-set-field`, `skinned-source-field`). Both are in
  `ClipEditorLayoutTests.RequiredElementNames` and in every `Q<>` in the window. Moving an element
  in the tree is free; renaming one is a session.
- **A nested `TwoPaneSplitView` inside a cover pane needs its own `minWidth`.** The Actor Profiles
  tab is hidden by USS class on every tab switch; a split view laid out at zero comes back with no
  handle. Outer 880, middle 680, right 460 — all three, not just the new one.
- **The two USS classes the toolbar fields used are also the Cutscene panel's.** Do not delete
  `.clip-editor__toolbar-label` / `.clip-editor__object-field`; `CutsceneEditorPanel.cs:394, :402`
  apply them.
- **`Conformance_G`**: `ActiveAssetSelection`, `ActorProfileCatalogColumn`, `ActorProfileSaveLocation`
  are instance classes — untouched by the suffix rule. `ActorProfileAssetUtility` is static and
  legal only because it lives in `Editor/ClipUtilities/`. Do not put it beside the panel.
- **`Conformance_F`**: one `<summary>` per file. `ClipEditorWindow.cs` already has its one; the
  renamed handlers (`ApplyRigSelection`, `ApplyClipSetSelection`) get **no** summary — delete the
  one on the old `OnSkinnedSourceChanged` (`:3516`).
- **Profile delete asks with `EditorUtility.DisplayDialog`, which the drive cannot answer.** T12
  deletes the scratch profile through `ActorProfileAssetUtility.TrashProfile` directly and asserts
  on the catalog, or runs the dialog by hand — say which.
- **`AssetDatabase` writes must be proven on a reloaded reference** (HANDOFF §3). The new profile's
  bookends are asserted on the asset loaded fresh from its path, on a scratch copy under
  `Assets/A80Scratch/`, deleted afterwards with its `.meta`.
- **`execute_code` is CodeDom C# 6** — no `using` lines, fully-qualified names, no `out var`.
  `resolvedStyle` / `layout` are stale in the call that built the UI; read them in a second call.
- **Captures scale by `pixelsPerPoint`**, need `EditorApplication.isFocused` true, and go black on
  a monitor left of the primary — move the window to positive x first. If a capture is stale, say
  so and record `resolvedStyle` numbers instead of claiming a screenshot you do not have.
- **A subagent that "cannot find" a member reads the file, not the spec again.** Names and line
  ranges were verified against `9faa224b` on 2026-09-09; if one drifted, grep, follow the code,
  and note the drift in the spec's §7.

## When you finish

Update the spec's status line, `Docs/AnimationToolkit/HANDOFF.md` §4 (one paragraph: what landed,
the three ⚠ interpretations the owner is asked to judge), the vault note section named in T12.5,
and stop with the T13 message. Do not start A79.
