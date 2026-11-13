# Next session: readability pass, part 4 (awareness lookup bundle)

Paste everything below the line into a fresh session.

---

Finish the last item of the Stitch Punk readability refactor: a shared lookup bundle for the awareness systems in `Assets/_Scripts/Systems/UtilityAISystemGroup/UtilityAwarenessSystemGroup/`. Read `Assets/_Vault/Memories/Code/Systems_AI.md` first.

**Already done, don't redo:** part 3 shipped `BehaviorExecutionSystem`'s shared command context (merge `d2ec84de`). That pass proved in the Editor that `[ReadOnly]` and `[NativeDisableParallelForRestriction]` on fields of a struct that is itself a job field are honoured by the job safety system: a CampLoop Play run showed no safety exceptions. Don't re-verify that in PackageCache.

**Facts from the part 3 inventory (re-check the counts with one Haiku grep; the code may have moved):**
- 16 files in the folder. `ClearOptionsSystem` has no lookups. 49 `.Update(ref state)` lines across 14 files.
- The only lookup set shared by 4 or more systems is `{LocalTransform, Dead}`, all read-only, in 9 systems: AllyAlert, Retreat, Social, RangedAttack, Flee, Enemy, SelfDefence, Drunk and ItemAwareness. Also: LocalTransform alone is in Interaction, Navigation and GuardPost; Dead alone is in ThreatDecay.
- Everything else is in 3 systems or fewer: Item 3; Interaction, GuardPost, StateMachine and CutsceneActor 2 each. Faction is RangedAttack only.
- Naming is mixed. AllyAlert, Flee, SelfDefence, Social, Drunk and ItemAwareness use `_deadLookup` / `_transformLookup` system fields. The others use bare `deadLookup` / `transformLookup`. Job fields are always bare names with `[ReadOnly]`.

**The design question to settle first.** A two-lookup bundle only saves about 5 boilerplate lines per system, and it makes call sites longer (`unitState.dead.HasComponent(x)`). Make it earn its place with helpers:
1. A Haiku `verifier` greps the 9 systems for how `deadLookup` and `transformLookup` are used together. Examples: `!deadLookup.HasComponent(target)` then `transformLookup[target].Position`; `transformLookup.HasComponent(x) && !deadLookup...`; a distance-to-live-target check. It returns a table of each recurring pattern, the systems using it, and the line numbers, 20 lines max.
2. Design `UnitStateLookups` in `Assets/_Scripts/Utils/`:
   - Fields: `[ReadOnly] ComponentLookup<LocalTransform> transform` and `[ReadOnly] ComponentLookup<Dead> dead`.
   - `static UnitStateLookups Create(ref SystemState state)` and `void Update(ref SystemState state)`.
   - Helper methods for every pattern that recurs in 3 or more systems, named so they read as documentation. For example `bool IsAlive(Entity unit)` and `bool TryGetLivePosition(Entity unit, out float3 position)`.
   - Helpers must keep the exact semantics of the code they replace: the same `HasComponent` checks, in the same order, with the same enabled-bit handling (check whether `Dead` is an enableable component and whether the old code used `HasComponent` or `IsComponentEnabled`), and the same strictness.
   - Check `EntityUtils.IsDead/IsAlive` (added in part 2) first. If they already cover a pattern, call them rather than duplicate them.
   - If no pattern recurs in 3 or more systems, stop. Report that the bundle is boilerplate only, and ask the owner whether to build it anyway.
3. Convert ONE system first; pick the one with the most matches. Then gate, compile in the Editor, run its fixture and get a verifier pass. Only then do the rest.
4. Waves: 2–3 systems per Sonnet `worker`, in parallel. A Sonnet `verifier` checks each wave for equivalence. Systems with `LocalTransform` only or `Dead` only (Interaction, Navigation, GuardPost, ThreatDecay) take the bundle only if a helper simplifies them; partial adoption is fine. Normalise the touched system fields to the bare naming style.

**Leave alone:** `MinionGatherSystem`, the World Features session owns it. Also any `Terrain*` file. Any write lookup stays where it is.

**How to work (owner's standing instructions):**
- You are the observer: never edit code yourself. Use the lowest tier that can do each job:
  - `model: haiku` (`verifier` type) for greps and inventories.
  - `model: sonnet` `worker` for every edit.
  - `model: sonnet` `verifier` for equivalence and Burst-hazard reviews.
  - Never `general-purpose`.
- Each brief names at most two files, pastes the snippets and line ranges, says "stop at turn 30 and report", and caps the report at 15–25 lines. Each worker runs `python Tools/devloop/devloop.py gate game` before reporting (exit 2 is not a pass) and `devloop.py lint <files>`.
- Run `ListAgents` first. If a peer is busy on main, work in a worktree: `git worktree add .claude/worktrees/readability-4 -b refactor/readability-4`. Never `cd` the session; use `git -C` or `(cd <dir> && cmd)`.
- To gate the worktree:
  - Junction `<worktree>\Library` to main's `Library` (`cmd //c mklink //J`).
  - Backdate the asmdefs: `find <wt>/Assets/_Scripts <wt>/Packages -name '*.asmdef' -exec touch -d "2026-10-30" {} +`.
  - Run `gate game`.
  - Before `git worktree remove`, remove the junction with `cmd //c rmdir`.
- **Unity MCP is shared.** Message the peers before using the Editor, wait for their "free", merge to main only after that (the merge's recompile would hit their work), and tell them when you're done. Their tips:
  - The first Play after a recompile can pause on a Rive NullReferenceException; unpause it.
  - A stale Burst copy can keep old behaviour after a revert; check with `BurstCompiler.Options`.
- If a 429 kills a wave, check `git status` / `git diff --stat` before respawning. Never resume a capped agent.
- Burst hazards for the verifier to check:
  - A lookup bundle passed by value into a Burst static helper.
  - Helpers on an `in` bundle (they must not mutate it; mark the struct's helper methods `readonly`).
  - Missing `unmanaged` constraints.

**Verify:**
1. `gate game`.
2. A verifier pass per wave.
3. Editor: `refresh_unity`, then poll until `isCompiling` is false, then `read_console` for CS and Burst `BC####` errors.
4. Confirm the new code actually loaded: use `execute_code` reflection to check that the new type exists.
5. Run the AI PlayMode tests in PlayMode with `group_names` `BehaviorExecutionSystemTests`, `SmartHumanAi`, `BanditCampSystemTests`, `GuardPostLeashTests`, `BrawlNonLethalTests`, `CutsceneSystemTests` and `EmotePresentationTests` (36 tests at part 3). Use `init_timeout` 240000; the first run timed out at 120000.
6. One Play run in `Scenes/CampLoop.unity`: clear the console first, then check for `InvalidOperationException` and other safety errors.

**Finish:**
- Merge to main with `--no-ff`, then run `gate game` on main.
- Commit with explicit paths, never `git add -A`. Don't push.
- Update `Systems_AI.md` with one line about the bundle.
- Add "awareness: bandits and zombies still notice, flee, retreat and fight as before" to `Tasks/Verification/verify-smart-ai.md`.
- Move this prompt to `Spencer/Archive/`.
- Report:
  - lines before and after
  - which systems took the bundle and which helpers they use
  - which systems didn't, and why
  - the play check the owner still owes.
