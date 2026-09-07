# Actor Editor Roadmap — layers, named animations, per-animation direction, ragdoll mix

> **Status:** ✅ specs written 2026-09-07, nothing built. Owner product calls recorded in §2.
> **Executor:** each spec is sized for one fresh Claude Sonnet session. `Cutscene_Roadmap.md` §4 is
> the protocol every session follows (commit prefixes below). Read it before opening any spec.
> **Supersedes:** [`AnimationLayersContent_System.md`](AnimationLayersContent_System.md) (AL) — its
> clip-authoring tasks move into G5 §5; its unit-SO wiring is replaced by the profile.
> [`RagdollTuning_System.md`](RagdollTuning_System.md) (RG) is unaffected and may run first.

---

## 1. The owner's vision (2026-09-07, verbatim where it matters)

> "On the left side I can add animation layers, under those I can set what animation an actor can
> do … on those animations I could add the option for there to be a direction dimension … it also
> allows me to set multiple clips for one animation command on a layer. This is supposed to be the
> testing area for actor animation and how they will all mix … there will be a default override
> layer and a starter layer, the others can build … direction will be a per action thing for
> characters … I should also be able to test ragdoll in this area too … a character dying which
> would trigger the death face and a ragdoll and then having the character reanimate."

Today the "which clip for which action" table is the **game's** (`UnitSO.idleAnimation`,
`actionAnimations`, `DirectionSetBlob`), the rig owns the layer list, direction is folded by game
code, and the editor preview composites one clip. The roadmap moves all four into the package.

## 2. Owner product calls (2026-09-07 — do not re-ask)

1. **Layers live on the actor profile asset**, not the rig. The rig keeps targets, sockets,
   billboards and ragdoll bodies. Breaking; no migration (standing directive: new rigs are fresh).
2. **The game asks for an animation by name.** Names are a third vocabulary registry with generated
   constants (`AnimNames.Attack`). The toolkit resolves layer, facing slot and clip from the actor's
   baked profile. Direction resolution moves into the package.
3. **Base and Override are fixed bookends** of every profile: layer 0 seeds the starter animation,
   the top layer is what cutscenes and narrative play on. Authors add and reorder only the layers
   between.
4. **Ragdoll is a flag on an animation entry** (`Start` / `Stop`, at play or at an event marker).
   Playing *Death* drops the body and plays the death face; playing *Revive* restores and resumes.
   The same asset drives the editor test and the game.

## 3. The specs, in order

| # | Spec | Delivers | Depends on | Prefix |
|---|---|---|---|---|
| A70 | [`Amendment_A70_ActorProfile_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A70_ActorProfile_Spec.md) | `ActorProfileAsset` (layers → animations → optional direction slots), `AnimationNameRegistry` vocabulary, `ActorProfileBlob`, `PlayAnimation`/`StopAnimation` commands resolved by the package, `ActorFacing`, facing re-pick, ragdoll trigger, cutscenes on the top layer, `RigAsset.layers` removed | RG optional | `A70-Tn` |
| A71 | [`Amendment_A71_ActorEditor_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A71_ActorEditor_Spec.md) | the **Actor Editor** tab: layer/animation tree, per-entry inspector with the direction queue, a composited multi-layer preview with transport and direction slider, trigger buttons, ragdoll mix and reanimate in the preview | A70 | `A71-Tn` |
| G5 | [`ActorProfileCutover_System.md`](ActorProfileCutover_System.md) | game cutover: `UnitSO` → profile, name-convention binding of `ActionType`/stances, assignment issues named commands, `UnitFacingSystem` writes `ActorFacing`, `DirectionSetBlob`/`AnimationToolkitLayer` deleted, `MaleCitizen` profile authored with the AL clips (Idle, Attack, Blink, DeathFace) | A70, A71 | `G5-Pn` |

**Critical path:** A70 → A71 → G5. RG can run before A70 or after G5 (it only touches
`ragdollBodies` and `HealthSystemGroup`).

## 4. Execution protocol

`Cutscene_Roadmap.md` §4, unchanged, with the commit prefixes above. Toolkit amendments obey
`Docs/AnimationToolkit/HANDOFF.md` §2 (A69 comment rule, static-class suffixes, UI Toolkit only) and
§3 (gate + discovered counts).

## 5. Shared vocabulary (every spec uses these names; do not invent synonyms)

Toolkit, namespace `DotsAnimationToolkit` / `.Authoring`:

- `ActorProfileAsset { rig, clipSets, turnDirections, layers }` · `ActorLayerDefinition
  { displayName, defaultActive, startingAnimationKey, animations }` · `ActorAnimationDefinition
  { animationKey, hasDirections, clip, directionSlots, loop, speed, blendIn, ragdollTrigger,
  ragdollAtEventKey }` · `DirectionSlots` (the five east-side slots, shared with `DirectionSetAsset`).
- `AnimationNameRegistry` (third vocabulary; `VocabularyRegistryProvider.AnimationNames`;
  generated class `AnimNames`).
- `ActorProfile : IComponentData { BlobAssetReference<ActorProfileBlob> Value }` ·
  `ActorFacing : IComponentData { Direction facing }` · `RagdollTrigger { None, Start, Stop }`.
- `CommandKind.PlayAnimation` / `.StopAnimation` · `AnimationCommand.animationKey` ·
  `PlaybackLayer.animationKey` · `AnimEventOutput.animationKey`.
- `PlaybackApi.PlayAnimation` / `.StopAnimation` / `.IsAnimationPlaying` · `ActorProfileApi.TryResolve`.
- `CutsceneApi.TopLayer` (= `byte.MaxValue`, resolved per actor).
- `ClipEditorTab.ActorEditor` (renamed from `DirectionSets`) · `ActorEditorPanel` ·
  `ActorPreviewComposer`.

Game, global namespace: `UnitSO.actorProfile` (validate-only against the prefab), name-convention
binding (`ActionType`/`StanceType` names → `AnimNames`), `UnitFacingSystem` → `ActorFacing`.
