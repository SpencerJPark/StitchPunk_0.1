# Amendment A61 — Cutscene Stage Baking

> **Status:** ✅ spec, not built. Written 2026-09-04.
> **Roadmap:** `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` — read its §4 protocol first.
> **Depends on:** nothing. **Parallel-safe with:** A62 (disjoint files; both touch `CHANGELOG.md` — merge by hand).
> **Session budget:** one Sonnet session. Four tasks, one owner checkpoint at the end.

## 1. The gap

The Cutscene Editor binds slots to real scene GameObjects (`CutsceneSceneBindingUtility`, per-scene `GlobalObjectId` strings inside `CutsceneAsset.sceneBindings`), and the runtime player binds slots to entities (`CutsceneActorBinding`, host-filled). **Nothing connects the two.** A host must rebuild every binding by hand in code, and there is no baked entity that carries a `CutsceneBlob` into a subscene at all — `CutsceneBlobBuilder.Build` has no production caller outside the editor.

This amendment adds the bake step that turns "author in the editor" into "plays in a scene": a `CutsceneStageAuthoring` component whose baker emits one `CutsceneStage` entity per cutscene, blob and bindings included, and a cast-panel action that keeps that component in sync with the editor's bindings.

## 2. Read first

- `Authoring/Baking/SocketAttachmentAuthoring.cs` — the shape of a small authoring + baker pair in this package (`GetEntity(other, TransformUsageFlags.Dynamic)`, "an unconfigured component bakes to nothing").
- `Authoring/Baking/ActorBaker.cs` lines 600–680 — `AddBlobAssetWithCustomHash` / `DependsOn` usage.
- `Authoring/Build/CutsceneBlobBuilder.cs` — `Build(CutsceneAsset, out BlobAssetReference<CutsceneBlob>, List<string>)`.
- `Runtime/Api/CutscenePlaybackApi.cs`, `Runtime/Components/CutsceneComponents.cs`.
- `Editor/ClipEditor/Cutscene/CutsceneCastPanel.cs`, `CutsceneSceneBindingUtility.cs`, and `CutsceneEditorPanel.cs` methods `BindSlotToObject`, `PlaceSlotFromPrefab`, `RefreshCastPanel`, `RefreshSceneStatus`.
- `Tests/PlayMode/CutsceneTimelineSystemTests.cs` — the manual-`World` fixture pattern and the hand-built blob helpers.

## 3. Design

### 3.1 Authoring

`Authoring/Baking/CutsceneStageAuthoring.cs`:

```csharp
[AddComponentMenu("DOTS Animation Toolkit/Cutscene Stage")]
[DisallowMultipleComponent]
public sealed class CutsceneStageAuthoring : MonoBehaviour
{
    public CutsceneAsset cutscene;
    public List<CutsceneStageSlotBinding> bindings = new List<CutsceneStageSlotBinding>();
}

[Serializable]
public sealed class CutsceneStageSlotBinding
{
    public uint slotId;          // CutsceneSlot.SlotId — never the slot's name or list index
    public GameObject target;    // the actor root (Actor slot) or transform-only object (Prop slot)
}
```

`CutsceneStageBaker : Baker<CutsceneStageAuthoring>`:

