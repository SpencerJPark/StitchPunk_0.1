# Amendment A83 — Decompose `ClipEditorWindow.cs` into pane elements

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.30.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 0, second.
> **Predecessors:** A82 (the shared column and split view, so the extracted panes do not carry
> raw split views). `ActorEditorPanel` hosting `ActorEditorLayersColumn` / `ActorEditorProfilesColumn`
> is the shape being copied.
> **Executor:** one orchestrator; **one worker per extraction, sequential**, four extractions, each
> gated before the next starts. This is the only spec in the roadmap that forbids a parallel wave —
> every task edits the same file.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A83** on the DOTS Animation Toolkit package (head `0.29.0` after A82).
Spec: `Assets/_Vault/Tasks/AnimationPackage/A83_WindowDecomposition_Spec.md`. Read it, then the
roadmap §3 protocol, then only what §3 here names. This spec produces **no behaviour change**: it
moves code out of a 9,300-line file into four pane elements. You are the orchestrator; do T0 and
T1 yourself, then run T2, T3, T4, T5 **one at a time**, each as one `worker` briefed with the exact
line ranges T1 produced, each followed by a compile gate and `ClipEditorLayoutTests`. Expect two
sessions; stop cleanly after any gated extraction and record where you are in §7.

---

## 1. Goal

`ClipEditorWindow.cs` is 9,319 lines plus 1,531 in `ClipEditorWindow.ComponentStack.cs`. No
session or subagent can read it; every window task since A56 has been briefed with grep-derived
line ranges. A80's two waves existed only because two tasks touched this file. After A83 the window
keeps the tab strip, the shared selection, session state, the transport target and the validation
badge; the four dock panes are elements:

| Element | UXML pane | Owns today (grep anchors) |
|---|---|---|
| `ClipListPane` | `clip-list-pane` | clip `ListView`, Clip Set field, clip create/rename/delete |
| `RigHierarchyPane` | `hierarchy-pane` | hierarchy `TreeView`, Rig field, `CountTracksForTarget`, drag-reparent |
| `ClipInspectorPane` | `inspector-pane` | key/event/track inspectors (`BuildEventInspector`, `KeyAddress` handling) |
| `TimelinePane` | `timeline-pane` | `TimeRulerElement`, `TrackLaneElement` rows, `PlayheadElement`, `EventLaneAddressing` callers, box select |

Each element is constructed with the same three references: `ActiveAssetSelection`,
`ClipPreviewController`, and a new `ClipEditorSession` value object (the selected clip, selected
key address, playhead) so panes talk through it instead of through window fields.

---

## 2. Decisions (recorded — do not re-ask)

- **A83-D1 — Characterise first, move second.** T1 captures every tab and runs the full EditMode
  suite before a line moves; the same captures and suite are the acceptance after each extraction.
  `ClipEditorLayoutTests` stays green throughout **unchanged** — the UXML does not change, only who
  queries it.
- **A83-D2 — One extraction per worker, one gate per extraction, in the table's order.** Clip list
  first because it has the fewest cross-references; timeline last because it has the most.
