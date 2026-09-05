# Actor Content Rebuild — Design Spec (G0)

> **Status:** specced 2026-09-05, nothing built. Inserted **before A63** on the cutscene critical
> path by owner decision (2026-09-05) — see `Cutscene_Roadmap.md` §3.
> **Executor:** one fresh Claude Sonnet session with no prior context. `Cutscene_Roadmap.md` §4 is
> the protocol; read it before opening this spec's tasks.
> **Why it exists:** every cutscene spec after G1 builds features nobody can see, because no unit in
> this game can play an animation clip. This spec makes one unit a real toolkit actor with a real
> walk cycle, so the rest of the roadmap has something to look at.

---

## 1. Purpose & scope

Give `MaleCitizen` (and through it `TestRotter` and `PlayerUnit`, which share the visual) a working
`RigAsset` → `ClipSetAsset` → `ActorAuthoring` chain and one authored **walk cycle**, then put that
clip into the G1 checkpoint cutscene so the two minions *walk* their root-motion lanes instead of
sliding along them.

**In scope:** the rig, the per-part authoring, one walk clip, the clip-set wiring, the dead-script
cleanup that shares those same prefabs, and the checkpoint cutscene's clip blocks.

**Out of scope:** idle/attack/death clips, direction sets and facing variants, VAT, ragdoll bodies,
sockets. One clip that loops and reads as walking is the whole deliverable — a second clip is
cheaper once the first proves the chain.

---

## 2. What exists today (verified 2026-09-05 — re-verify before trusting)

This inventory is why the spec exists. Every line was checked, not assumed.

