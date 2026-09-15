# Next session prompt — A93 (written 2026-09-14, after A92)

Paste everything below the line into a fresh session. Fill in or delete the two answer slots first.

---

You are running Amendment A93 (Events tab) on the DOTS Animation Toolkit package
(Packages/com.dotsanimationtoolkit, version 0.39.0 after A92). Spec:
Assets/_Vault/Tasks/AnimationPackage/A93_EventsTab_Spec.md. Read it in full, then the roadmap's
section 3 protocol (Assets/_Vault/Tasks/AnimationPackage/AnimationPackage_Roadmap.md, binding), then
only the files the spec's section 3 names, at the line ranges it names. Do not read the other specs in
that folder.

You are the orchestrator: you alone compile, test, drive and commit through mcp__UnityMCP__*. Run it
in this session with `worker` subagents, not through /worktree-run. Follow the spec's task split and
waves. Stop at its owner checkpoint (T16).

## Step 0: owner answers (apply before A93, each as its own commit)

Owner's A88 T9 answer: <PASTE HERE, or delete this line if unanswered>
Owner's A92 T10 answer: <PASTE HERE, or delete this line if unanswered>

- A88 (0.38.0) is the Layer Events strip under the Actor Editor preview's transport. Its questions:
  does dimmed/hollow read as "will not fire", and should the strip start collapsed?
  - "Start collapsed" means changing `EditorPrefs.GetBool(ExpandedPrefKey, true)` to false in
    Editor/ClipEditor/ActorEditor/LayerEventStripElement.cs.
  - Pins are drawn in `LayerEventRowElement.DrawLane` in that file. `InactiveAlpha = 0.35f` is the dim;
    the hollow ghost is a 0-alpha fill with a 35% outline.
  - Gate: compile, then `LayerEventRowResolverTests` and `ActorEditorPanelTests` (10 tests). Commit as
    A88-T10, following the A87-T10 precedent (071c361c).
- A92 (0.39.0) is refactor operations. Its question: do the dialog wording and the four entry points
  read right?
  - Dialog strings live in Editor/ClipEditor/Editing/RefactorPromptEditing.cs.
  - Entry points: EventMarkerContextMenu.cs ("Change key everywhere…"), AnimEventKeyRegistryEditor.cs
    ("Merge into…"), TargetTagRegistryEditor.cs ("Replace in clips with…"), RigsPanel.cs
    (`PopulateTagButtonContextMenu`).
  - Gate: compile, then `RefactorTargetResolverTests` (2 tests). Commit as A92-T11.
- For either spec, CHANGELOG gains the change under that version's section, and the spec's status
  line and section 7 record the answer. HANDOFF section 4: mark that spec's paragraph "T9 answered" or
  "T10 answered". Roadmap: tick its box, remove its to-do line and update the status line.
- If either is unanswered, leave its to-do line open.

## Version

A93 takes 0.40.0, as specced. At T0, confirm CHANGELOG's top section is `## [0.39.0]`. If it has moved,
take the next free minor and correct the spec's status line.

## Owner calls already settled (do not re-ask)

- A87 D1: a paused or far seek fires nothing. A87 D5: a crossing flashes the pin as a 3 px outline in
  its own colour for 120 ms, and the flash beats the selection outline.
- A90 accepted. A91 accepted as "works for now". The real player build is blocked by game-side
  compile errors (tracked in Assets/_Vault/Spencer/verify-a91-player-build.md). Never run a player
  build.
- A88: 13 T0 drifts settled in its spec's §7. A92: 13 T0 drifts settled in its §7. Cutscene part tracks
  are included in Replace tag, and a merge leaves int/float payloads raw with a dialog warning (owner
  calls 2026-09-14). Do not re-litigate either unless the Step 0 answers do.