- **A83-D3 — The pane element queries its own UXML subtree.** The window passes the pane's root
  `VisualElement` (`rootVisualElement.Q("clip-list-pane")`) into the element's `Bind(VisualElement
  paneRoot, ...)`; the element does its own `Q<>` calls. No UXML edits.
- **A83-D4 — Cross-pane calls go through `ClipEditorSession` events**, never through a pane holding
  another pane. Where today's code calls a window method from what becomes another pane, the method
  becomes a session event (`SelectedClipChanged`, `SelectedKeyChanged`, `PlayheadChanged`,
  `RebuildRequested`) or stays on the window if it touches the transport or the badge.
- **A83-D5 — The `ComponentStack`, `CameraNavigation` and `RagdollHandles` partials stay where they
  are.** They are already partial files; A99 moves the ragdoll one.
- **A83-D6 — Nothing is renamed.** Methods keep their names when they move so grep still finds
  them and the vault's line-range notes only need a file, not a new name.
- **A83-D7 — The window's remaining size target is under 2,500 lines.** If an extraction leaves it
  above that, the next session's T0 decides what else to lift; do not lift it in the same task.

---

## 3. Read first

- `Editor/ClipEditor/ClipEditorWindow.cs` — **by grep only.** T1 produces the range map; workers
  read only their pane's ranges.
- `Editor/ClipEditor/ActorEditor/ActorEditorPanel.cs` lines 1–120 (how a host hands columns their
  roots and the composer) and `ActorEditorLayersColumn.cs` lines 1–60 (an element's `Bind`).
- `Editor/ClipEditor/Shared/ActiveAssetSelection.cs` in full (short).
- `Tests/EditMode/ClipEditorLayoutTests.cs` in full — the invariant.
- Vault `AnimationToolkit.md`: "Never rebuild a pane from a value-changed callback", "The header
  column and the lane column agree by height, not by index", "The Clip Editor goes dead after any
  recompile while it's open".

---

## 4. Design

### 4.1 `Editor/ClipEditor/ClipEditorSession.cs` (T1, orchestrator)

```csharp
public sealed class ClipEditorSession
{
    public ClipAsset SelectedClip { get; }
    public KeyAddress SelectedKey { get; }
    public float PlayheadNormalized { get; }
    public event Action<ClipAsset> SelectedClipChanged;
    public event Action<KeyAddress> SelectedKeyChanged;
    public event Action<float> PlayheadChanged;
    public event Action RebuildRequested;   // "something structural changed; panes re-query"
    public void SetSelectedClip(ClipAsset clip);
    public void SetSelectedKey(KeyAddress address);
    public void SetPlayhead(float normalizedTime);
    public void RequestRebuild();
}
```

Plain class, no `UnityEngine.Object`; the window owns one and passes it to every pane.

### 4.2 Pane element shape (T2–T5)

```csharp
public sealed class ClipListPane : VisualElement, IDisposable
{
    public void Bind(VisualElement paneRoot, ActiveAssetSelection selection,
        ClipEditorSession session, ClipPreviewController preview);
    public void Dispose();   // unregisters every callback it registered
}
```

The pane registers on `selection.ClipSetChanged` and the session events in `Bind`, and unregisters
in `Dispose`. The window calls `Dispose` on every pane in `OnDisable`, which also fixes the "goes
dead after recompile" trap's cousin: stale delegates on a destroyed window.

### 4.3 The range map (T1 output, pasted into §7)

For each pane: every method that becomes part of it, with start–end lines, and every window field
it reads, marked **moves** / **becomes session state** / **stays on window**. This is the brief for
T2–T5; a worker reads only its rows.

---

## 5. Tasks

- [ ] **T0 — Baseline (orchestrator).** Gate; totals in §7. `wc -l` on the window and its
  partials. Capture every tab to `Library/A83Captures/before_*.png`.
- [ ] **T1 — Range map + `ClipEditorSession` (orchestrator).** Grep the window for every method
  and field; classify by the §1 table; write §4.3 into §7. Write `ClipEditorSession.cs` by hand.
  Gate. Commit `A83-T1`.
- [ ] **T2 — `ClipListPane` (one worker, sequential).** Files: new
  `Editor/ClipEditor/Panes/ClipListPane.cs`, `Editor/ClipEditor/ClipEditorWindow.cs` (only the
  ranges T1 lists for this pane, plus the construction site in `CreateGUI`). Move, do not rewrite.
  Gate + `ClipEditorLayoutTests` + `ClipEditorAuthoringTests`. Commit `A83-T2`.
- [ ] **T3 — `RigHierarchyPane` (one worker).** Same shape; ranges from T1; carries
  `CountTracksForTarget` unchanged (A84 fixes its matching). Gate +
  `ClipEditorHierarchySelectionTests`. Commit `A83-T3`.
- [ ] **T4 — `ClipInspectorPane` (one worker).** Ranges from T1, including the two
  `(KeyAddress address, EventMarker marker, AnimEventKeyRegistry registry)` builders. Gate +
  `ClipEditorAddEventTests`. Commit `A83-T4`.
- [ ] **T5 — `TimelinePane` (one worker, possibly two if the range exceeds ~1,500 lines — then the
  lane rows and the ruler/playhead are split into two sequential workers).** Gate +
  `ClipEditorLayoutTests` + `ClipKeyClipboardTests`. Commit `A83-T5`.
- [ ] **T6 — Verification (orchestrator).** Full suites; totals must equal T0's. Captures
  `after_*.png`; compare with `before_*` — identical is the acceptance. `wc -l` the window; record
  against D7.
- [ ] **T7 — Docs (orchestrator or worker).** `CHANGELOG.md` `## [0.30.0]` (one paragraph: no
  user-visible change; four pane elements). Vault note: replace every "grep the member, read forty
  lines" instruction that names the window with the pane file. `package.json`.
