# Phase F — Rig-Centric Binding

Owner-initiated 2026-08-28. Corrects an inverted data hierarchy before any real animation content
is authored. **No migration of existing animation assets — the owner is re-authoring from
scratch. Foundation over compatibility.**

## 1. The problem

The hierarchy is upside down. `ClipSetAsset.rig` pins every set to one rig (rule V06), the Clip
Editor is set-centric (`clipSet.rig` is the rig everywhere), and `ActorAuthoring` derives its rig
from its one set. The owner's model is the inverse:

- **The rig is the top-level identity.** An actor states its rig directly.
- **A clip set is a rig-agnostic collection of motion**, expressed through the shared tag
  vocabulary: tags + events dictating transforms on whatever targets carry those tags.
- **One rig takes several clip sets** (same rig, different movement styles, per actor).
- **One clip set plays on any rig whose tags partially align** — only the aligning tags animate.
  T2's lenient skip (Phase E spec §6.1) is already exactly this; Phase F extends the stance to the
  structure around it.

Phase E built the track-level half: tag-bound tracks, T2/T3, and the null-`clip.rig` shareable
clip exemption. Phase F inverts the asset spine those tracks hang from.

## 2. Target model

```
RigAsset                     — targets (tagged), layers, sockets, billboards, ragdoll. Unchanged.
ClipSetAsset                 — clips + vatTextures + editor-only previewRig. No semantic rig.
ClipAsset                    — unchanged. clip.rig stays the *authoring* rig (null = tag-only).
ActorAuthoring               — rig (required) + clipSets (list, at least one entry).
```

Binding resolution — which dense target a track drives — happens **at bake, against the actor's
rig**, never against anything stored on the set.

## 3. Data changes

- **`ActorAuthoring`**: gains `public RigAsset rig` and replaces `clipSet` with
  `public List<ClipSetAsset> clipSets`. The old "the rig comes from the set, so an actor cannot
  disagree with its clips" remark inverts: the rig comes from the actor, and clips that do not
  align simply skip (§4).
- **`ClipSetAsset.rig`**: deleted. Replaced by `#if UNITY_EDITOR public RigAsset previewRig` —
  the rig the set was last authored against, for the Clip Editor to open with. Never a bake
  input, never in a player build (the `RigAsset.sourcePrefab` pattern and reasoning). Renaming
  the field (not just re-meaning it) forces every one of the ~40 `clipSet.rig` call sites to be
  consciously revisited during F5.
- **`ClipSetAsset.eventKeys`**: deleted. A52 made the `ProjectSettings/` vocabulary canonical;
  the per-set override is a second source of truth with no remaining reason. The
  "carries its vocabulary between projects" rationale is served by the registry's own
  export/import path, not by a per-set asset reference.
- **`ClipAsset.rig`**: kept, same meaning as Phase E left it — the rig the clip was authored
  against, used by the editor for preview and editor-time V02, and by the Mirror utility.
  Null remains the fully-shareable tag-only clip.

## 4. Binding resolution rules

- **Tag-bound tracks**: unchanged. Resolved against the actor rig's tag→target map; a tag the
  rig does not carry is **skipped with a warning** naming clip + track + tag + rig (T2). A tag
  absent from the registry stays an **error** (T3).
- **Id-bound tracks — new rule T4**: at bake, an id-bound track whose `targetId` the actor's rig
  does not declare is **skipped with a warning** naming clip + track + id + rig — the exact
  mirror of T2. Rationale: a set applied to a second rig legitimately carries id-bound tracks
  that only its home rig declares; a hard V02 at bake would ban the scenario this phase exists
  for. The genuine-authoring-error case (a dangling id on the clip's own rig) still surfaces as
  an **editor-time V02 error judged against `clip.rig`**, where it was authored.
- **V06 retired** entirely — there is no set rig to match.
- **T1** (a non-zero tag unique within a rig) unchanged.

## 5. Bake

`ClipRegistryBuilder` builds one blob per **(rig, set-list) bind**, not per set:

- Signature becomes `Build(RigAsset rig, IReadOnlyList<ClipSetAsset> clipSets, ...)` (and the
  same for `TryComputeContentHash` / validation entry points).
- Canonical clip list = union of every set's clips, deduplicated by asset identity (V11 extends
  across sets, still a warning), sorted by clip id as today. Two **distinct** clips sharing an id
  anywhere in the union is V05, still an error. Set list order is canonicalised (sort by set
  stableId) so it can never matter.
- Canonical targets, dense indices, and the tag map come from the actor's rig — mechanically the
  same code, different rig source.
- **`ClipRegistryBlob.setKey` becomes the bind key**: `rig.stableId` XOR-folded with every set's
  `stableId`. It was always a dedup/diagnostic identity, nothing at runtime looks a clip up by
  it. The dedup key composition (`ComposeDedupKey`) takes the same fold.
- **`SchemaVersion` → 9.** The layout may not change shape, but a v8 blob's `setKey` and a v9
  bind key are different identities for the same bytes, and the hash stream's meaning changes
  with the merged-clip canonical order. Same-bytes-different-meaning is exactly what the version
  gate exists for.

## 6. Bakers

