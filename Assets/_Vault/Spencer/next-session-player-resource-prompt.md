You are the **stage orchestrator** for one game spec run through the Worktree Toolkit (`/worktree-run`,
`Packages/com.worktreetoolkit`): **Player Resource System + Summon Cost**, one `spec-lead` in its own git worktree.
You alone touch `mcp__UnityMCP__*`, merge, integrate and close. The owner is away: every checkpoint closes under the
standing rule (accepted unless game breaking) and the merge below is pre-authorized.

| Spec id | Path | What it is | Suites |
|---|---|---|---|
| `player-resource` | `Assets/_Vault/Tasks/Plans/PlayerResource_System.md` (§12 is the build plan) | `ResourceStack` ledger + `ResourceChangeRequest` delta buffer on `Player`, `PlayerResourceSystemGroup`, `IPersist` snapshot with two save mirrors, `UnitSO.summonCost` → `UnitDataBlob.summonCost`, `PlayerReviverSystem` charges scrap + electricity before enabling `ReviveRequest` and stamps `ResourceShortfall` when it cannot, Rive `ResourceHudManager`, `DebugResourceMenu`, three PlayMode fixtures | game; `StitchPunk.Tests`, `StitchPunk.Tests.PlayMode` |

Why this one: the 2026-09-15 review after `Code_Audit_2026-09.md` found the game has no loop. Revival is free, corpses
are not a resource, and the owner's own design rule (one body + electricity + scrap per unit, bodies are the only cap)
has no code behind it. This spec is the declared foundation for summoning, building, trade, caravan and camp mode, and
it lands the first real spender. It is not animation work; do not touch `Packages/com.dotsanimationtoolkit`.

Read, in order:
1. `CLAUDE.md` (root), `.claude/skills/worktree-run/SKILL.md`, `.claude/agents/spec-lead.md`,
   `Assets/_Vault/Memories/Code/WorktreeToolkit.md` (all traps).
2. The spec **whole**. §11 holds the twelve settled decisions PR-D1–PR-D12; §12 the waves; §6 the three owner
   ← DECISION markers, which are HUD presentation only and never block code.
3. `Assets/_Vault/Memories/Code/RULES.md`, `Systems.md`, `Components.md`, `Contracts.md`, `Gotchas.md`.
4. `Assets/_Vault/Spencer/next-session-parallel-a102-despawn-minionorders-prompt.md`: the lead contract, the
   game-side substitutions, the gate syntax and the hand-gated PlayMode mechanics carry over verbatim unless this
   file says otherwise. Copy the lead contract into the lead's prompt with those game-side substitutions applied.

**Owner pre-answers (do not ask):**
- Models: lead `opus`, workers `sonnet`. Skip the `AskUserQuestion` step of the skill.
- Merge: `worktree.py merge player-resource` is authorized once the lead reports ready with both wave gates green
  and the revert-to-fail logged. Push after it.
- The three HUD ← DECISION markers in §6: take the recommendation written beside each, build to it, and keep the
  three as ⚠ rows in `verify-player-resource.md` for the owner.
- Play mode: **not authorized.** No scene edits, no `SaveAssets`, no Play, no Rive artboard. PlayMode *fixtures*
  through the Test Runner are fine. The Rive artboard and the HUD placement are the owner's; the TMP fallback labels
  field in `ResourceHudManager` is what the verify pass uses until then.

## Phase 0 — stage prep (all yours, before the lead)

1. **Preflight:** `ListAgents` (trap: a clean `git status` is not a free trunk; another session ran A103 on
   2026-09-15 and may still be merging); `python Packages/com.worktreetoolkit/Tools~/worktree.py doctor --json`
   (retry once on a broker heartbeat blip before declaring it dead); `git status` clean apart from what you create;
   delete any empty `.claude/worktrees/*` folder no longer locked.
2. **Baseline:** compile gate, then `StitchPunk.Tests` (floor 68) and `StitchPunk.Tests.PlayMode` (floor 19) once.
   Anything red now is real; record it and stop if it touches `SystemPlacementConformanceTests` or
   `SystemGroupOrderTests`.
3. **Declare the group (PR-D2), stage-owned, before the lead spawns:** in `Assets/_Scripts/Systems/SystemGroups.cs`
   add `PlayerResourceSystemGroup : GameSceneSystemGroup` with `[UpdateInGroup(typeof(SimulationSystemGroup))]`,
   `[UpdateAfter(typeof(BuildingsSystemGroup))]`, `[UpdateBefore(typeof(CombatSystemGroup))]` and a two-line
   header comment in the file's house style; create the empty folder
   `Assets/_Scripts/Systems/PlayerResourceSystemGroup/` (Unity writes its meta on refresh); insert
   `typeof(PlayerResourceSystemGroup)` between `BuildingsSystemGroup` and `CombatSystemGroup` in
   `Assets/_Scripts/Tests/SystemGroupOrderTests.cs`; update the sim-order line in the root `CLAUDE.md`
   (`… → Movement → Buildings → PlayerResource → Combat → …`). Compile gate, then run exactly
   `StitchPunk.Tests.SystemPlacementConformanceTests` and `StitchPunk.Tests.SystemGroupOrderTests`. Commit
   `PlayerResource-S0: declare PlayerResourceSystemGroup`; push, so the worktree branches from it.