| Thing | State |
|---|---|
| `ActorAuthoring` | **Referenced by zero prefabs and zero scenes.** No unit in the game is a toolkit actor. |
| `Assets/ScriptableObjects/Animations/NewRig.asset` | The project's **only** `RigAsset`. `targets: []`, `layers: []` — completely empty. Referenced by nothing. |
| `NewClip.asset` / `NewClip 1.asset` | The project's only `ClipAsset`s: 1 and 3 `transformTracks`. Stubs, not animation. |
| `NewClipSet.asset` | Lists both stub clips. Its serialized `rig:` and `eventKeys:` lines are **stale YAML** — `ClipSetAsset` no longer declares either field (Phase F), so Unity drops them on the next save. A clip set does **not** name a rig; the *actor* pairs rig + sets. |
| Unit prefabs | `Assets/Prefabs/Units/{BaseUnit, MaleCitizen, MaleUnitVisual, PlayerUnit}.prefab`. `MaleCitizen.prefab` is the big one (151 objects) and owns the body-part tree; `MaleUnitVisual.prefab` is a `PrefabInstance` of a model prefab (guid `57a3dea4b08c71c448f81b8aa7fe69d5`). **Confirm which prefab actually owns the 31 parts before editing** — T1. |
| Body parts | 31, under `<unit>/Visual/MaleUnitVisual/`: `LeftUpperLeg LeftLowerLeg LeftFoot Pelvis Belt Buldge Torso LeftJacket LeftUpperArm LeftLowerArm LeftHand Neck BaseHead Ear Eyes Facedetails Faceware Hair LeftEyeBrow Mouth Mustache Nose RightEyeBrow RightJacket RightJacketInside RightUpperArm RightLowerArm RightHand RightUpperLeg RightLowerLeg RightFoot`. (Spelling `Buldge` is the asset's, not a typo here.) |
| Dead scripts | **7 unresolved script GUIDs** on `MaleCitizen.prefab`, 3 of them also on `BaseUnit.prefab`. One (`40345665c1986bb47860365174cf5dd9`) sits on all 31 parts. Left by the legacy-stack deletion (`43530db7`) and the CharacterRig commit (`1e3bb164`); the meta files are long gone, so these are unrecoverable, not re-linkable. ~100 warnings per bake. |

**The one piece of good news:** the toolkit already has the tooling to do all of this without
hand-writing YAML — see §4.

---

## 3. The target

```
NewRig.asset            targets: one RigTargetDefinition per animated part, each with a tagId
                        layers:  [ "Base", defaultActive = true ]
                        mirrorPairs: left/right limb pairs
                                  |
MaleCitizen.prefab      ActorAuthoring { rig = NewRig, clipSets = [ NewClipSet ],
                                         startingLayers = [ layer 0 -> Walk ] }
                        RigTargetAuthoring on each animated part { targetStableId = <its target> }
                                  |
Walk.asset (ClipAsset)  duration ~1.0s, defaultLoop = true, frameRate 30,
                        transformTracks keyed by tagId: leg swing, arm counter-swing, torso bob
                                  |
NewClipSet.asset        clips: [ ..., Walk ]
                                  |
G1CheckpointCutscene    both slots: rig = NewRig, clipSets = [ NewClipSet ],
                        one clipBlock 0..4s, loop = true
```

**Acceptance:** open `Assets/Scenes/CutsceneG1Checkpoint.unity`, press Play, press **F9** — the two
minions walk their lanes with legs and arms cycling, and keep cycling for the whole 4 seconds.

---

## 4. Read first (in this order, and only these)

1. Repo root `CLAUDE.md`; `Assets/_Vault/Memories/Code/RULES.md`; `Gotchas.md` (its last four entries
   are from the G1 checkpoint session and three of them are cutscene traps).
2. `Docs/AnimationToolkit/HANDOFF.md` §2, §3, §5, §6. **Skip its §4 history** except the G1 entry.
3. `Packages/com.dotsanimationtoolkit/Samples~/QuickStartActor/Editor/QuickStartActorBuilder.cs` —
   **the end-to-end recipe already written down**: `CreateRig` → clip with `transformTracks` →
   `CreateClipSet` → an actor object with `ActorAuthoring` + per-part `RigTargetAuthoring`. Read it
   before writing anything. Note `Samples~` is excluded from Unity compilation and rots silently
   (`Gotchas.md`) — treat it as a reference, not as callable code.
4. `Packages/com.dotsanimationtoolkit/Authoring/Assets/RigAsset.cs` — `RigTargetDefinition`
   (`displayName`, `sourceNodePath`, `kind`, `boundsExtents`, `facesDirection`, `tagId`),
   `LayerDefinition`, and `EnsureStableIds`.
5. `Packages/com.dotsanimationtoolkit/Authoring/Baking/ActorAuthoring.cs` and
   `RigTargetAuthoring.cs` — the two components' real field names.
6. `Packages/com.dotsanimationtoolkit/Editor/ClipUtilities/RigAssetUtility.cs` and
   `Editor/ClipEditor/Authoring/NewRigPanel.cs` — the **New Rig panel scans a source prefab's
   hierarchy** and mints targets from it. Prefer it over building the rig by hand.

---

## 5. Tasks

Work in order. After each: save → compile gate → run only the fixtures the task names → tick the
box → commit that task alone (`G0-Tn: <what>`, stage paths explicitly, never `git add -A`).

- [ ] **T1 — Map the hierarchy and decide the target set.** [parallel-safe]
  Open `MaleCitizen.prefab` and record which prefab actually owns the 31 parts (asset vs. nested
  `MaleUnitVisual` vs. the model prefab) — this decides where `RigTargetAuthoring` goes, and editing
  the wrong layer silently does nothing. Then decide **which parts are animated targets**. Not all 31
  need to be: face details (`Eyes Facedetails Faceware Mouth Mustache Nose LeftEyeBrow RightEyeBrow
  Ear Hair`) ride the head and need no tracks of their own. Recommended target set is the ~14 that
  move: both legs (upper/lower/foot), both arms (upper/lower/hand), `Pelvis`, `Torso`, `Neck`,
  `BaseHead`. Record the chosen list and the tag name per target in §7.
  *Gate: no code change; the recorded list is the deliverable.*

- [ ] **T2 — Strip the seven dead script GUIDs.** [parallel-safe]
  Remove the missing-script components from `MaleCitizen.prefab` and `BaseUnit.prefab`. The GUIDs:
  `14d360c5a6f8d4d4cbd374a60bdfa72a`, `2034f872939f04e44b67cda7f1a00afa`,
  `40345665c1986bb47860365174cf5dd9` (×31), `703e04cde0134e57aa50cefdd628be22`,
  `c16549610bfe4458aa9389201d072bb6`, `da03443cf962d5341bcf2132bae8432d`,
  `dfa4a7c782bd420c820466dd6ace6f18`. Their meta files are gone from history — these are dead, not
  re-linkable. Prefer `GameObjectUtility.RemoveMonoBehavioursWithMissingScript` over YAML surgery.
  *Gate: enter Play on `CutsceneG1Checkpoint.unity`; `read_console` shows **zero** "The referenced
  script is missing" warnings. That count is the test — do not add a fixture.*

- [ ] **T3 — Build the rig.** Populate `NewRig.asset` in place (decision D1) from T1's list: one
  `RigTargetDefinition` per animated part with `displayName`, `sourceNodePath`, a `tagId`, and
  sensible `boundsExtents`; one `Base` layer with `defaultActive = true`; `mirrorPairs` for the
  left/right limbs. Use the Clip Editor's **New Rig panel** against the unit prefab if it can carry
  the whole job; otherwise `RigAssetUtility.CreateRig` via `execute_code`.
  **Trap (`RigAssetUtility`'s own remarks):** call `EnsureStableIds()` *after* `targets` is
  populated, never before — a rig saved with every target id still `0` fails validation rules V02
  and V05, and both shipped samples hit exactly that.
  *Gate: reload the asset from disk and assert every target has a non-zero id and a non-zero tagId;
  the rig passes the toolkit's own validation with no V02/V05/V13 error.*

- [ ] **T4 — Make the unit an actor.** On the prefab layer T1 identified: `ActorAuthoring` on the
  unit root (`rig = NewRig`, `clipSets = [ NewClipSet ]`), and `RigTargetAuthoring` on each animated
  part with its `targetStableId`. Leave each part's `rig` field **null** so it inherits the actor's
  rig — setting a different rig is an authoring error the baker reports.
  *Gate: enter Play; assert in the world that the `MaleCitizen` entity carries the actor archetype
  (a `PlaybackLayer` buffer and the command/event buffers) and that no `ActorBakeFailed` entity
  exists. Assert by querying the world, not by reading the inspector.*

- [ ] **T5 — Author the walk clip.** Create `Assets/ScriptableObjects/Animations/Walk.asset`:
  `duration` ~1.0s, `defaultLoop = true`, `frameRate` 30, and `transformTracks` addressed **by
  `tagId`** (T3's tags), not by target id. A readable 2.5D cutout walk is roughly: upper legs
  counter-swinging ±25°, lower legs trailing, upper arms counter-swinging ±15° opposite the legs,
  a small torso bob on Y at twice leg frequency. `MakeSwingTrack` in `QuickStartActorBuilder.cs` is
  the shape to copy. Add it to `NewClipSet.asset`'s `clips`, and set `ActorAuthoring.startingLayers`
  so layer 0 seeds with it.
  *Gate: enter Play; assert the actor's layer 0 is playing the Walk clip id and that a sampled part's
  `LocalTransform.Rotation` **changes between two frames**. A clip that loads but never moves a part
  is the failure this gate exists to catch.*

- [ ] **T6 — Put the walk into the checkpoint cutscene.** On both slots of
  `Assets/ScriptableObjects/Animations/G1CheckpointCutscene.asset`, set `rig = NewRig`,
  `clipSets = [ NewClipSet ]`, and add one `CutsceneClipBlock` for the walk spanning 0→4s with
  `loop = true`. Re-bake (reopen the subscene or re-enter Play).
  *Gate: fire the cutscene from `execute_code` at `speed = 0.1` (10× slow, so a mid-playback sample
  is reachable across an MCP round trip — this is how the G1 session verified the scene) and assert
  a bound minion's part rotation changes between two samples **while** its root position advances
  along the lane.*

- [ ] **⏸ T7 — Owner checkpoint.** Stop here and hand over. The owner opens
  `Assets/Scenes/CutsceneG1Checkpoint.unity`, presses Play, presses **F9**, and watches. Expected:
  both minions **walk** their lanes with limbs cycling, the camera tracks then hard-cuts at 3.05s, a
  sound fires at 1.5s, `UtilityActions` is empty on both while it runs, WASD does nothing, and at the
  end they resume wandering while the camera blends back to gameplay.
  Then run the full suites once (`DotsAnimationToolkit.Tests.EditMode`/`.PlayMode`,
  `StitchPunk.Tests`/`.PlayMode`) and check the discovered totals did not drop.

---

## 6. Decisions

**Delegated, already made — do not re-litigate:**

- **D1. Populate `NewRig.asset` in place** rather than minting a new rig asset. It is empty and
  referenced by nothing, and `Cutscene_Roadmap.md` §6 already names it as the acceptance cutscene's
  rig — keeping the name keeps the roadmap's vocabulary intact.
- **D2. One clip, not a set.** Walk only. Idle/attack/death are cheap once the chain is proved and
  expensive to author against a rig that may still change.
- **D3. Not every part is a target.** Face details ride the head. A rig with 14 moving targets is
  easier to key and cheaper to sample than one with 31.
- **D4. T2 (dead-script cleanup) comes before the rig work**, not after, because both touch the same
  prefabs and doing it second means re-diffing a prefab that just gained 14 components.

**Owner calls — ask, do not assume:**

- Nothing outstanding. If the walk cycle reads wrong on screen that is T7 feedback, not a decision.

---

## 7. Open questions / build log

- *(T1)* Which prefab layer owns the 31 parts — record the answer here before T4 edits anything.
- **Standing context from the G1 checkpoint session (2026-09-05):** the checkpoint scene
  (`CutsceneG1Checkpoint.unity` + `SubScenes/CutsceneG1Checkpoint_Sub.unity`) is built, wired and
  machine-verified end to end; root motion, camera tracking, the hard cut, AI gating and release all
  work. **The only thing missing from it is the animation this spec adds.** Detail in
  `CutsceneIntegration_System.md` §7.
- **Do not "fix" the minions teleporting to their lane at t=0.** Cutscene root keys are absolute
  world positions and a wandering unit snaps to the first key when the cutscene starts. That is the
  toolkit staging its cast, and it is in `Gotchas.md`.