- [ ] **T8 — ⏸ owner checkpoint.** Message: "Nothing should look different. Open the Clip Editor,
  pick a clip, scrub, add an event, add a key, drag a hierarchy row. If any of that misbehaves,
  that is this amendment. Line counts before/after are in the spec's §7."

---

## 6. Deliberately out of scope

- Any behaviour change, any rename, any UXML change.
- The `ComponentStack` / `CameraNavigation` / `RagdollHandles` partials (D5).
- The cutscene panel (5,175 lines) — a later amendment with the same shape if the owner wants it.

## 7. Build log

### 7.1 T0 — baseline (2026-09-12, head `a7bc2846`)

- Compile gate clean. EditMode **824** (823 pass, 1 pre-existing failure:
  `PackagingConformanceTests.Conformance_A_AsmdefReferenceLists_MatchSection13Exactly`, the
  `Unity.RenderPipelines.Universal.Runtime` reference — not this amendment's). PlayMode **283/283**.
- `wc -l` at head: `ClipEditorWindow.cs` **9,365** (the spec's 9,319 was A81-era; A82 added the
  divider work), `ClipEditorWindow.ComponentStack.cs` 1,531, `.RagdollHandles.cs` 447,
  `.CameraNavigation.cs` 80. **Drift:** three more `partial class ClipEditorWindow` files the spec
  does not list — `ClipEditorTransport.cs` 755, `ClipEditorKeyTransform.cs` 690,
  `ClipEditorView.cs` 635. They are treated like the D5 partials: already separate files, they stay.
- **Captures not possible.** `EditorApplication.isFocused` was `false` for the whole session and
  the live `ClipEditorWindow` instance sat at position (−12800, −12764) — unparked, no dock. Per the
  vault rule no stale frame was saved; `Library/A83Captures/` was not created. T6's "identical
  captures" acceptance therefore falls to the owner checkpoint (T8) plus the unchanged
  `ClipEditorLayoutTests`.

### 7.2 T1 — `ClipEditorSession` and the range map

`Editor/ClipEditor/ClipEditorSession.cs` written to the §4.1 shape. One reading recorded: **every
setter raises on every call, not only on a change.** `SelectClip(null)` is called today on a set
change and on a restore precisely to reset the playhead and rebuild the timeline even when nothing
was selected; a change-only event would silently drop that.

**How the ranges were produced (re-run after every extraction — line numbers below are at
`a7bc2846` and shift with each move):**

```
grep -nE "^\s{8}(public|private|internal|protected|static)[^=;(]*\(" Editor/ClipEditor/ClipEditorWindow.cs
```

Each method's range is its declaration (plus the contiguous comment/attribute lines above it) to
the line before the next 8-space-indented declaration. Field usage per method came from a
word-match of the window's 193 instance fields against each range.

**Two facts the §1 table did not anticipate (decided under the delegated-architecture mandate,
recorded as decisions here, flagged at the checkpoint):**

- **A83-D8 — the two multi-selections are session state, not pane state.** `selectedKeys`
  (`HashSet<KeyAddress>`), `activeKey`/`hasActiveKey`, and the hierarchy selection
  (`selectedHierarchyItems`, `activeHierarchyItemId`, `selectedTargetId`/`selectedSocketId`/
  `selectedBoneName`) are read by partials that stay on the window under D5 — `ComponentStack`
  (`FindHierarchyItemForKey`, `SocketBelongsToItem`, `ActiveHierarchyItem`, and it clears
  `selectedKeys` twice), `KeyTransform` (`BeginKeyTransform`, `CaptureTransformSnapshots`,
  `ResolvePivotTime`), `View` (`FrameSelection`) — and by the gizmo code (`selectedTargetId`,
  `selectedSocketId`). The §4.1 session's single `SelectedKey` cannot carry them. When T3 and T5
  run, the session grows `SelectedKeys` (the set itself, owned by the session), `HasActiveKey`, and
  a hierarchy-selection surface (`SelectedHierarchyItems`, `ActiveHierarchyItem`,
  `HierarchySelectionChanged`); `HierarchyItem`/`HierarchyItemKind` move from nested-private to
  file-scope `internal` in the pane file (a move, not a rename — D6 holds). No pane holds another
  pane (D4 holds).
- **A83-D9 — window fields are mirrored into the session, not replaced.** `selectedClip` (100+
  reads across seven files) and `playheadTime` stay as window fields; `SelectClip` and
  `SetPlayheadTime` additionally write the session. Replacing every read would be a rewrite, which
  the tasks forbid. Panes read the session; the window remains the writer.

**What `RebuildRequested` means (fixed here so T2–T5 agree):** a pane raises it after it created,
deleted or renamed a clip. The window's handler is exactly `MarkPreviewDirty()` +
`validationBadge.Refresh(activeRig, clipSet)`; each pane's handler is its own cheap re-query
(`ClipListPane`: `RefreshClipList` + `RefreshClipActionButtons`). It never rebuilds the inspector
or the hierarchy — both are gesture-guarded `Request…Rebuild` paths a pane calls explicitly.

#### 7.2.1 `ClipListPane` (T2) — 9 methods, 204 lines, no partial touches it

| Member | Lines | Disposition |
|---|---|---|
| clip-set-field block inside `BindToolbar` | 960–967 | **moves** into `Bind`; the lambda becomes a named handler so `Dispose` can unregister it |
| `BindClipList` | 1124–1155 | **moves** (body of `Bind`) |
| `CreateClip` | 1157–1187 | **moves**; `MarkPreviewDirty()` → `session.RequestRebuild()` |
| `RefreshClipList` | 1189–1200 | **moves**, becomes `public` |
| `RefreshClipActionButtons` | 1202–1215 | **moves**, becomes `public` |
| `DeleteSelectedClip` | 1217–1267 | **moves**; `SelectClip(null)` → `session.SetSelectedClip(null)`; preview+badge → `session.RequestRebuild()` |
| `SelectClipNearIndex` | 1269–1282 | **moves** |
| `MakeClipRow` | 3562–3567 | **moves** with `ClipRowUssClassName` (line 72, only user) |
| `BindClipRow` | 3569–3582 | **moves** |
| `OnClipSelectionChanged` | 4507–4516 | **moves**; `SelectClip(clip)` → `session.SetSelectedClip(clip)` |

Fields: `clipSetField`, `clipListView`, `newClipButton`, `deleteClipButton` (126–129) **move**.
`clipSet` → `selection.ClipSet`; `selectedClip` → `session.SelectedClip`; `activeRig`,
`validationBadge` **stay** (reached through `RebuildRequested`).

Window call sites re-pointed: `CreateGUI` 945 (`RefreshClipActionButtons`) → pane;
`RestoreView` 731–735 (`clipListView.SetSelection/ScrollToItem`) → new `public void
SelectClipRow(int clipIndex)` on the pane; `OnPanelChangedSetClips` 1889–1890 → pane's two public
refreshes; `ApplyClipSetSelection` 4480, 4488–4489 → deleted (the pane's own `ClipSetChanged`
handler does the field sync and the two refreshes); `SelectClip` 4526 → deleted (the pane's
`SelectedClipChanged` handler refreshes its buttons); `MakeClipNameField` 9297 → pane's public
`RefreshClipList` (the inspector is still on the window until T4). `SelectClip` becomes the
window's `SelectedClipChanged` handler and its two direct callers (`RestoreView` 745,
`ApplyClipSetSelection` 4481) call `session.SetSelectedClip(null)` instead.

