# Next session prompt — A92 (written 2026-09-13, after A88)

Paste everything below the line into a fresh session. Fill in or delete the A88 T9 slot first.

---

You are running Amendment A92 (project-wide refactor operations) on the DOTS Animation Toolkit
package (Packages/com.dotsanimationtoolkit, head d4b7c769, version 0.38.0 after A88).
Spec: Assets/_Vault/Tasks/AnimationPackage/A92_RefactorOperations_Spec.md. Read it in full, then the
roadmap's section 3 protocol (Assets/_Vault/Tasks/AnimationPackage/AnimationPackage_Roadmap.md,
binding), then only the files the spec's section 3 names, at the line ranges it names. Do not read
the other specs in that folder.

You are the orchestrator: you alone compile, test, drive and commit through mcp__UnityMCP__*.
Follow the spec's task split and waves. Stop at its owner checkpoint.

## Step 0: A88 T9 answer (do this before A92)

Owner's A88 T9 answer: <PASTE HERE, or delete this line if unanswered>

- A88 is built (0.38.0): the Layer Events strip under the Actor Editor preview's transport. The two
  questions were: (1) does dimmed/hollow read as "will not fire"; (2) should the strip start collapsed.
- If answered, apply it first as A88-T10, following the A87-T10 precedent (commit 071c361c):
  - "Start collapsed" = change the default in `EditorPrefs.GetBool(ExpandedPrefKey, true)` in
    Editor/ClipEditor/ActorEditor/LayerEventStripElement.cs (:46) to false. The key is per machine, so
    anyone who already toggled it keeps their choice.
  - Visual-language changes: pins are drawn in `LayerEventRowElement.DrawLane` in the same file;
    `InactiveAlpha = 0.35f` is the dim, and the hollow ghost is a 0-alpha fill with a 35% outline.
  - Compile gate, then `LayerEventRowResolverTests` + `ActorEditorPanelTests` (10 tests).
  - CHANGELOG `## [0.38.0]` gains the change, and the spec status and §7 record the answer.
  - HANDOFF §4 A88 paragraph: "T9 answered". Roadmap: tick the A88 box, remove its to-do line, update
    the status line.
- If the owner questioned any §7 drift, especially 8 (a paused step counts as playing) or 9 (strip
  below the transport), treat it as an owner call and apply it the same way.
- If unanswered, leave the to-do line open and go straight to A92.

## Version

A92 takes 0.39.0, as specced. Confirm CHANGELOG's top section is `## [0.38.0]` at T0. If it has
moved, take the next free minor and correct the spec's status line.

## Owner calls already settled (do not re-ask)

- A87 D1: a paused or far seek fires nothing. A87 D5: a crossing flashes the pin as a 3 px outline in
  the pin's own colour for 120 ms, and the flash beats the selection outline.
- A90: accepted. A91: accepted as "works for now". The real player build is blocked by game-side
  compile errors and tracked in Assets/_Vault/Spencer/verify-a91-player-build.md. Never run a
  player build.
- A88: built with 13 T0 drifts settled in its spec's §7. Do not re-litigate them unless the T9
  answer above does.
- Standing: no sound mixing in the package; no package-side event handlers; names, never numbers,
  in every editor surface.

## A92-specific cautions (verify at T0, log drift in §7, escalate rather than quietly re-spec)

- A92 needs A84's Asset Reference Index. At T0, grep its public surface rather than trusting the
  spec's names. Note that one combined FindAssets beat six calls in A84.
- A92 writes across every clip, set, profile and cutscene in one undo step. Drive it ONLY against
  scratch assets you create in an Assets/ scratch folder, and delete that folder afterwards
  (AssetDatabase.DeleteAsset leaves no stray .meta).
- Never re-key, merge or retag the owner's real assets or registry entries.
- ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset is the owner's uncommitted work.
  A refactor drive must not write it. Use a fake IVocabularyRegistry or a scratch registry, and when
  copying an interface, check the copied range covers every member (A91 lost
  GeneratedConstantsPath to a range one line short).
- Never call AssetDatabase.SaveAssets(): it flushes the owner's unsaved editor state. Guard dirty
  flags before driving any code path that saves, and save only your scratch assets, via
  SaveAssetIfDirty(object).