- Standing: no sound mixing in the package; no package-side event handlers (A93 D-level: the routing
  table is data, and the consumer stub is a file written into the host's project); names, never
  numbers, in every editor surface.

## A93-specific cautions (verify at T0, log drift in §7, escalate rather than quietly re-spec)

- Grep A93's predecessors' public surfaces rather than trusting the spec's names:
  - A82: `ToolkitCatalogColumn<TAsset>`
  - A84: `AssetReferenceIndex.ReferencesToEventKey`, `SummarizeForDialog`
  - A85: payload fields on `AnimEventKeyEntry` (`intParamLabel`, `intParamValueNames`,
    `floatParamLabel`, `floatParamUnit`)
  - A87: `previewClip`
- A92 is built, so the spec's optional "Merge lands on the row menu" is available:
  `RefactorPromptEditing.ShowMergeIntoMenu(VisualElement anchor, uint fromKey, Action onApplied)`.
  Its merge always acts on the project registry.
- ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset holds the owner's uncommitted work.
  - The Events tab edits that registry, so no drive may write it.
  - Record its sha256 at T0 and check it again after the drive.
  - Drive new-key/payload/route flows against a `CreateInstance` registry or a scratch asset.
  - `VocabularyRegistryProvider.Persist` is a no-op for anything but the project instance.
- "Generate consumer stub…" writes a file into the host project. Drive it only into an Assets/ scratch
  folder, then delete the folder (AssetDatabase.DeleteAsset leaves no stray .meta).
- Never call AssetDatabase.SaveAssets(): it flushes the owner's unsaved editor state. Save only your
  scratch assets, via SaveAssetIfDirty(object).
- T12 wires `ClipEditorTab.Events` into ClipEditorWindow.
  - The window is single-instance (GetWindow) and the owner's copy is docked. Do not drive,
    rearrange or load anything into it.
  - Do not open scenes or enter Play mode.
  - Prove the panel one level down on a detached `EventsPanel`.
  - Capture only if EditorApplication.isFocused is true and without touching the docked window;
    otherwise say so.
- `EditorUtility.DisplayDialog` is modal. Never reach one from execute_code; call the operation below
  the dialog instead.
- When copying an interface or signature into a brief, check the copied range covers every member.

## Inherited facts (do not re-derive)

- Baseline suites at the A92 close:
  - EditMode 840, with one pre-existing unrelated failure (Conformance_A: DotsAnimationToolkit.Editor
    has an extra Unity.RenderPipelines.Universal.Runtime).
  - PlayMode 285.
  - Totals must not drop.
- Version pins: package.json "version", and PackagingConformanceTests.cs
  Supplementary_PackageManifest_MatchesSection11Identity (the comment listing every version, and the
  Assert.AreEqual).
- Conformance_E: UI Toolkit only (OnGUI, GUILayout and Handles. are banned; DisplayDialog,
  generateVisualContent and Painter2D are fine).
- Conformance_F: one <summary> per file, max 3 lines. No "§", "amendment A<n>", "Phase A-G", "rule V<nn>",
  <remarks> or <para> anywhere, string literals included.
- Conformance_G/H: static class suffixes are Api, Builder, Sampler, Resolver, Math, Validation,
  Utility (Editor/ClipUtilities only) and Editing, or a name on the plain-noun allowlist. The
  EditMode test asmdef already references Runtime, Authoring and Editor.
- Worker briefs (spawn `worker`, never general-purpose, from the repo root):
  - At most two files, named line ranges, and exact signatures pasted for anything another worker or
    you write. Use final literal strings only.
  - No mcp__UnityMCP__* and no git.
  - Include "at turn 30 stop editing and write your report", a report of 30 lines or fewer, and "add
    NO <summary> blocks" for edits to existing files.
  - Write committed stubs for every new shared type at T1 so workers compile against real signatures.
    A92's four workers ran at 55-69k tokens each this way.
  - A task that is ten lines is faster done by you than briefed.
- After a wave, review the diff yourself for extra <summary> blocks, missing usings, var,
  single-letter names, and per-tick work that should be event-driven.
- Revert-to-fail: back up the file, mutate it, watch the fixture fail, restore from the backup, and
  check sha256. Chain multi-step shell scripts with `set -e`. One compile can prove several mutations
  only if each hits a different test.
- Compile gate: refresh_unity (compile: request) returns "compiling". Poll
  mcpforunity://editor/state until compilation.is_compiling is false and
  last_domain_reload_after_unix_ms is later than your edit, then read_console (errors).
  - A "stale_status" block right after a reload clears by itself; retry.
  - "ping not answered" means retry. get_test_job can report a plugin disconnect; re-run.
  - Never run execute_code while a test job is running. Foreground sleeps are blocked.
- execute_code is CodeDom C# 6 (compiler: codedom):
  - No usings, fully qualified names, no out var or local functions (a `System.Func` delegate works).
  - Find package types by scanning AppDomain.CurrentDomain.GetAssemblies() and use reflection.
  - Extension methods are called statically.
  - `GetInstanceID()` fails compilation on 6.5; use GetEntityId or find objects by name.
  - Pass safety_checks=false only to delete your own scratch.
- Git:
  - `git commit` takes the WHOLE index, not just the paths you added. Run `git diff --cached --stat`
    before every commit; A92's first commit swept in a deletion another session had staged.
  - Stage paths explicitly, never `git add -A`.
  - Uncommitted files you must leave alone:
    - Assets/ScriptableObjects/Animations/A63CheckpointCutscene.asset, NewClip.asset, "NewClip 1.asset"
    - ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset
    - ProjectSettings/EditorBuildSettings.asset
    - Assets/SceneDependencyCache/*
    - the untracked screenshot in Tasks/AnimationPackage/
    - the untracked .meta files under Assets/_Vault/Spencer/ and Assets/_Vault/Tasks/NewPlans/
- Commit per wave with an "A93-Tn:" prefix naming every task; push when green.
- Close:
  - The spec's status line and section 7.
  - HANDOFF.md section 4: one paragraph at the top of the queue.
  - CHANGELOG ## [0.40.0], and the Documentation~ page the spec names.
  - A new "(A93, 0.40.0)" traps section appended at the end of
    Assets/_Vault/Memories/Code/AnimationToolkit.md. Do not edit older dated sections.
  - The roadmap status line, plus an A93 checkpoint line in the owner to-do block.
  - A next-session prompt for A94 in Assets/_Vault/Spencer/.
- End with the spec's owner checkpoint message, adapted to the drift you settled. Name real assets the
  owner can try it on, say exactly what they should see, and ask the spec's questions.