- `ActorBaker`: registry from `(actor.rig, actor.clipSets)`; `DependsOn` every set and the rig.
- `RigTargetBaker`: the actor-rig-vs-part-rig comparison reads `actor.rig`, not `clipSet.rig`.
- `startingLayers`: a starting clip must be a member of the merged union (error message names
  which sets were searched); layer indices validate against `actor.rig.layers` as before.

## 7. Clip Editor

- Every `clipSet.rig` read routes to `previewRig` (most already funnel through the
  `ComponentStack.cs` accessor and a handful of window-level locals). The toolbar rig picker
  writes `previewRig` with the same undo/dirty handling it gives `clipSet.rig` today.
- **New-track creation default flips**: a track created on a target that carries a tag defaults
  to **tag-bound**; an untagged target defaults to id-bound. Phase E's "sharing is opt-in per
  track, never a migration" directive concerned existing data, which stays untouched; under the
  rig-centric model tag-binding is the primary authoring intent, and defaulting new tracks to
  id-bound would quietly produce sets that do not share.
- Nothing else changes on-screen this phase. "Which rigs can play this set" browsing is out of
  scope (§9).

## 8. VAT

A VAT texture encodes one skinned mesh's vertex motion — it **cannot retarget** and no rule can
make it. So:

- `VatTextureSetAsset` gains `sourceRigKey` (ulong), stamped with the rig's `stableId` by the
  texture baker at bake time.
- Actor bake **errors** when a bound set's `vatTextures.sourceRigKey` is non-zero and differs
  from `actor.rig.StableId` — a wrong-mesh VAT is never wanted, and the failure it prevents
  (another character's mesh motion) is invisible to tests and obvious to a player.
- Key 0 (anything baked before this field) passes — consistent with no-migration.
- Transform/sprite/billboard content in the same set still shares normally; only the VAT-driven
  parts pin the set to its source rig in practice.

## 9. Out of scope

- **Runtime set switching** on a live actor. The registry stays one immutable blob per bind;
  changing a loadout at runtime is a later phase's question (prefab variants or registry swap).
- Editor browsing/matrix of rig↔set tag alignment.
- Any migration path or compatibility shim for pre-F assets. Old serialized `clipSet.rig` /
  `eventKeys` data silently drops on load; that is accepted.
- Downstream event consumers (standing directive — unchanged).

## 10. Decisions (recorded per the delegation directive; flag before F1 lands if any is wrong)

- **D1 — sets attach at the actor, not on `RigAsset`.** "One rig, different clip sets applied"
  is per-actor by construction; a set list on the rig asset would force one loadout per rig and
  rig duplication to vary it. A "default sets" convenience on the rig can be added later without
  reversing anything.
- **D2 — `previewRig` is editor-only.** The one thing a set still knows about rigs is which one
  it was last authored against, and that is workflow state, not semantics.
- **D3 — T4 is lenient** (skip + warning at bake; error stays editor-time against `clip.rig`).
- **D4 — `eventKeys` dies** rather than deprecates. No migration burden exists to justify a
  deprecation window.
- **D5 — bind key in the existing `setKey` slot, `SchemaVersion` 9.**
- **D6 — new tracks default to tag-bound on tagged targets.** Creation default only; no data
  rewrite.

## 11. The queue (mirrors HANDOFF §4)

- **F1 — Data model.** §3 asset/authoring changes; mechanical compile-fix of every reference
  (bake temporarily reads the first set only if needed to stay green mid-task — do not commit
  that state). Gate after: EditMode + PlayMode counts must not drop.
- **F2 — Builder + validation.** §5 build-from-bind, §4 rule changes (V06 out, T4 in, V05/V11
  across the union), schema 9, bind key. The determinism, data-contract, golden-hash, and
  builder tests all pin the old shape — update them **as assertions of the new contract**, never
  by loosening.
- **F3 — Bakers.** §6. PlayMode acceptance fixtures re-point from set-rig to actor-rig.
- **F4 — Clip Editor.** §7 `previewRig` plumbing + the creation default. Prove the persist path
  with a real asset save/reload, per HANDOFF §3.
- **F5 — VAT stamp.** §8.
- **F6 — Samples + docs.** Rewire both sample builders (Samples~ is excluded from compilation —
  compile-check via a temp assembly before trusting it), rewrite `sharing-clips.md` around the
  rig-centric model, CHANGELOG, package version → 0.13.0.

Each task commits separately, paths staged explicitly, full gate between tasks.

## 12. Risks

- **The Clip Editor is the blast radius** (~40 `clipSet.rig` sites in `ClipEditorWindow` alone).
  Mechanical, but HANDOFF §9 lesson 2 applies: run the window, don't just read the diff.
- **Merged-set clip-id collisions** become possible the moment two independently-authored sets
  meet on one actor. V05-across-the-union must be in place (F2) before anyone can author that
  state (F4's editor work), which the queue order guarantees.
- **`BindingReconciler`, `MirrorClipUtility`, `ClipAssetUtility`, `VatBakePanel`,
  `ActorAuthoringEditor`, `NewRigPanel`** all read `clipSet.rig` today — find them by compile
  error after the rename, not by this list (it rots).