- ClipEditorWindow is single-instance (GetWindow) and the owner's copy is docked. Do not drive,
  rearrange or load anything into it, and do not open scenes or enter Play mode. Prove UI one level
  down on a detached element. Capture only if EditorApplication.isFocused is true and without
  touching the docked window; otherwise say so.

## Inherited facts (do not re-derive)

- Baseline suites at d4b7c769: EditMode 838 with one pre-existing unrelated failure (Conformance_A:
  DotsAnimationToolkit.Editor has an extra Unity.RenderPipelines.Universal.Runtime), PlayMode 285.
  Totals must not drop.
- Version pins: package.json "version", and PackagingConformanceTests.cs
  Supplementary_PackageManifest_MatchesSection11Identity (the comment that lists every version, and
  the Assert.AreEqual).
- Conformance_E: UI Toolkit only (generateVisualContent/Painter2D are fine).
- Conformance_F: one <summary> per file, max 3 lines, and no "§", "amendment A<n>", "Phase A-G" or
  "rule V<nn>" anywhere, string literals included.
- Conformance_G/H: static class suffixes are Api, Builder, Sampler, Resolver, Math, Validation,
  Utility (Editor/ClipUtilities only) and Editing. The EditMode test asmdef already references
  Runtime, Authoring and Editor.
- Worker briefs (spawn `worker`, never general-purpose, from the repo root):
  - At most two files, named line ranges, and exact signatures pasted for anything another worker or
    you write. Use final literal strings only.
  - No mcp__UnityMCP__* calls and no git.
  - Include "at turn 30 stop editing and write your report", a report of 30 lines or fewer, and "add
    NO <summary> blocks" for edits to existing files.
  - Pasting full file bodies or exact edit anchors kept A88's four workers at 54-79k tokens each.
- After a wave, review the diff yourself for extra <summary> blocks, missing usings, var,
  single-letter names, and per-tick work that should be event-driven.
- Revert-to-fail: back up the file, mutate it, watch the fixture fail, restore from the backup, and
  check sha256. Chain multi-step shell scripts with `set -e`.
  - One compile can prove several mutations only if each hits a different test and no test asserts
    two rules. Unity's NUnit has no Assert.Multiple.
- Compile gate: refresh_unity (compile: request) returns "compiling". Poll
  mcpforunity://editor/state until compilation.is_compiling is false and
  last_domain_reload_after_unix_ms is later than your edit, then read_console (errors).
  - A "stale_status" block right after a reload clears by itself; retry.
  - "ping not answered" means retry. get_test_job can report a plugin disconnect; re-run.
  - Never run execute_code while a test job is running. Foreground sleeps are blocked.
- execute_code is CodeDom C# 6 (compiler: codedom):
  - No usings, fully qualified names, no out var or local functions.
  - Find package types by scanning AppDomain.CurrentDomain.GetAssemblies() and use reflection.
  - Extension methods are called statically, e.g. UnityEngine.UIElements.UQueryExtensions.Q(root,
    "name", (string)null).
  - Pass safety_checks=false only to delete your own scratch.
- Uncommitted files you must leave alone (stage paths explicitly, never git add -A):
  - Assets/ScriptableObjects/Animations/A63CheckpointCutscene.asset, NewClip.asset, "NewClip 1.asset"
  - ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset
  - ProjectSettings/EditorBuildSettings.asset
  - Assets/SceneDependencyCache/*
  - the untracked screenshot in Tasks/AnimationPackage/
  - Assets/_Vault/Tasks/NewPlans/TexturePacker_Roadmap.md.meta
  - Assets/_Vault/Spencer/verify-a91-player-build.md.meta
  - the .meta Unity creates for this prompt file
- Commit per wave with an "A92-Tn:" prefix naming every task; push when green.
- Close: spec status line and section 7, HANDOFF.md section 4 (one paragraph at the top of the
  queue), CHANGELOG ## [0.39.0], the Documentation~ page the spec names, a new "(A92, 0.39.0)"
  traps section appended at the end of Assets/_Vault/Memories/Code/AnimationToolkit.md (do not
  edit older dated sections), and the roadmap status line plus an A92 checkpoint line in the owner
  to-do block.
- End with the spec's owner checkpoint message, adapted to the drift you settled: name real assets
  the owner can try it on, say exactly what they should see, and ask the spec's questions.
