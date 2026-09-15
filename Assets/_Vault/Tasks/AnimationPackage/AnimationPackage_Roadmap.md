# DOTS Animation Toolkit — package roadmap after A81 (written 2026-09-10)

> **Status:** 📝 nineteen specs written 2026-09-10; A82 and A83 built and accepted 2026-09-12; A84 built 2026-09-12 as `0.31.0` and A85 built 2026-09-13 as `0.32.0`, both accepted 2026-09-13. A86 (`0.33.0`) built 2026-09-13 and accepted the same day. A87 (`0.34.0`) built 2026-09-13 and accepted the same day (D1: a seek fires nothing, as built; D5: the flash stays, and now also shows on a selected pin). A89 built 2026-09-13 as `0.35.0` (A88 unbuilt; it takes the next free minor) and accepted the same day. T8 was answered, and its three follow-ups (T9–T12) are built. A91 built 2026-09-13 as `0.36.0` and accepted the same day (a real player build was attempted 2026-09-13 but blocked by unrelated game compile errors; accepted as working for now, check tracked in `Assets/_Vault/Spencer/verify-a91-player-build.md`); every A94 prerequisite is now built. A90 built 2026-09-13 as `0.37.0` and accepted the same day. A88 built 2026-09-13 as `0.38.0` (the specced `0.35.0` went to A89; 13 T0 drifts settled in its §7); its T9 owner checkpoint is open. A92 built 2026-09-14 as `0.39.0` (13 T0 drifts in its §7; cutscene part tracks included and merge payloads left raw, owner calls 2026-09-14); its T10 owner checkpoint is open. A93 (`0.40.0`), A94 (`0.41.0`) and A95 (`0.42.0`) built 2026-09-14 in one parallel worktree batch, integrated in `ffdc6754` (A93's routing asset default moved to `Assets/Generated/DotsAnimationToolkit/` by Conformance_D); their T16, T14 and T15 owner checkpoints are open. Next: A96, A97 and A98 (`Assets/_Vault/Spencer/next-session-parallel-a96-a98-prompt.md`).
> **Where:** every spec lives beside this file in `Assets/_Vault/Tasks/AnimationPackage/`. Each
> spec's §0 is its session prompt — paste it into a fresh Sonnet session.
> **What this replaces:** `Docs/AnimationToolkit/Handoff_NextFive_2026-09-09.md` items 1 and 2 are
> re-specced here as A82 and A83; items 3 (A79), 4 (release readiness) and 5 (PlayerUnit) stay
> where they are and are not in this roadmap.

## 1. The order, with checkboxes

> **⏸ Owner to-do (left 2026-09-13, answer in any session):**
> - [ ] **A87 follow-up (built 2026-09-13, not yet seen):** select an event pin in a clip, then scrub across
>   it. It should swell for about a tenth of a second, then show its selection outline again.
> - [ ] **A88 T9 (built 2026-09-13 as `0.38.0`, not yet seen):** Actor Profiles ▸ MaleCitizen ▸ ▶ Walk
>   on Base, then ▶ MeleeContinuous on Action (Blink already runs on Eyes). Under the preview's
>   transport, the Layer Events strip has one row per layer; MeleeContinuous's `Attack` pin at 0.35
>   flashes once per ▶ (silently, no preview clip), and being play-once its row dims by itself at the end.
>   Two questions: does dimmed/hollow read as "will not fire"? Should the strip start collapsed?
> - [ ] **A92 T10 (built 2026-09-14 as `0.39.0`, not yet seen):** MaleCitizen's `MeleeContinuous` clip ▸
>   right-click the `Attack` pin ▸ Change key everywhere… ▸ pick any other event. The dialog names every
>   clip, cutscene and profile using `Attack`; Cancel changes nothing. Apply rewrites them, and one Ctrl+Z puts
>   them all back. The same flow is behind Merge into… on an Event Keys row, Replace in clips with… on a
>   Target Tags row, and right-click on a Rigs tab Tag button. Merge also rewrites the event registry, which
>   has uncommitted edits. Question: do the dialog wording and the four entry points read right?
> - [ ] **A93 T16 (built 2026-09-14 as `0.40.0`, not yet seen):** Clip Editor ▸ Events. Left: your event keys
>   with the 64-key budget line. Middle: the selected key's fields and every clip, cutscene and profile using it
>   (click a row to ping it). Right: its routes, and Generate consumer stub…. Two questions: the routing asset now
>   auto-creates at `Assets/Generated/DotsAnimationToolkit/AnimEventRouting.asset` (the spec said
>   `Assets/Settings/…`; the package may only name `Assets/Generated`) — right place? Should the Routes column
>   exist at all, or is the stub alone enough?
> - [ ] **A94 T14 (built 2026-09-14 as `0.41.0`, not yet seen):** Clip Editor ▸ Health ▸ Scan. Expect three
>   rows: H06 `VatSampleTentacleClips` unbaked (pinned first, Rebake + Locate), H02 `NewClipSet` lists 3 missing
>   clips (Remove missing), H05 `VatSampleTentacleRig` used by no profile. Questions: which rules are noise? Should
>   H01 offer Delete? Should the tab read "Health (1)" while a stale or unbaked VAT finding exists?
> - [ ] **A95 T15 (built 2026-09-14 as `0.42.0`, not yet seen):** Clip Editor ▸ Sprite Sheets ▸ New, drag four
>   same-size images in from Images, reorder, Bake, Save; drag the array into a `ToolkitSpriteUnlitArray`
>   material's `_MainTexArray`. Then on a sprite key pick a Frame by name. Questions: want a compressed-array
>   follow-up (arrays bake uncompressed RGBA32)? Schedule the game-side task retiring `TextureArrayBuilder.cs`?
> - Answering any of these ticks its box below and updates the status line above.

Tick a box only when the spec's own status line says built **and** its ⏸ owner checkpoint (if any)
has been answered. A spec's session ticks its own tasks inside the spec; this list is the owner's
view. Versions are the expected `CHANGELOG.md` slot; if the changelog has moved, take the next
free minor and correct the spec's status line.

### Phase 0 — the two prerequisites (sequential, in this order)

- [x] **A82 — One catalog column, one cover-pane split** (`0.29.0`) — [`A82_SharedCatalogColumn_Spec.md`](A82_SharedCatalogColumn_Spec.md). Extracts `ToolkitCatalogColumn<TAsset>` from the five near-identical catalogs and a `CoverPaneSplitView` that remembers its divider. Every later tab builds on both.
- [x] **A83 — Decompose `ClipEditorWindow.cs` into pane elements** (`0.30.0`) — [`A83_WindowDecomposition_Spec.md`](A83_WindowDecomposition_Spec.md). Four sequential extractions behind a characterisation pass. No behaviour change. After this, every tab task below is parallel-safe against the window file.

### Phase 1 — infrastructure (A84 first; A85–A92 are independent of each other and can run in any order, or in parallel sessions on separate branches)

- [x] **A84 — Asset Reference Index** (`0.31.0`) — [`A84_AssetReferenceIndex_Spec.md`](A84_AssetReferenceIndex_Spec.md). One editor service answering "where is this rig / clip / set / profile / event key / tag used". Fixes the tag-bound undercount in delete confirmations. A92, A93 and A94 read it.
- [x] **A85 — Event payload schema** (`0.32.0`) — [`A85_EventPayloadSchema_Spec.md`](A85_EventPayloadSchema_Spec.md). A registry entry says what `intParam` / `floatParam` mean; the Clip Editor shows a dropdown instead of a raw integer; generated constants carry the meaning.
- [x] **A86 — One event editing surface for clips and cutscenes** (`0.33.0`) — [`A86_UnifiedEventEditing_Spec.md`](A86_UnifiedEventEditing_Spec.md). Same inspector, lane drawing and validation for `EventMarker` and `CutsceneEventMarker`; serialized types untouched.
- [x] **A87 — Scrub crossings and sound on scrub** (`0.34.0`) — [`A87_ScrubEventCrossings_Spec.md`](A87_ScrubEventCrossings_Spec.md). Playhead-before / playhead-after crossing detection in the editor, an editor-only preview `AudioClip` per registry entry. Lifts the standing "do not start it" (HANDOFF §5) on the owner's 2026-09-10 instruction.
- [ ] **A88 — Layered event preview in the Actor Editor** (`0.38.0`) — [`A88_LayeredEventPreview_Spec.md`](A88_LayeredEventPreview_Spec.md). Per-layer marker strip under the composited preview; inactive and crossfade-source layers drawn as non-emitting.
- [x] **A89 — Stale VAT bake detection** (`0.35.0`) — [`A89_StaleVatBakeDetection_Spec.md`](A89_StaleVatBakeDetection_Spec.md). Compute the source hash without baking; badge the VAT Bake tab and the clip set when it differs from `VatTextureSetAsset.sourceHash`.
- [x] **A90 — Camera-data fallback warning** (`0.37.0`) — [`A90_CameraDataWarning_Spec.md`](A90_CameraDataWarning_Spec.md). A project with no `AnimationToolkitCameraData` writer gets one warning naming the sample, not silent spherical billboarding.
- [x] **A91 — Profile P2 at save and at build** (`0.36.0`) — [`A91_ProfileP2AtBuild_Spec.md`](A91_ProfileP2AtBuild_Spec.md). Animation-name membership checked on asset save and as a build preprocessor; the bake still cannot do it and the spec says why.
- [ ] **A92 — Project-wide refactor operations** (`0.39.0`) — [`A92_RefactorOperations_Spec.md`](A92_RefactorOperations_Spec.md). Re-key an event, merge two keys, replace a tag across every clip, set, profile and cutscene in one undo step. Needs A84.

### Phase 2 — tabs (A93 and A94 first; the rest in any order)

- [ ] **A93 — Events tab** (`0.40.0`) — [`A93_EventsTab_Spec.md`](A93_EventsTab_Spec.md). Registry catalog, payload + usage, a routing table baked to a blob hosts read, a consumer-stub generator. Needs A84, A85.
- [ ] **A94 — Health tab** (`0.41.0`) — [`A94_HealthTab_Spec.md`](A94_HealthTab_Spec.md). One project-wide findings list replacing the per-clip badge for cross-asset problems. Needs A84, A89.
- [ ] **A95 — Sprite Sheets tab** (`0.42.0`) — [`A95_SpriteSheetsTab_Spec.md`](A95_SpriteSheetsTab_Spec.md). `Texture2DArray` flipbook builder with a contact-sheet preview, writing the layer indices sprite tracks already expect; frame-by-name picker on sprite keys. Atlas output dropped 2026-09-12 (spec D0).
- [ ] **A96 — Materials tab** (`0.43.0`) — [`A96_MaterialsTab_Spec.md`](A96_MaterialsTab_Spec.md). Every actor material against the shader contract; create-from-template.
- [ ] **A97 — Retarget tab** (`0.44.0`) — [`A97_RetargetTab_Spec.md`](A97_RetargetTab_Spec.md). Clip × rig binding table with per-row tag remap and roster coverage.
- [ ] **A98 — Capture tab** (`0.45.0`) — [`A98_CaptureTab_Spec.md`](A98_CaptureTab_Spec.md). PNG sequence (and optional GIF) from the preview camera for a clip, profile animation or cutscene.
- [ ] **A99 — Ragdoll tab** (`0.46.0`) — [`A99_RagdollTab_Spec.md`](A99_RagdollTab_Spec.md). Bodies, limits and the drop simulation get their own three-column home; the Clip Editor keeps only the preview toggle.
- [ ] **A100 — Stats tab** (`0.47.0`) — [`A100_StatsTab_Spec.md`](A100_StatsTab_Spec.md). Play-mode counts: actors, events per frame, LOD histogram, VAT texture memory, group timings.

## 2. Standing owner calls these specs inherit (do not re-ask)

- Names, never numbers, in game code and editor surfaces (HANDOFF §5).
- No manual asset wiring: vocabularies auto-create under `ProjectSettings/`.
- Events are authored loosely; downstream systems read and redirect. **The package ships no
  handler** — A93's routing table is data a host reads, and the stub generator writes into the
  host's project, not the package.
- Sound mixing is not this package. A87 plays a preview clip in the editor and nothing else.
- New rigs are created fresh; build no migration paths. A86 therefore leaves both marker types'
  serialized fields alone.
- UI Toolkit only in editor sources (`Conformance_E`); one `<summary>` per file (`Conformance_F`);
  static-class suffix vocabulary (`Conformance_G`, plain nouns go on the allowlist).
- Every new tab: a `ClipEditorTab` value, a `ToolbarToggle` in `tab-strip`, a cover pane in the
  UXML, a `BindTab` line, a `Show…Tab` call, and a `ClipEditorLayoutTests` update. Each tab spec
  lists these six edits as one orchestrator task ("window wiring"), done after the wave so the
  wave never touches `ClipEditorWindow.cs`. Tab order in the strip: new tabs go **after** Cutscene
  Director unless a spec says otherwise.

## 3. Execution protocol (binding for every session running a spec here)

This is the Cutscene roadmap's §4 protocol with the 2026-09-08 subagent budget rules folded in.

1. **Read, in order:** repo root `CLAUDE.md`; `Docs/AnimationToolkit/HANDOFF.md` §2, §3, §5, §6;
   `Assets/_Vault/Memories/Code/RULES.md`; the spec in full; then only the files the spec's §3
   names, at the line ranges it names. Do not read the other specs in this folder.
2. **Ground before writing (T0).** Every spec's T0 is the orchestrator's: baseline gate, record
   discovered totals, verify the names the spec uses still exist (`grep`, not whole-file reads), and
   run any probe the spec asks for. Names were verified on 2026-09-10 against `63190d66`; if one has
   drifted, follow the code and log the drift in the spec's §7.
3. **You are the orchestrator; you alone touch `mcp__UnityMCP__*`.** Spawn `worker` (edits) or
   `verifier` (read-only), never `general-purpose`. Every worker brief: the spec path, the task's
   text, its "Files" and "Read" lines, the §4 block it builds, the hard rules (no `var`, no
   single-letter names, explicit types; one `<summary>` per file ≤3 lines, no `§` or amendment
   citations in shipped code), and the closing lines "at turn 30 stop editing and write your report;
   report ≤ 30 lines; never call any `mcp__UnityMCP__*` tool." At most two files per worker, named
   line ranges only. **Spawn from the repo root.** A capped worker is never resumed: read its diff,
   spawn a fresh worker for what is missing.
4. **Waves.** Tasks marked `[parallel-safe]` in one wave spawn together; wait for all; one compile
   gate over the wave; then the fixtures the wave's tasks name. Tasks not marked are the
   orchestrator's own or sequential.
5. **Prove a test can fail.** Before keeping any fixture, revert the fix and watch it fail; a test
   that passes both ways is deleted. Roughly two fixtures per spec, often zero for UI wiring.
6. **Full suites once, at the spec's end** (`DotsAnimationToolkit.Tests.EditMode`, then
   `.PlayMode`). The discovered total must not drop. Then the spec's drive step: run the feature
   against a real asset, prove writes persist by reloading from disk, capture the tab, and look at
   the capture.
