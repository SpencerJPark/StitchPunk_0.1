---
tags: [task, claude, code, audit, roadmap, animation, release]
related: "[[Systems_Gap_Audit_2026-08]], [[Code_Audit_2026-07]], [[Tasks/Plans/README]], [[Tasks/AnimationPackage/AnimationPackage_Roadmap]]"
created: 2026-09-15
status: active
---

# Code Audit — September 2026 (after A101 / 0.53.0)

Written 2026-09-15 from the tree, not from docs. Question asked: what needs work next, is the
animation package done, what can run unattended, what is the next step. Supersedes the August
gap audit's *facts* (§2 lists the ones that rotted); its build-queue ordering still holds.

## 0. Facts verified this pass

| Measure | Value |
|---|---|
| HEAD | `b680a97c` (0.53.1 tab strip), main |
| Package version | `0.53.0` in `package.json`; roadmap A82–A101 all ticked, Phase 2 complete |
| Package `.cs` files (no `Samples~`) | 532 (390 on 2026-09-09) |
| `TODO` / `FIXME` / `HACK` | 0 |
| Largest files | `CutsceneEditorPanel.cs` 5,510 · `ClipEditorWindow.cs` 4,580 · `TimelinePane.cs` 2,381 |
| Last full gate | EditMode 866 (865 passed, standing `Conformance_A`), PlayMode 285 |
| Game files referencing the toolkit | 60 (August audit said zero) |
| Legacy `AnimationSystemGroup/` | 2 systems left (`UnitFacingSystem`, `UnitAnimationAssignmentSystem`), both seam code |
| `Core/Unused/` | 10 files (factory, minion orders, outline, reset events) |
| Open `verify-*.md` checklists | 19 |
| Open in-game owner checkpoints | G5-P10, G6-P7, RG T4–T10, `verify-cutscene.md` (editor-side ones closed under the 2026-09-14 rule) |
| Unity Editor | open on this project during the pass; compile gate clean after the asmdef fix |

## 1. Fixed in this pass

- **Player builds could not compile** — `Assets/_Scripts/Editor/StitchPunk.Editor.asmdef` had
  `"includePlatforms": []`, so the whole game Editor assembly (PropertyDrawers, IMGUI,
  `DotsAnimationToolkit.Editor`) was compiled into the player. This is the failure that blocked the
  A91 build check on 2026-09-13. Now `["Editor"]`. Nothing outside that folder referenced the
  assembly. The A91 check (`_Vault/Spencer/verify-a91-player-build.md`) is unblocked.
- `Assets/Prefabs/EntityLibraries/AnimationLibrary.prefab` — an empty GameObject with no
  references (its script was deleted in the migration; the spec asked a human to remove it). Deleted.
- `Assets/CLAUDE.md` pointed at `Assets/AnimationToolkitMigration/`, which never existed. Re-pointed.
- Doc truth: HANDOFF §7's `CountTracksForTarget` undercount was fixed by A84 (it now calls
  `AssetReferenceIndex.CountTracksBoundToTarget`); `verify-animationeventtiming.md` said no clip
  carried an `Attack` event — `MeleeContinuous` and its east-facing twin do (key 18);
  `Plans/README.md` rows for the cutscene roadmap, cutscene integration, actor editor roadmap and
  movement extraction still said "nothing built" for work that shipped weeks ago.

## 2. August gap-audit claims that are now false

Do not act on these lines of `Systems_Gap_Audit_2026-08.md`:

- "Zero references to `DotsAnimationToolkit` in `Assets/_Scripts/`" — 60 files, six asmdefs.
- "The legacy animation stack is what the game runs" — migration phases 3–6 landed 2026-08-29; the
  legacy systems, SOs, assets and the old Animation Editor are deleted. Phase 2 (owner clip
  authoring) landed as G0/G5 content (`MaleCitizen` rig, clip set, profile).
- Area 2 (event timing) is built (`AnimationEventTiming_System.md`, in Verification); area 3
  (direction) is built through phase 4 and reworked into the toolkit's direction sets.
