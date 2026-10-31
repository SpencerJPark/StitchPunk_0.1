# Next session: Smart Human AI H5 (guns, wind-up telegraph, line of fire), in a worktree

You are the **orchestrator** for SmartHumanAI **H5**. The spec is finished and every decision is settled; **don't re-ask them**. Build it in **your own git worktree**, because another Claude session (the terrain session) works on `main` and shares the one Unity Editor. Edits go to `worker` subagents. Set `model` explicitly on every spawn: `sonnet` for logic, `haiku` for mechanical work.

## Read, in order
1. Root `CLAUDE.md`, then `Assets/_Vault/Memories/Code/Systems_AI.md` whole. Its "Tactics (H4)" section is current as of 2026-10-31.
2. `Assets/_Vault/Tasks/Plans/SmartHumanAI_H5_System.md`: **the build spec** (H5-D1–D12, phases 0–6, tests, verification).
3. `Assets/_Vault/Tasks/Plans/RangedCombat_System.md` §0 (RC-D1–D10, the owner's product calls).
4. `Assets/_Vault/Tasks/Plans/DamageReaction_System.md` §1 and §9, for context only. It is **not** part of H5; it is the next build. Until it lands, friendly hits deal damage but make no ThreatEntry (RC-D8 for H5).
5. `Assets/_Vault/Memories/Code/WorktreeToolkit.md` "Traps" 1, 3, 4, 20.

## State on 2026-10-31 (main)
- H4 (Keep Range, Heal When Hurt, Retreat) is merged: dd205902 and 02d1bf76. PlayMode was 133/133 and EditMode 204/204 then. It isn't play-tested yet (`verify-smart-ai.md` § H4).
- The terrain session has since landed AI-adjacent edits. `FactionRegistrySystem.cs` and `BehaviorExecutionSystem.cs` filter `WithNone<AbstractSimulation>`, and `UnitBakingUtil.cs` bakes the abstract components. PlayMode was 139/139 after that. **Re-read those files before editing them.**
- `ThrowAwarenessSystem` already owns Keep Range (`TacticsCooldown.keepRangeReadyAtSeconds`, fan, leash clamp). H5 renames it `RangedAttackAwarenessSystem` and keeps the `.meta` GUID (`git mv` both files).

## Worktree workflow (the owner asked for this so sessions don't run over each other)
1. **Check peers first:** `ListAgents`. Message the terrain session (`stitch-punk-9b`, or whichever peer is busy on main) to say you're starting H5 in a worktree.
2. **Create:** `python Packages/com.worktreetoolkit/Tools~/worktree.py create smart-ai-h5 --mode source-only`. That gives branch `spec/smart-ai-h5` at `.claude/worktrees/smart-ai-h5`.
3. **Work in it** with absolute paths. **Never `cd` the session** into it or anywhere else: use `(cd path && cmd)` or `git -C path`. A session whose cwd is inside the worktree locks the folder, so `worktree remove` can't delete it, and a bare `cd` breaks the relative `.claude/hooks` paths.
4. **Commit in the worktree** after each phase, staging explicit paths (never `git add -A`).
5. **The offline gate does not run inside a source-only worktree.** `devloop gate` reads Unity's compile setup from `Library/Bee` under the worktree root, and there is none. Run it once to confirm (expect exit 2; never read 2 as a pass). From then on, compile and test by **merging the phase to main**:
   - Ask the peer: "OK to merge `spec/smart-ai-h5` (phase N) into main and take the Editor for ~15 min?" Wait for an explicit go.
   - `git -C <main> merge --no-ff spec/smart-ai-h5`, then `refresh_unity` → poll `editor_state` → `read_console`, then run the touched PlayMode fixtures.
   - Fix forward. Small fixes can go straight on main with explicit paths; anything bigger goes back in the worktree and merges again.
   - Message the peer when you're done with the Editor.
6. **Before deleting anything** on main, confirm with `cmp` that it matches your commit. Use literal paths with `rm`; the safety check blocks `rm "$VAR/..."`.
7. **Finish:** `worktree.py remove smart-ai-h5` (with your cwd outside it), then `git branch -d spec/smart-ai-h5`. Don't push commits that aren't yours; tell the peer main is ahead and leave the push to whoever owns those commits.

## Shared-Editor rules
- **Never save `Assets/Scenes/CampLoop.unity`** (camera drift lives there). Scene edits go into `Assets/Scenes/SubScenes/CampLoop_Sub.unity` only. Open it additively in `execute_code`, add root objects, save that scene, and close it if it wasn't open before.
- **Never remove the terrain session's objects or files** (e.g. the `TerrainStreaming` object in CampLoop_Sub, its tests). New props and items get **no `NavFootprintAuthoring`**.
- Run only your own fixtures mid-pass: `group_names: ["StitchPunk.Tests.PlayMode.SmartHumanAi"]` and your new ones. Run the full `StitchPunk.Tests.PlayMode` + `StitchPunk.Tests` EditMode once before the final commit.
- `execute_code` is CodeDom C# 6: no local functions. Gameplay types are in the global namespace.

## Traps that cost time on H4
- **Stale DLL across assemblies:** a new member in Data/Components/Utils shows CS0117/CS0246/CS1061 in Systems and Tests until the Editor recompiles. Brief workers that those specific errors are expected.
- **Revert-to-fail with Burst off:** `Unity.Burst.BurstCompiler.Options.EnableBurstCompilation = false` via execute_code. Break the feature (python string edits), force-refresh, run the fixture, and see red. Then `git checkout --` the files, turn Burst back on, force-refresh, and re-run green. One test may need two mutation rounds; for example, a cooldown test fails on "never offered" before it can fail on "re-offered".
- **Assets by `execute_code`**, not hand-written YAML (Unity writes the GUIDs). Copy the H4 script `Assets/_Vault/Spencer/h4_assets_execute_code.txt` as the template for behaviours, actions, modules, library and brain wiring. Copy `h4_bandages_execute_code.txt` for prefab-from-Rock + subscene placement.
- **`.md`/`.txt` files you add under Assets need a `.meta`**, or the Editor creates one later as an untracked file. Copy an existing TextScriptImporter meta with a fresh `uuid4().hex` GUID.
- **No unit has toolkit animation data:** anim waits complete at once. Don't time anything on clip events; `WindUp` is a plain timed blocking command.
- **The body tint can't whiten:** `BodyPartTint` is a multiply. The flash needs the additive `_FlashAmount` per-instance property in the body-part 2D shader graphs (`shader-edit` skill). That is phase 0's probe; do it before designing the presentation code.
- **Phase 2 ends at an owner checkpoint:** capture the wind-up on a rock throw (swell + white flash) and stop for the owner to judge colour, strength and scale (H5-D3). The values are tunables on `WindUpPresentationConfig`. Under the standing rule, close it as accepted if the owner hasn't looked by the end of the session, and note that in the build log.

## Deliverable
- Phases 0–6 of `SmartHumanAI_H5_System.md`, with tests proven red with Burst off.
- A CampLoop live check via `execute_code`: the baked BanditBrain has Shoot/Throw/KeepRange; the guns and starting gunman exist.
- Docs:
  - update `Systems_AI.md`;
  - add a `SmartHumanAI_System.md` §11 H5 build-log entry;
  - flip the H5 status line;
  - add the `verify-smart-ai.md` H5 section from spec §7;
  - fix the H3 checklist line "a rock that hits a fellow bandit does nothing", which is now false (RC-D1).
- Memory update, then the worktree removed. Move this prompt to `Assets/_Vault/Spencer/Archive/` when done.