#### 7.2.2 `RigHierarchyPane` (T3) — ~1,450 lines

**Moves:** `BindHierarchy` 1284–1309, `RefreshPrefabActionState` 1311–1325, `LoadedPrefab`
1327–1335, `ResolveHierarchyPath` 1337–1368, `ResolveTargetSourceNode` 1370–1394,
`FindItemIdByName` 1516–1534, skinned-source-field block in `BindToolbar` 1090–1105,
`BuildHierarchyContextMenu` 2186–2215, `FindBillboardRootIndexFor` 2247–2279,
`BuildBillboardAddressFor` 2281–2296, `FindRagdollBodyIndexFor` 2298–2340,
`BuildRagdollAddressFor` 2342–2368, `RebuildHierarchy` 3584–3624 (public),
`ResolveHierarchyEmptyMessage` 3626–3643, `BuildHierarchyItem` 3645–3667, `ResolveNodeTargetId`
3669–3680, `BuildRigTargetItems` 3682–3726, `DescribeSocketLabel` 3728–3741, `FindSocket`
3743–3760, `FindSocketIndex` 3762–3778, `MakeHierarchyRow` 3780–3803, `RegisterReparentDrag`
3805–3846, `CanDropOn` 3850–3877, `ReparentInPrefab` 3879–3902, `BindHierarchyRow` 3910–3938,
`FindRigTargetById` 3940–3957, `ApplyBillboardIndicator` 3959–4008, `ResolveHierarchyTransform`
4010–4030, `TryFindSocketSourceItemId` 4032–4063, `TryFindRigTargetItemId` 4065–4078,
`CountTracksForTarget` 4080–4110 (unchanged, A84 fixes it), `OnHierarchySelectionChanged`
4112–4133, `ApplyHierarchySelectionChange` 4135–4204, `IsHierarchySelectionEcho` 4206–4238,
`RefreshHierarchyRows` 4240–4259 (public), `SelectHierarchyItem` 4261–4278 (public),
`ApplyHierarchySelection` 4280–4335, `ActiveHierarchyItem` 4337–4351, `FindItemIdOf` 4353–4363,
`IsTargetSelected` 4365–4380, `IsBoneSelected` 4382–4398, `DescribeSelection` 4400–4422,
`DescribeHierarchyItemName` 4424–4429, `ClearHierarchySelection` 4431–4454 (public),
`FindBoneTrackIndex` 4456–4475. Nested `HierarchyItemKind`/`HierarchyItem` (≈373–397),
`RigTargetItemIdBase`, `NothingSelectedItemId`, `hierarchyItemsById`, `selectedHierarchyItems`,
`activeHierarchyItemId`, `previouslySelectedItemIds`, `isHandlingHierarchySelection`,
`selectedHierarchyItemId`, the three row USS constants and the two billboard glyphs (73–91).