- Cutscenes: G1 (integration), G2 (interactions), G3 (acceptance), G6 (profile cutover) are all
  code-complete; only the owner's eyes are missing.

What still holds: the §6 build-queue order (Despawn → Minion Orders → Zombie Conversion → Ranged →
Player Resource → Factory → Schedules → Crowd), the §4 "extract nothing else yet" rule, and §5's
hygiene rows.

## 3. The animation package — is more needed?

**Feature work: no.** Fourteen tabs, one chrome, events/health/stats, capture, ragdoll, retarget,
materials, flipbooks, texture packer, cutscenes, actor profiles. Every roadmap box is ticked and the
owner's product calls are recorded in the roadmap §2. Adding tabs now is scope creep.

**What is genuinely left, in order of value:**

1. **Release readiness — the package's own "remaining before 1.0" list, unchanged since 0.9.0.**
   `package.json` names three: a clean-project import check, a player build, a compile pass over
   `Samples~` (Unity never compiles it; the vault already records that it rots). Plus the standing
   red `Conformance_A`: the Editor asmdef references `Unity.RenderPipelines.Universal.Runtime`
   (needed by the VAT preview material and the cutscene viewport) and the test's §1.3 expectation
   does not. A gate that is always red is a gate nobody reads. **Decision (architecture call, mine
   to make per the standing delegation): the dependency is legitimate; add it to the expectation
   and to the architecture doc §1.3.** No sample compile test exists today — the only `Samples~`
   mention in `PackagingConformanceTests.cs` is the `UnityEditor` scan's Editor-folder exemption.
2. **Unified Clip Authoring (UA) P1–P5** — the owner's own directive ("I should be animating
   everything in the same window, vat bones, object transforms, flipbooks"), P0 built 2026-09-08,
   the rest specced with three ← DECISION markers. **A79** (VAT Bake preview shows the other parts)
   is the same gap seen from the other tab: both want per-kind posers that do not depend on the
   baked registry. Build them as **one** spec: UA P2's poser split first, then P3 (VAT in the Clip
   Editor) and A79 (cutouts in the VAT Bake preview) share it. Decisions to record rather than
   re-ask: P1 (b) — a targetless rig is fine when the clip set has bone or VAT tracks; P2 — build an
   empty-target blob so one code path serves every kind; P4 — read-only lanes only, no import.
3. **HANDOFF hygiene.** `HANDOFF.md` is 788 lines. §1 still says Phase F "has not compiled or run
   yet" and "current state (2026-09-08)"; §4 is a 580-line newest-first log from A55 to A100. A fresh
   Sonnet session pastes the whole file. Move §4's closed paragraphs (everything with its checkpoint
   answered or closed under the standing rule) to `HANDOFF_History.md`, rewrite §1 as three
   paragraphs, keep §5–§9. Same for `Documentation~/index.md` if it lists tabs by hand (the vault
   rule: write the command that lists, not the list).
4. **Owner-only calls that never got made** (HANDOFF §6): the bone-reparent guard and the
   Spatial3D twist axis. Both wait on him; neither blocks a sale.

## 4. The game — where it stands

The game runs on the toolkit with **one** actor (`MaleCitizen`). `PlayerUnit`/`BaseUnit` still use a
separate body-part tree with no rig, clip set or profile. Everything action-shaped now goes through
`PlaybackApi.PlayAnimation` by name; combat timing reads the `Attack` event with `hitTime` as the
fallback. Cutscenes, dialogue, sound, save (phases 1–3), AI decision/execution split, self-defence,
flee, talk, sit, pickup and minion move orders are built. The factory loop is parked.

The spec-ready, zero-code queue (all in `Tasks/Plans/`, decisions locked, testable in PlayMode
fixtures without a scene):