7. **Commit per wave**, `A8x-Tn:` prefix naming every task, staging paths explicitly, never
   `git add -A`. Push when green.
8. **⏸ owner checkpoints are real stops.** End the session with the message the spec gives.
9. **Escalate, never quietly re-spec.** A spec/reality conflict is a §7 note and a question at the
   end, not a silent doc edit.
10. **Close:** the spec's status line, `HANDOFF.md` §4 (one paragraph), `CHANGELOG.md`, the
    `Documentation~` page the spec names, and `Assets/_Vault/Memories/Code/AnimationToolkit.md` if
    you learned a trap. Tick the spec's box in this file.

## 4. Shared vocabulary (every spec uses these names)

- **Catalog column** — the boxed two-line row list with search, New, Refresh and right-click
  Rename/Delete that A75/A76/A80/A81 each built by hand; A82's `ToolkitCatalogColumn<TAsset>`.
- **Cover pane** — a full-window `VisualElement` in `ClipEditorWindow.uxml` shown for one tab
  (`clip-editor__cover-pane`), e.g. `texture-packer-pane`.
- **Shared selection** — `ActiveAssetSelection` (`Editor/ClipEditor/Shared/`), one per window,
  the Clip Set and Rig every tab reads and writes.
- **Registry** — the three vocabulary ScriptableObjects under `ProjectSettings/`:
  `TargetTagRegistry`, `AnimEventKeyRegistry`, `AnimationNameRegistry`, reached only through
  `VocabularyRegistryProvider` and persisted only through its `Persist` overloads.
- **Pulse / window** — the two event channels: `AnimEventOutput` (one frame) and `AnimEventMask`
  (a bit held open for `windowSeconds`). Keys 16–79 are maskable; above 79 pulse-only.
- **Gate** — HANDOFF §3 steps 1–2 per wave; steps 3–4 once per spec.