4. Record Phase 0 in the spec's §13. Commit only that file; push.

## Phase 1 — spawn

One `spec-lead`, `model: opus`, background. Its prompt holds: spec id `player-resource`, the spec path, worker model
`sonnet`, the Phase 0 results (group declared, floors), the gate syntax, the lead contract with the game-side
substitutions, and these **additions**:

- The waves are §12's: **T1** is the lead's shared-types commit (`ResourceType.cs`, `ResourceComponents.cs`,
  `SummonCostBlob` + the `UnitDataBlob.summonCost` field, a stub `ResourceLedger.cs`); **W1** five workers (T2–T6);
  **W2** three workers (T7–T9); T10 revert-to-fail; T11 close. W2 briefs paste the real `ResourceLedger` signatures.
- Every worker brief: at most two named files, reading limited to named line ranges, the snippets from spec §4–§5
  pasted in, "at turn 30 stop editing and write your report", report of 30 lines or fewer.
- **Gate syntax** for EditMode through the broker, the conformance pair on every gate:
  `python Packages/com.worktreetoolkit/Tools~/worktree.py gate --edit-mode StitchPunk.Tests.SystemPlacementConformanceTests --edit-mode StitchPunk.Tests.SystemGroupOrderTests`.
  A gate is real only when its passed count equals the tests named.
- **PlayMode fixtures are hand-gated by you** (the broker refuses them): the lead sends
  "gate needed: player-resource <sha> StitchPunk.Tests.PlayMode.<Fixture>" and waits. F1 after W1; F2 and F3 after
  W2; T10's mutation commit must fail exactly F1, F2 and F3 and nothing else.
- `UnitSO.cs` lines 57–60 (the commented `Spawn Cost` block) are **replaced**, not appended to. The struct default
  `summonCost = 1/1/1` means no `.asset` edit is needed; the lead must not touch `Assets/ScriptableObjects/`.
- `PlayerReviverSystem` stays a main-thread `SystemAPI.Query` (it is today); the cost gate goes between the
  "target has `ReviveRequest`" check and the enable, exactly as §5 shows. `ReviveRequestSystem` and
  `SpawnStateInitSystem` are not edited (PR-D7).
- `ResourceHudManager` mirrors `Assets/_Scripts/UI/MinionGroupHUD.cs` (plain MonoBehaviour, `RiveUtils.OnLoaded`,
  `RivePropertyCache`, push on change) with the seven property names from §6 and the optional `TMP_Text[]`
  fallback. No Rive asset is created.
- **Stage-owned, never edited by the lead:** `SystemGroups.cs`, `SystemGroupOrderTests.cs`, every `CLAUDE.md`,
  `Tasks/Plans/README.md`, `Tasks/Verification/*`, `Tasks/Claude/*`, `ProjectSettings/`, `Assets/Scenes/`,
  `Assets/ScriptableObjects/`. The README row text and the `verify-player-resource.md` body go in the lead's
  For-integration block.

## Phase 2 — while running

Stay quiet while the broker resolves EditMode gates. Hand-gate the PlayMode requests only while `list --json` says
`stage.busyWith` is null (`stage-commit`, compile, the named fixture, `restore-trunk`, then `git status` for stray
folder metas). A capped lead is never resumed: read its diff, then spawn a fresh `worker` with the worktree's absolute
path for what is left, never a second `spec-lead`.

## Phase 3 — merge, integrate, close

- `worktree.py merge player-resource`; push; `worktree.py remove player-resource`.
- **Integration on the stage:** compile gate; `StitchPunk.Tests` (must not drop below 68) and
  `StitchPunk.Tests.PlayMode` (19 + `ResourceChangeSystemTests` + `ReviveCostTests` + `ResourceSnapshotSyncTests`);
  move the spec to `Assets/_Vault/Tasks/Verification/PlayerResource_System.md` and write
  `verify-player-resource.md` from its §10 with the rebake as the first line (player prefab archetype and
  `UnitDataBlob` layout both changed) and the three HUD ⚠ rows; `Tasks/Plans/README.md` row → 🔨 built, verify
  pending; `Assets/CLAUDE.md` "Built" line gains "player resources + revive cost"; `Contracts.md`, `Components.md`,
  `Systems.md`, `Data.md`, `Gotchas.md` truth (the lead drafted them; you check them against the merged code);
  `Code_Audit_2026-09.md` §4 table row "Player Resource + HUD" → built, HUD artboard owner-side.
- Commit `PlayerResource: integration`, push. `git status` clean.
- **End** with one message for the owner: the ready report condensed, the test totals, the rebake-first play checks in
  `verify-player-resource.md`, the three HUD decisions with what was built to, the Rive artboard property names he
  needs to author, and the next prompt: Scene 03 "Reanimation class" as the first playable vertical slice in
  `TestArea.unity` (dialogue → raise from the ledger → command → fight → save), which needs his eyes and Play mode,
  so it is a spec-writing session, not an unattended build.

## Settled owner calls (do not re-ask)

Names never numbers; no manual asset wiring; unseen checkpoints close as accepted unless game breaking; PR-D1–PR-D12
as written in the spec's §11; the HUD is Rive, the artboard is the owner's; wood → electricity burning and corpse
collection are the next specs, not this one.