**Stays on window** (touches preview, the dock, reconciliation or the window itself): prefab
round-trip `RememberRoundTripState` 1401–1412, `OnPrefabStageSaved`/`Closing` 1414–1438,
`FocusSelf` 1440–1450, `IsStageOurPrefab` 1452–1461, `ReloadAfterPrefabEdit` 1463–1487,
`RestoreRoundTripState` 1489–1514 (calls pane `SelectItemById`), `OpenPrefabForSelection`/
`OpenPrefabAt` 2141–2184 (re-docks the window; raised by the pane as an event `PrefabOpenRequested`),
`AppendBillboardMenuActions` 2217–2245 (calls `ComponentStack`'s `AddComponent`/
`ConfirmRemoveComponent` — D5; the pane's context menu raises `BillboardMenuRequested`),
`RefreshAfterBillboardEdit` 2370–2381, the reconcile block 1969–2139, `ApplyRigSelection`
3524–3560, `CommitRigBaseEdit` 1924–1964. Cross-pane readers of the selection go through the
session per D8: timeline `RebuildTimeline`/`PasteKeysAtPlayhead`/`SyncBoneSelectionToKey`,
inspector `RebuildInspector`, gizmo `RefreshGizmo`/`TryBeginGizmoDrag`/`ApplyGizmoDragValue`,
`RememberSessionState`, and the `ComponentStack` partial.

#### 7.2.3 `ClipInspectorPane` (T4) — ~2,300 lines

**Moves:** `BindInspector` 2445–2448, the live-binding block `ClearLiveInspectorBindings` …
`IsBeingEdited` 6683–6906, `RebuildInspector` 6988–7022 (public; still calls the window's
`BuildComponentStack` — D5 — through a delegate the window hands in at `Bind`),
`BuildKeyInspector` 7024–7102, `AddKeyValueFields` 7104–7123, `IsEasingPropertyName` 7125–7130,
`AddSelectedEventMarkerFields` 7132–7176, `AddEventKeyField` 7178–7189, `DescribeEventName`
7191–7196, `OpenEventKeyPicker` 7198–7209, `ApplyEventKeyChoice` 7211–7250,
`ResolveEventKeyAddressForFlatIndex` 7252–7261, `AddEventWindowField` 7263–7290,
`FindRegistryEntryByKey` 7292–7309, `DescribeEventKey` 7311–7326, `ResolveEventKeyRegistry`
7328–7332, `ResolveReferenceFrameRate` 7334–7342, `EditEventMarker` 7344–7360,
`AddSelectedFlipbookKeyFields` 7362–7412, `AddInterpolationControls` 7414–7460,
`ApplyEasingPreset` 7462–7489, `GetKeyInterpolation` 7491–7498, `SetKeyCurve` 7500–7527,
`EnsureUsableBezierHandles` 7529–7544, `GetKeyBezierHandles` 7546–7560, `AddBoneTransformFields`
7562–7660, `ReadRigEditBonePose` 7662–7686, `FindBoneTrack` 7688–7706, `DescribeBoneState`
7708–7717, `ApplyBoneEdit` 7719–7766, `ApplyBoneEditFromFields` 7768–7790, `AddSocketFields`
7792–7902, `MakeSocketBakeHint` 7904–7938, `BuildSocketTargetField` 7940–7978,
`BuildSocketBoneField` 7980–8011, `MakeSelectionHeading` 8098–8120, `BuildFlipbookTrackBlock`
8135–8248, `ApplyFlipbookEdit` 8250–8274, `MakeFlipbookResolvedLabel` 8276–8283,
`ApplyFlipbookResolvedLabel` 8285–8311, `ToggleFlipbookKeyMode` 8313–8333, `AddTransformFields`
9025–9143, `ReadRigEditPose` 9145–9167, `DescribeTransformState` 9169–9182,
`MakeTransformStateChip` 9184–9192, `BuildClipInspector` 9194–9216, `AddBoneTrackControls`
9218–9244, `AddBoneTrack` 9246–9278, `MakeClipNameField` 9280–9301, `MakeHeading` 9303–9308,
`MakeHint` 9310–9315, `AddBoundField` 9317–9324, `FindKeyProperty` 9326–9347,
`FindTrackKeyProperty` 9349–9362. Fields `inspectorPane`, `liveTransformBindings`,
`liveFlipbookBindings`, `flipbookTracks`, `flipbookTrackIndices`, `clipSerializedObject` (with
`RefreshSerializedClip` 4535–4542 — owned by the pane, refreshed on `SelectedClipChanged`, exposed
read-only to the window for `ComponentStack`).

**Stays on window:** the held-transform edit (169–219, 8843–9023: `RecordClipEdit`,
`CommitClipEdit`, `ResolveDisplayedTransform`, `ApplyTransformEdit`,
`ApplyTransformEditFromFields`, `CommitPendingTransformEdit`, `DiscardPendingTransformEdit`) —
shared with the gizmo; the socket commit trio `RecordSocketEdit`/`CommitSocketEdit`×2/
`CommitSocketPlacementEdit`/`ConfirmDeleteSocket` 8013–8096 — touch the preview; the retag block
8335–8841 (`ResolveTargetDisplayName` … `EnsureClipTrackTagsAssigned`) — shared by the timeline
headers and the inspector; the deferred-rebuild quartet 6908–6986; `ComponentStack` in full (D5).
The pane reaches every one of these through delegates handed in at `Bind` or through session
events; T4's brief lists the exact set.

#### 7.2.4 `TimelinePane` (T5) — ~2,400 lines in the main file, split into two workers

**Worker T5a — lanes, keys, selection:** `RebuildTimeline` 4777–4959 (public), `AddTrackRow`
4961–5117, `CollectEventWindowLengths` 5119–5139, `AddLane` 5141–5169, `BindTrackHeaderWrap`
5171–5189, `MakeTrackKey` 5191–5194, `ToggleTrackExpanded` 5196–5203, `GetChannelNames`
5205–5222, `RepaintLanes` 5224–5230, `RefreshLaneKeys` 5232–5267, `OnKeyPointerDown` 5269–5333,
`SyncBoneSelectionToKey` 5335–5374, `OnDragMove` 5376–5391, `UpdateKeyDrag` 5393–5427,
`ShowDragReadout` 5429–5442, `TickDragAutoScroll` 5444–5484, `OnDragEnd` 5486–5514,
`OnLanePointerDown` 5516–5556, `OnGhostLanePointerDown` 5558–5580, box select 5587–5718,
undo gesture 5720–5754, `OnTimelineKeyDown` 5756–5824, clipboard 5826–5917, `DeleteSelectedKeys`
5919–5991, `GetKeyTime`/`SetKeyTime`/`ResolveEventFlatIndex` 5993–6067, `InsertKey` 6069–6158,
events 6160–6359, sorting and selection 6361–6648. Fields: `expandedTrackKeys`,
`eventWindowLengths`, `pasteDestinations`, the drag block (`isDraggingKeys` … `dragTrackIndex`),
`lastSortIndexMap`, `timelineRowCount`, the box-select block.
**Worker T5b — ruler, playhead, header column:** `BindTimeline` 2450–2490, `BindTrackHeaderResizer`
2604–2679, `SetTrackHeaderWidth` 2681–2687, `ApplyTrackHeaderWidth` 2689–2719, the header-width
fields and constants (43–60, 296–306), `ruler`, `playhead`, `ghostLanes`, `laneStack`,
`laneColumn`, `trackHeaderColumn`, `statusLabel`, `boxSelectElement`.

**Stays on window:** `SetPlayheadTime` 4719–4758 (transport + ragdoll toggle + inspector live
refresh — becomes the window's `PlayheadChanged` handler; the pane's ruler scrub calls
`session.SetPlayhead`), `SnapFrameCount` 4760–4775 (reads the toolbar toggle; handed in as a
delegate), the `View`/`Transport`/`KeyTransform` partials in full (they read `viewPan`/`viewZoom`,
`laneColumn`, `laneStack`, `ruler`, `playhead` — after T5 those live on the pane and the partials
reach them through pane properties of the same names; this is the largest re-point in the
amendment and the reason T5 is last).

#### 7.2.5 T1 gate

_(recorded below when done)_