- `cutscene == null` → bake nothing (the `SocketAttachmentBaker` rule and its reason).
- `DependsOn(authoring.cutscene)`; for every Actor slot `DependsOn(slot.rig)` and each `DependsOn(clipSet)` so a clip-set edit re-bakes the stage.
- `CutsceneBlobBuilder.Build(authoring.cutscene, out blob, warnings)` then `AddBlobAsset(ref blob, out Hash128 _)` — the `BlobAssetStore` owns it from here; **do not dispose**. Warnings are already logged by the builder.
- `Entity stageEntity = GetEntity(TransformUsageFlags.None)`; `AddComponent(stageEntity, new CutsceneStage { blob, cutsceneKey = cutscene.StableId })`; `AddBuffer<CutsceneStageBinding>` with one element per binding whose `target != null`: `GetEntity(binding.target, TransformUsageFlags.Dynamic)`. A binding whose `slotId` no slot in the asset carries bakes to nothing and logs one warning naming the stage object and the id.
- **Trap to write into the class remark:** `GetEntity` only resolves GameObjects baked in the same subscene. A target in another scene bakes to `Entity.Null`; the host must supply that binding at play time (G1's override buffer does this for runtime-spawned units).

### 3.2 Runtime

`Runtime/Components/CutsceneComponents.cs` gains:

```csharp
public struct CutsceneStage : IComponentData
{
    public BlobAssetReference<CutsceneBlob> blob;   // owned by the bake-time BlobAssetStore
    public ulong cutsceneKey;                         // CutsceneAsset.StableId — how a host finds this stage
}

[InternalBufferCapacity(4)]
public struct CutsceneStageBinding : IBufferElementData
{
    public uint slotId;
    public Entity target;
}
```

`Runtime/Api/CutscenePlaybackApi.cs` gains:

```csharp
public static Entity CreatePlayRequestFromStage(
    EntityManager entityManager, Entity stageEntity, byte layerIndex = 0, float speed = 1f)
// = CreatePlayRequest(stage.blob, ...) then copies every CutsceneStageBinding into CutsceneActorBinding.
// The host may still add or overwrite CutsceneActorBinding entries afterwards (spawned actors).

public static bool TryFindStage(EntityManager entityManager, ulong cutsceneKey, out Entity stageEntity)
// Linear scan over a temporary EntityQuery<CutsceneStage>. Main-thread convenience for hosts;
// a host with many stages caches the result.
```

### 3.3 Editor — "Sync to Stage"

`CutsceneCastPanel` toolbar gains a **Stage** status label and a **Sync to Stage** button:

- Resolve every bound slot through `CutsceneSceneBindingUtility` to its live GameObject.
- Find a `CutsceneStageAuthoring` in the open scene(s) whose `cutscene` is this asset. None → create `GameObject("Cutscene Stage — <asset name>")` with the component, **in the scene of the first bound object** (`boundObject.scene`), so a subscene-hosted cast gets a subscene-hosted stage that actually bakes. Bindings spanning more than one scene → warn in the status label and still write.
- Write `cutscene` and `bindings` through a `SerializedObject` (one Undo step, `EditorSceneManager.MarkSceneDirty`).
- Status label states: `Stage: none` / `Stage: synced` / `Stage: out of date` (a bound slot differs from the component's list, or the component names an object the panel does not). Recomputed in `RefreshCastPanel`.
- **A61-D2:** sync is explicit, never automatic. Auto-writing a scene component from every Bind/Place click would dirty the scene on preview scrubs and surprise a user who was only rehearsing.

Placement: the button and label live in the cast panel header row beside "+ Actor Slot". Match the existing chrome (`ToolbarButton`, `.cutscene-editor__*` USS classes) — no new visual language.

## 4. Decisions

- **A61-D1** The stage is a separate authoring component, not a field on `CutsceneAsset`. An asset can be staged in several scenes (its `sceneBindings` already say so); the entity that bakes must live in the scene whose objects it binds.
- **A61-D2** Explicit sync (above).
- **A61-D3** `TryFindStage` matches on `cutsceneKey`, never on asset path or name — the same identity rule every other stable-id asset uses.
- **A61-D4** No migration of existing `sceneBindings`; the first Sync writes them. Nothing shipped depends on a stage existing.

## 5. Tasks

- [ ] **T1 — Runtime components + API.** `CutsceneStage`, `CutsceneStageBinding`, `CreatePlayRequestFromStage`, `TryFindStage`. Test (PlayMode, `CutsceneTimelineSystemTests.cs` or a new `CutsceneStageApiTests.cs` in the same folder): `CreatePlayRequestFromStage_CopiesEveryStageBinding` — hand-build a stage entity (blob via the file's existing `BuildTestCutsceneBlob`, two bindings), call the API, assert the `CutsceneActorBinding` buffer holds both `(slotId, entity)` pairs and `CutsceneSlotRuntimeState.Length == blob.slots.Length`. Gate: compile + this fixture.
- [ ] **T2 — Authoring + baker.** `CutsceneStageAuthoring.cs` per §3.1. No `UnityEditor` token anywhere in the file, comments included (`Conformance_C` scans raw text). Gate: compile + `PackagingConformanceTests`. Prove the bake live: `execute_code` → build a `CutsceneAsset` in memory with one Prop slot, a scratch scene with one cube bound, run a `BakingUtility`/`ConversionWorld` bake if the package already has a baker test harness (grep `Tests/EditMode` for `Baker` first); if it has none, prove it by opening a subscene with the component in play mode and querying `CutsceneStage` through `execute_code`. Record which path you used in §7.
- [ ] **T3 — Sync to Stage** in the cast panel, per §3.3. **[parallel-safe with T2]** (Editor assembly only). No fixture — UI wiring. Prove via `execute_code` against a real open window: bind two objects, call the private sync method by reflection, assert the component exists in the right scene with two entries, `git status` clean after deleting the scratch scene.
- [ ] **T4 — Docs.** `Documentation~/cutscenes.md`: replace the "Playing a cutscene" snippet with the stage flow (author → Sync to Stage → `TryFindStage` + `CreatePlayRequestFromStage`), keep the manual-binding snippet as the fallback for spawned actors. `CHANGELOG.md` `[Unreleased]` → "Added — Cutscene stages". HANDOFF §4 one paragraph.
- [ ] **⏸ Owner checkpoint.** Open `Assets/Scenes/SubScenes/DOTSTestScene.unity`, open any cutscene, Place two actors, press Sync to Stage, enter Play mode, open Window ▸ Entities ▸ Hierarchy and find the `Cutscene Stage — …` entity: it must show `CutsceneStage` and a two-element `CutsceneStageBinding` buffer with non-null entities.

## 6. Risks and traps

- `AddBlobAsset` after `CutsceneBlobBuilder.Build` — the builder allocates `Persistent`; the store takes ownership. Disposing it yourself corrupts every later bake of the same content.
- A stage GameObject created in the *main* scene while the cast lives in a subscene bakes nothing (the main scene is not baked). §3.3's "scene of the first bound object" rule exists for this; do not simplify it away.
- `Conformance_D` flags the literal `Assets/` + identifier in package files. Write "the host's asset folder" in comments and docs.

## 7. Build log

(The executing session appends: drift found, path taken for T2's proof, anything owed.)
