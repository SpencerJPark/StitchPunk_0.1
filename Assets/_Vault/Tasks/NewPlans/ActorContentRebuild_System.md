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

- [x] **T1 — Map the hierarchy and decide the target set.** [parallel-safe]
  Open `MaleCitizen.prefab` and record which prefab actually owns the 31 parts (asset vs. nested
  `MaleUnitVisual` vs. the model prefab) — this decides where `RigTargetAuthoring` goes, and editing
  the wrong layer silently does nothing. Then decide **which parts are animated targets**. Not all 31
  need to be: face details (`Eyes Facedetails Faceware Mouth Mustache Nose LeftEyeBrow RightEyeBrow
  Ear Hair`) ride the head and need no tracks of their own. Recommended target set is the ~14 that
  move: both legs (upper/lower/foot), both arms (upper/lower/hand), `Pelvis`, `Torso`, `Neck`,
  `BaseHead`. Record the chosen list and the tag name per target in §7.
  *Gate: no code change; the recorded list is the deliverable.*

- [x] **T2 — Strip the seven dead script GUIDs.** [parallel-safe]
  Remove the missing-script components from `MaleCitizen.prefab` and `BaseUnit.prefab`. The GUIDs:
  `14d360c5a6f8d4d4cbd374a60bdfa72a`, `2034f872939f04e44b67cda7f1a00afa`,
  `40345665c1986bb47860365174cf5dd9` (×31), `703e04cde0134e57aa50cefdd628be22`,
  `c16549610bfe4458aa9389201d072bb6`, `da03443cf962d5341bcf2132bae8432d`,
  `dfa4a7c782bd420c820466dd6ace6f18`. Their meta files are gone from history — these are dead, not
  re-linkable. Prefer `GameObjectUtility.RemoveMonoBehavioursWithMissingScript` over YAML surgery.
  *Gate: enter Play on `CutsceneG1Checkpoint.unity`; `read_console` shows **zero** "The referenced
  script is missing" warnings. That count is the test — do not add a fixture.*