| Spec | Size | Unattended? | Why it is next |
|---|---|---|---|
| Zombie Conversion | S | yes | both composed requests exist; only `ZombifyRequest`'s composer is missing; the demo-defining beat |
| Despawn System | M | yes | prerequisite for projectile pooling |
| Minion Order Robustness | M | yes | hard-coded `ActionType.MeleeSingle` breaks on the first ranged minion |
| Ranged / Projectile Combat | L | yes, after the two above | last unbuilt behaviour phase |
| Player Resource + HUD | M | code yes, HUD no | the owner wants his say on UI |
| Factory Minimal Loop | M | yes | un-parks `ProductionSystem` |
| Schedules + Waypoints, Crowd-Scale Awareness | M / M | yes | after the above |
| Feature Isolation, Cleanup Batch rows 1/2/5/6, CharacterRig hardening remainder | S each | yes | fill-in |
| Second toolkit actor (`PlayerUnit`) | L | scripted half yes, the look no | proves the profile design generalises; the recipe scripts under `Editor/ContentAuthoring/` are the template |

## 5. What can run unattended (ranked)

1. **Release-readiness pass on the package** (one session, orchestrator-heavy): Conformance_A
   green by recording URP in §1.3; a temporary asmdef that compiles `Samples~` through the normal
   gate, then removed; a Windows player build through `manage_build` now that the asmdef fix is in
   (expect further game-side player errors — fix what is mechanical, report the rest); a
   clean-project import via `Unity.exe -batchmode -createProject` with the package as a `file:`
   dependency and `-quit`, grepping its log for `error CS`. Closes the A91 check as a side effect.
2. **Zombie Conversion → Despawn + Minion Order Robustness (parallel worktrees) → Ranged.** Pure
   DOTS systems with approved specs; `/worktree-run` is built and drive-proven for exactly this.
3. **UA + A79 as one spec** (§3 item 2), with the three decisions recorded above.
4. **HANDOFF and index truth pass** (§3 item 3).
5. **`PlayerUnit` as a toolkit actor — the scripted half only:** a recipe script authoring rig
   targets, a clip set and a profile from the existing body-part tree, gated by the same
   name-binding fixtures G5 used. Stop before anything that needs eyes.
6. Cleanup Batch rows 1/2/5/6, Feature Isolation, the `Core/Unused/` decision (delete or un-park).
7. Worktree Toolkit broker: `GateRequestStore.WriteJsonAtomically` (`Packages/com.worktreetoolkit/Editor/Broker/`, line 108)
   throws `IOException: Unable to remove the file to be replaced` from the heartbeat tick when the destination is
   momentarily locked (seen once during this pass with nothing else running). `File.Replace` needs a retry or
   `File.Move(overwrite: true)`; one small fixture.

**Not unattended:** every `verify-*.md`, the four in-game checkpoints, ragdoll limits and launch
feel, any HUD or UI, the Stats tab's "which numbers matter" question.

## 6. The owner's one play session (when back, ~45 minutes, in this order)

1. `TestArea.unity`, Play, F9 — **G6-P7**: two minions walk to the cart marks (Walk cycling,
   facing, six-direction turns), idle facing the cart, board, dialogue holds, cart leaves, everyone
   detaches and AI resumes. Judge the turn-while-walking pop and the arrival facing.
2. Same scene — **G5-P10**: idle↔walk on a citizen, a swing on the Action layer handing back to
   Base, death face on the Eyes layer, revive restarting the blink.
3. Same scene — **RG-T5/T6**: kill one citizen, watch drop/settle/sleep; revive it; then say
   whether the ±45° default hinge limits look wrong (they were invented by an agent).
4. `DOTSTestScene` F9 — G2's beat (`verify-cutscene.md`).
5. Clip Editor, **Stats** tab in Play — which numbers do you want, which are noise, should
   Snapshot also write a file.
6. Ten minutes on the remaining `verify-*.md` that cover systems still live (billboarding, camera
   visibility, sound, unit design, zombie conversion once built). Close the rest as "superseded"
   where the system they describe was deleted in the migration (ragdoll2d, characterrig,
   colorpalette's ragdoll half).

## 7. Next step

Today, unattended: §5 item 1 (release readiness) — it is the only thing standing between "roadmap
complete" and "sellable", it is mostly Editor driving, and it turns the always-red gate green.
Then §5 item 2 through `/worktree-run`. UA+A79 after that. The owner's §6 session is the single
biggest unblocker on the game side and needs no preparation from anyone.