- [x] **T3 — Build the rig.** Populate `NewRig.asset` in place (decision D1) from T1's list: one
  `RigTargetDefinition` per animated part with `displayName`, `sourceNodePath`, a `tagId`, and
  sensible `boundsExtents`; one `Base` layer with `defaultActive = true`; `mirrorPairs` for the
  left/right limbs. Use the Clip Editor's **New Rig panel** against the unit prefab if it can carry
  the whole job; otherwise `RigAssetUtility.CreateRig` via `execute_code`.
  **Trap (`RigAssetUtility`'s own remarks):** call `EnsureStableIds()` *after* `targets` is
  populated, never before — a rig saved with every target id still `0` fails validation rules V02
  and V05, and both shipped samples hit exactly that.
  *Gate: reload the asset from disk and assert every target has a non-zero id and a non-zero tagId;
  the rig passes the toolkit's own validation with no V02/V05/V13 error.*

- [x] **T4 — Make the unit an actor.** On the prefab layer T1 identified: `ActorAuthoring` on the
  unit root (`rig = NewRig`, `clipSets = [ NewClipSet ]`), and `RigTargetAuthoring` on each animated
  part with its `targetStableId`. Leave each part's `rig` field **null** so it inherits the actor's
  rig — setting a different rig is an authoring error the baker reports.
  *Gate: enter Play; assert in the world that the `MaleCitizen` entity carries the actor archetype
  (a `PlaybackLayer` buffer and the command/event buffers) and that no `ActorBakeFailed` entity
  exists. Assert by querying the world, not by reading the inspector.*

- [x] **T5 — Author the walk clip.** Create `Assets/ScriptableObjects/Animations/Walk.asset`:
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

- **(T1, answered 2026-09-05) `MaleCitizen.prefab` owns all 31 parts itself.** It is a *Regular*,
  fully flattened prefab: every one of its 39 transforms returns `null` from
  `PrefabUtility.GetCorrespondingObjectFromSource`, so nothing under it is a nested prefab instance.
  `ActorAuthoring` and all 16 `RigTargetAuthoring` components go **on `MaleCitizen.prefab` directly**,
  under `Visual/MaleUnitVisual/…`.

  **Drift from §2 — the four unit prefabs do NOT share one visual.** §2 says `MaleCitizen` shares its
  visual "through `TestRotter` and `PlayerUnit`". Verified false:

  | Prefab | Where its 31 parts come from |
  |---|---|
  | `Units/MaleCitizen.prefab` | **owns them** (flattened, no source) — the two checkpoint minions |
  | `Units/BaseUnit.prefab` | instance of `Units/**Visuals**/MaleUnitVisual.prefab` |
  | `Units/PlayerUnit.prefab` | variant of `BaseUnit` (so, also `Visuals/MaleUnitVisual.prefab`) |
  | `Units/MaleUnitVisual.prefab` | instance of `Assets/Models/MaleNPC_StitchPunk.fbx`; **referenced by nothing** |

  So there are *two* copies of the same body-part tree in the project, and the one §2 names
  (`Units/MaleUnitVisual.prefab`, guid `57a3dea4…`) is the orphan — `BaseUnit` uses the
  similarly-named file in the `Visuals/` subfolder. Editing either would leave `MaleCitizen`
  untouched. **This spec animates `MaleCitizen` only**, which is what the G1 checkpoint's two minions
  (both guid `c7df5a12f0afc5a4186c4dc99eba6f7f`) instantiate. `PlayerUnit` gaining a walk is a
  follow-up: the same rig applies, but the components have to be added a second time on
  `Visuals/MaleUnitVisual.prefab`.

- **(T2, done 2026-09-05) One of the "seven dead GUIDs" is alive, and there were four more dead
  ones the spec never listed.** `c16549610bfe4458aa9389201d072bb6` resolves to
  `Packages/com.unity.entities/Unity.Entities.Hybrid/Baking/LinkedEntityGroupAuthoring.cs` — a live
  Unity Entities script that both `BaseUnit` and `MaleCitizen` legitimately carry. It only *looked*
  dead because a `grep` for its guid over `Assets/**.meta` + `Packages/**.meta` misses every script
  that lives in a resolved package; `AssetDatabase.GUIDToAssetPath` is the check that does not lie.
  `GameObjectUtility.RemoveMonoBehavioursWithMissingScript` never touched it, which is the second
  reason the spec's "prefer the API over YAML surgery" line was right — hand YAML surgery against
  the spec's list would have deleted a working baker component from every unit in the game.

  Six of the seven were dead as described. Four more, on prefabs the spec did not name, were found
  by scanning **all 29 prefabs** under `Assets/`: `91f157b4…` (`EntityLibraries/AnimationLibrary`),
  `eb82636b…` (`EntityLibraries/ScoringLibrary`), `42b941a8…` (`Units/Brains/CitizenBrain`),
  `925b5fd8…` (`Units/Brains/ZombieBrain`). All four bake into the checkpoint subscene, so the
  task's own gate ("zero missing-script warnings") could not have been met without them.

  **145 missing-script components removed across 9 prefabs**, project-wide count now 0:
  `Units/Visuals/MaleUnitVisual` 32, `Units/MaleCitizen` 36, `Units/BaseUnit` 2,
  `Units/Visuals/MaleHead` 2, `MaleHead Variant` 1, the two `EntityLibraries` 1 each, the two
  `Brains` 1 each — plus 34 on `Units/PlayerUnit` that were all inherited and cleared themselves
  once `BaseUnit` and `Visuals/MaleUnitVisual` were fixed (strip base prefabs before variants, or
  every removal lands as a variant override). The checkpoint's own scene files carry no dead
  scripts; the ten unresolved guids a raw grep finds in `CutsceneG1Checkpoint.unity` are all uGUI,
  Cinemachine, Input System and `SubScene`.

  *Gate result:* Play entered on `CutsceneG1Checkpoint.unity`, both `CutsceneStage` entities baked,
  console carries **zero** "The referenced script is missing" entries (previously ~100).

- **(T3, done 2026-09-05) `NewRig.asset` populated in place; 13 target tags minted.** The registry
  held 3 rows (`UpperRightArm`, `UpperLeftArm`, `LowerRightArm`) and now holds 16 — the 13 new ones
  follow that same `{Upper|Lower}{Side}{Limb}` convention rather than the parts' own
  `{Side}{Upper|Lower}{Limb}` order, because the vocabulary is the shared thing and three rows of it
  already existed. `Assets/Generated/DotsAnimationToolkit/TargetTags.cs` regenerated through
  `ConstantsGenerator.BuildVocabularyConstantsSource` + `WriteGeneratedFile` (the same path
  `VocabularyConstantsSection.RegenerateIfConfigured` uses), 16 constants, zero sanitisation reports.

  The rig carries `sourcePrefab = MaleCitizen.prefab`, 16 `Quad` targets with full
  `Visual/MaleUnitVisual/…` node paths, one `Base` layer with `defaultActive = true`, and 6 mirror
  pairs (the arm/leg/hand/foot left-right pairs; `Pelvis Torso Neck BaseHead` are midline and pair
  with nothing).

  *Gate result:* asset re-imported `ForceUpdate` and reloaded from disk — 16 targets, **0** zero ids,
  **0** zero tag ids, no duplicate id and no duplicate tag; `ClipValidation.ValidateRig` returns an
  **empty** message list, so no V02/V05/V13. Compile clean.

  **Note for anyone building a rig from `execute_code`:** `TargetKind` lives in the *runtime*
  namespace `DotsAnimationToolkit`, not `DotsAnimationToolkit.Authoring` where every other type this
  task touches lives. The MCP `execute_code` backend is also CodeDom/C# 6 here — no local functions,
  no `using` directives (the snippet is a method body), so everything is fully qualified.

- **(T4, done 2026-09-05) `MaleCitizen.prefab` is a toolkit actor.** `ActorAuthoring` on the root
  (`rig = NewRig`, `clipSets = [ NewClipSet ]`), `RigTargetAuthoring` on all 16 parts with their
  `targetStableId` and `rig` left null so each inherits the actor's rig.

  *Gate result:* Play on the checkpoint scene — **2** actor roots in the world (`TestRotter` and
  `MaleCitizen`, both `MaleCitizen.prefab` instances), each with 1 `PlaybackLayer`, a **16**-entry
  `RigPartRef` buffer, live `AnimationCommand`/`AnimEventOutput` buffers, and a created
  `ClipRegistry` blob. No toolkit error logged.

  **Spec drift — T4's "assert no `ActorBakeFailed` entity exists" is not assertable.**
  `ActorBakeFailed` is `internal` *and* `[BakingType]`, so it never reaches the built entity scene
  and is invisible to a Play-mode query. What actually proves the bake succeeded is the pair above:
  a created `ClipRegistry` on the root plus a populated `RigPartRef` buffer, which only
  `RigBindingBakingSystem` writes and only for an actor that did not bail. Use that assertion in any
  later spec that copies this gate.

  **Known bake noise, left alone deliberately:** `NewClip 1`'s three transform tracks quote target
  ids from the `HumanoidRig` deleted in the 2026-08-29 cleanup, so each bake logs three rule-T6
  "track is skipped" warnings per actor bind. They are Phase-F debt in a stub clip with no content,
  not a fault in this chain; deleting the stubs is a separate cleanup and would change what the Clip
  Editor and `NewCutscene.asset` currently point at.

- **(T5, done 2026-09-05) `Walk.asset` authored; 16 tag-bound tracks, 5 keys each.** Created through
  `ClipAssetUtility.CreateClipInSet` + `RenameClip` (so the set append and the id mint go the same
  way the Clip Editor's own New Clip button goes), then filled: `duration` 1.0s,
  `defaultLoop = Loop`, `frameRate` 30, keys at 0 / .25 / .5 / .75 / 1 with the first repeated last
  so the loop seam has no jump, `EaseInOut` throughout. Legs swing ±22°, arms counter-swing ±14°
  opposite their own side's leg, knees and elbows only ever bend one way, and the pelvis carries a
  3 cm dip twice per stride with the torso, neck and head counter-rotating a few degrees to keep the
  head near level. `ActorAuthoring.startingLayers` seeds layer 0 with it.

  **Three facts about this rig that decide how any future clip must be authored:**
  1. **Keys are OFFSETS from the part's rest pose, not absolute local transforms.** `ClipSampler`
     anchors an `Override` track on `TargetRestPose` (`restPose.localPosition.xy`,
     `restPose.rotation`), so a key of all zeros means "unchanged", and a channel left out of the
     `channels` mask stays at rest entirely.
  2. **Rotation is `float3 rotation` in degrees; `rotationZ` is legacy.** `ClipAsset.OnAfterDeserialize`
     migrates `rotationZ` into `rotation` **only when `rotation` is all-zero**, so authoring both is
     safe but authoring `rotationZ` alone is the old path. `QuickStartActorBuilder`'s `MakeSwingTrack`
     still writes `rotationZ` — that sample is stale on this point, as `Gotchas.md` warns about
     `Samples~` generally.
  3. **Z is the only axis that can swing these parts.** Every one of the 16 meshes is a flat quad —
     measured mesh bounds are `size.z == 0.000` — pivoted at its joint with the art hanging below
     (`center.y ≈ −0.19` on a limb). Rotating about X or Y foreshortens a cutout to a line. That is
     also why `boundsExtents` had to be re-derived from `|mesh.center| + mesh.extents` about the
     **pivot** rather than guessed: the first pass measured about the mesh centre and under-covered
     every limb by roughly half its length.

  *Gate result:* Play — both actors' layer 0 holds clip id `0xF8D1558C18E8F741` (`Walk`) at
  `clipIndex 2`, layer time advancing. Two samples ~9.7 s apart: `LeftUpperLeg` 0.07° → 342.06°,
  `LeftLowerLeg` 346.02° → 349.57°, `LeftFoot` 357.99° → 4.74°, and the pelvis `posY` 0.981 → 1.006
  (rest is 1.011, so the dip channel is live too). `ValidateClip(Walk)` and the rig half of
  `ValidateBind` are both clean; the only bind warnings are `NewClip 1`'s three pre-existing V38s.

- **(T1) The 16 animated targets and their tags.** Face details, jacket flaps, belt and bulge ride
  their parents and get no tracks (decision D3). Tag names follow the vocabulary the registry already
  uses — `{Upper|Lower}{Side}{Limb}`, from its three existing rows.

  | Part (path under `Visual/MaleUnitVisual/`) | Tag |
  |---|---|
  | `LeftUpperLeg` | `UpperLeftLeg` |
  | `LeftUpperLeg/LeftLowerLeg` | `LowerLeftLeg` |
  | `LeftUpperLeg/LeftLowerLeg/LeftFoot` | `LeftFoot` |
  | `RightUpperLeg` | `UpperRightLeg` |
  | `RightUpperLeg/RightLowerLeg` | `LowerRightLeg` |
  | `RightUpperLeg/RightLowerLeg/RightFoot` | `RightFoot` |
  | `Pelvis` | `Pelvis` |
  | `Pelvis/Torso` | `Torso` |
  | `Pelvis/Torso/LeftUpperArm` | `UpperLeftArm` *(existing row)* |
  | `Pelvis/Torso/LeftUpperArm/LeftLowerArm` | `LowerLeftArm` |
  | `Pelvis/Torso/LeftUpperArm/LeftLowerArm/LeftHand` | `LeftHand` |
  | `Pelvis/Torso/RightUpperArm` | `UpperRightArm` *(existing row)* |
  | `Pelvis/Torso/RightUpperArm/RightLowerArm` | `LowerRightArm` *(existing row)* |
  | `Pelvis/Torso/RightUpperArm/RightLowerArm/RightHand` | `RightHand` |
  | `Pelvis/Torso/Neck` | `Neck` |
  | `Pelvis/Torso/Neck/BaseHead` | `Head` |

  Mirror pairs: the six left/right limb pairs plus the two feet and two hands — eight pairs.
- **Standing context from the G1 checkpoint session (2026-09-05):** the checkpoint scene
  (`CutsceneG1Checkpoint.unity` + `SubScenes/CutsceneG1Checkpoint_Sub.unity`) is built, wired and
  machine-verified end to end; root motion, camera tracking, the hard cut, AI gating and release all
  work. **The only thing missing from it is the animation this spec adds.** Detail in
  `CutsceneIntegration_System.md` §7.
- **Do not "fix" the minions teleporting to their lane at t=0.** Cutscene root keys are absolute
  world positions and a wandering unit snaps to the first key when the cutscene starts. That is the
  toolkit staging its cast, and it is in `Gotchas.md`.
