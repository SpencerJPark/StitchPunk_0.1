# Amendment A80 — one clip set, one rig, every tab: the toolbar fields move into the tabs that own them

> **Status:** ✅ built 2026-09-09, shipped as 0.27.0. One ⏸ owner checkpoint open (§5 T13, three ⚠ interpretations).
> **Prompt:** [`Amendment_A80_SharedAssetSelection_Prompt.md`](Amendment_A80_SharedAssetSelection_Prompt.md).
> **Predecessors:** [`Amendment_A75_ClipSets_Spec.md`](Amendment_A75_ClipSets_Spec.md) and
> [`Amendment_A76_RigsTab_Spec.md`](Amendment_A76_RigsTab_Spec.md) — their catalogs become the
> pickers; A77 (no doc) made both catalogs create/rename/delete in place; A78 (0.26.0) is head.
> **Executor:** one Editor-connected orchestrator running the gate; `worker` subagents edit files in
> two waves (fifteen at once, then two) and never touch MCP. Every task is at most two files with
> named line ranges, sized to finish well inside a 40-turn / ~100k-token budget.

---

## 1. What the owner asked for (2026-09-09, verbatim where it matters)

> "I want to remove the clip set and rig set inputs (the first two inputs before the actual tab
> buttons) from the tool bar, have them located in their related windows, and their values shared
> between windows. an example would be I select a clip set in the Clip Sets tab, and I pick a rig
> from the Rigs Tab, when I open Clip Editor, those are already set as the set clip set and rig
> there, they also would be set in Vat Bake and actor profile, if I change them out in any of the
> tabs then they will be swapped in the other tabs as well. Clip Set and Rigs already visually
> supports this change, for clip editor I want them set in the left column above their respective
> parts. so Clips will have the set clip set there and then all the children will show the clips
> and allow the ability to edit those clips or add more, same for rig in clip editor, it will be
> above the rig hierarchy so it again shows, this is where the hierarchy comes from. in vat bake
> the warning will just be removed and they will no longer be ghost place holders in the top of the
> left panel. and then in actor profiles that page will be turned into 4 columns. 1 will be the
> profiles modeled with the same search bar off of the rigs part of the rigs window, then 2 the
> layers of that particular profile, 3 the preview, 4 the actor inspector. all columns resizable
> and in the first column at the top it will have clip."

The acceptance picture, tab by tab:

```
Top bar:   [Clip Sets][Rigs][Clip Editor][VAT Bake][Actor Profiles][Cutscene Director]      (⚠ badge)
           — no object fields; the tab strip starts the bar.

Clip Editor left column:
┌ Clips ──────────────── [+][🗑] ┐    ┌ Rig Hierarchy ──── [Edit][Prefab] ┐
│ [MaleCitizen.set        ○]    │    │ [MaleCitizen.rig          ○]     │
│  Walk                         │    │  ▸ Root                          │
│  Idle                         │    │    ▸ Torso                       │
│  MeleeContinuous              │    │      ▸ Head                      │
└───────────────────────────────┘    └──────────────────────────────────┘

VAT Bake left column:                Actor Profiles:
  Source                             ┌ Profiles ──┬ Layers ─┬ Preview ──┬ Actor Inspector ┐
  Clip Set  [MaleCitizen.set  ○]     │[set     ○] │ Base    │           │                 │
  Rig       [MaleCitizen.rig  ○]     │[rig     ○] │  Idle   │    ▄▄▄    │                 │
  MaleCitizen ▸ 3 VAT parts …        │ 🔍 search  │  Walk   │   ▐███▌   │                 │
  (no hint line)                     │ ┌────────┐ │ Action  │    ███    │                 │
                                     │ │MaleCit.│ │ Override│           │                 │
                                     │ └────────┘ │         │           │                 │
                                     │ [+New][⟳]  │         │           │                 │
                                     └────────────┴─────────┴───────────┴─────────────────┘
```

---

## 2. Decisions (recorded — do not re-ask). ⚠ marks an interpretation the owner has not confirmed.

- **A80-D1 — One shared selection object, owned by the window, written by every tab.** New
  instance class `ActiveAssetSelection` (§4.1) holds the clip set and the rig and raises one event
  per property. Every panel receives it through `Bind(ActiveAssetSelection)`, reads its current
  values, subscribes to its events, and **writes it directly** when the user picks something. This
  amends A75's "the panel reports, the window acts" rule: the selection *is* the report target, and
  the window is one subscriber among five rather than the hub. Rationale: with four tabs able to
  change either value, event-per-panel → window → push-to-four-panels is exactly the fan-out that
  was already going stale (`RefreshOpenPaneSource` exists only to patch it).
- **A80-D2 — Not a static, not persisted beyond what the window already persists.** The class is an
  ordinary `public sealed class` (so `Conformance_G`'s static-suffix vocabulary does not apply — a
  static `…Selection` would fail the gate). The window's existing `[SerializeField]`
  `sessionClipSet` / `sessionRig` and `ClipEditorDocking.CarriedState` keep carrying the values
  across domain reloads and re-docks; `RestoreView` writes them into the selection. No `EditorPrefs`.
- **A80-D3 — A catalog click is the pick.** On the Clip Sets and Rigs tabs, selecting a row writes
  the shared selection immediately. This reverses A76-D3 ("selecting a rig does *not* change the
  window's Rig field") on the owner's explicit instruction ("I pick a rig from the Rigs Tab, when I
  open Clip Editor, those are already set"). `RigsPanel.hasUserSelectedThisSession` is deleted —
  the catalog now mirrors the selection in both directions, so there is nothing to protect.
  Consequence the owner should know: pressing **New** on either tab creates an empty asset, selects
  it, and therefore makes it the Clip Editor's set or rig too (the hierarchy or clip list empties
  until the new asset has content). That is the model he described; it is noted in the T13 message.
- **A80-D4 — "Open in Clip Editor" / "Use in Clip Editor" stay, as tab jumps only.** They no longer
  carry a value (the selection already holds it). Both keep their names, icons and element names
  (`clip-set-open-button`, `rig-use-in-editor-button`); the window's two handlers shrink to
  `SetActiveTab(ClipEditorTab.ClipEditor)`.
- **A80-D5 — In the Clip Editor, the fields sit directly under each pane's header, full width,
  unlabeled.** `clip-set-field` moves into `clip-list-pane` between the `toolkit-pane-header` and
  `clip-list`; `skinned-source-field` moves into `hierarchy-pane` between its header and
  `hierarchy-empty-label`. The pane titles ("Clips", "Rig Hierarchy") are the labels; the field's
  own empty text (`None (Clip Set Asset)`) and its tooltip say the type. **Element names do not
  change** — `ClipEditorLayoutTests.RequiredElementNames` and every `Q<>` in the window keep
  working; only the test's ordering comment goes stale, and T11 fixes it.
- **A80-D6 — The VAT Bake fields become live, and the hint line is deleted.** ⚠ Interpretation of
  "the warning will just be removed and they will no longer be ghost place holders": the two
  `ObjectField`s stay where they are under **Source**, are always enabled, write the shared selection
  on change, and follow it. `sourceBoundHint` and the `SetEnabled(false)` calls go. The standalone
  `VatBakeWindow` constructs its own `ActiveAssetSelection` so the panel has exactly one code path.
- **A80-D7 — Actor Profiles becomes four columns over three nested `TwoPaneSplitView`s.** Outer
  `(0, 260f, Horizontal)` = [profiles | middle]; middle `(0, SideColumnWidth, Horizontal)` =
  [layers | right]; right is the existing `(1, SideColumnWidth, Horizontal)` = [viewport |
  inspector]. Floors: profiles 200, layers 220 (existing), viewport 200, inspector 260, right 460,
  middle 680 (existing `body` value), outer **880** — the cover-pane collapse trap (vault, "A nested
  `TwoPaneSplitView` inside a cover pane needs its own `minWidth`") applies to every one of them.
  All dividers drag; none persist (same as A75/A76).
- **A80-D8 — The profiles column is a mirror of `RigCatalogColumn`, not an extraction.** Same
  reasoning as A76-D2: the two catalogs have passed the owner's eye with live-verified constants;
  a third copy (`ActorProfileCatalogColumn`) costs one file and risks nothing. Extracting all three
  is a follow-up (§6). Rows: title = profile name; info = `"<n> layers · <rig name>"` or
  `"<n> layers · no rig"`; tooltip = the asset's folder. Same `fixedItemHeight = 64f`.
- **A80-D9 — The profiles catalog gets New, Refresh, search, and a context menu with Rename and
  Delete — A77 parity.** New writes an empty profile (with both bookends) into a remembered
  `EditorPrefs` folder (`DotsAnimationToolkit.Profiles.SaveFolder`) and selects it. Delete moves to
  the OS trash after a confirm, and clears the selection *before* rescanning (A77's rig-delete rule).
  Rename goes through `InlineRenameEditing.Begin` on the row title. No "Name" text field in another
  column — the profile has no editor column of its own; the row is the only rename surface.
- **A80-D10 — Picking a profile still sets the shared rig, never the shared clip set.** A71's rule
  stands: `ActorEditorPanel.Profile`'s setter calls `selection.SetRig(profile.rig)` when both are
  non-null. The window's `OnActorEditorProfileChanged` handler is deleted; the panel's
  `ProfileChanged` event stays for `FocusWithActorEditorTab` and the fixture.
- **A80-D11 — ⚠ The shared Clip Set *and* Rig fields sit at the top of the profiles column.** The
  owner's last clause is "in the first column at the top it will have clip" — read as "the clip set
  (and, by the same rule, the rig), the same two fields every other tab now shows". Two unlabeled
  `ObjectField`s (`actor-editor-clip-set-field`, `actor-editor-rig-field`) above the **Profiles**
  header, bound both ways to the selection exactly like the VAT Bake pair. The Actor Editor does
  not *use* the shared clip set (a profile names its own `clipSets`); the field is there so the
  owner's "swapped in the other tabs as well" holds everywhere. If he meant something else, the
  field is one `Add` call to move or delete — T13 asks.
- **A80-D12 — The header row with the old Profile `ObjectField` is deleted; the validation badge
  moves into the Preview column's header actions.** `actor-editor-profile-field` no longer exists;
  `ActorEditorPanelTests.Panel_ExposesThreeNamedColumnsAndTheProfileField` becomes
  `Panel_ExposesFourNamedColumns`. `validationBadge.AttachMessagePanel(viewportColumn)` is unchanged.
- **A80-D13 — The window keeps `activeRig` and `clipSet` as fields, mirrored from the selection.**
  9,000 lines read them; rewriting every read to `selection.Rig` is churn with no behaviour. The two
  subscription handlers assign the fields first, then do what `OnSkinnedSourceChanged` /
  `OnClipSetChanged` do today. `LoadedPrefab` reads `activeRig`, not the field's `value`.
- **A80-D14 — Every string that tells the user to "assign … in the toolbar" is rewritten in one
  orchestrator sweep (T10), by grep, not by workers.** The list is in §4.7. `ClipPreviewController.cs:396`
  ("open the error list in the top bar") stays — the badge is still in the top bar.
- **A80-D15 — Tasks build against each other's spec'd public surfaces and are gated once per wave,
  not once per task.** Wave 1 is fifteen disjoint-file workers gated together; `SetSource` on the
  four panels stays until the orchestrator deletes it (T9) after the window has switched to `Bind`
  (T6b), so every wave compiles on its own.
- **A80-D16 — No new UXML beyond moving two existing elements; the tab strip loses its 16px left
  margin.** `.clip-editor__tab-strip { margin-left: 16px }` existed to separate the strip from the
  Rig field; with nothing to its left it becomes `0`. `.clip-editor__toolbar-label` and
  `.clip-editor__object-field` **stay** — `CutsceneEditorPanel.cs:394, :402` put the cutscene
  picker in its own bar with them (grep 2026-09-09); only their comments are reworded. A new
  `.clip-editor__pane-field` (`margin: 2px 6px 4px 6px; flex-shrink: 0`) styles the two moved
  fields and the two Actor Editor ones. The stale USS comments at `:430-442` are rewritten with
  the rule.

---

## 3. Read first (the executor and every subagent — only what your task names)

1. Repo root `CLAUDE.md`; `Docs/AnimationToolkit/HANDOFF.md` §2, §3, §5 (skip §4 history);
   `Assets/_Vault/Memories/Code/RULES.md`.
2. `Assets/_Vault/Memories/Code/AnimationToolkit.md` sections **"Never rebuild a pane from a
   value-changed callback"**, **"Clip Sets tab (A75)"**, **"Both catalog tabs create, rename and
   delete in place (A77)"**, **"Rigs tab (A76)"** (the `TwoPaneSplitView` floor paragraph and the
   `Undo.undoRedoPerformed` paragraph), **"Actor Editor: three flex columns became two nested
   TwoPaneSplitViews"**, **"A cover pane that owns a PreviewRenderUtility must be disposed by the
   host"**.
3. `Packages/com.dotsanimationtoolkit/Editor/ClipEditor/ClipEditorWindow.cs` — the hub. `:126`,
   `:312`, `:327-353` (the two fields, `clipSet`, `activeRig`, `RigOfOpenWindow`); `:239-270` (pane
   fields, `activeTab`, `tabToggles`); `:502-513` (`FocusWithActorEditorTab`); `:620-633`
   (`AdoptCarriedState`); `:640-730` (session fields, `RememberSessionState`, `RestoreView`);
   `:767-815` (`OnDisable` disposal order); `:900-931` (tail of `CreateGUI`); `:935-943` and
   `:1073-1083` (the two field bindings in `BindToolbar`); `:1098-1130` (`BindClipList`);
   `:1164-1190` (`RefreshClipList`, `RefreshClipActionButtons`); `:1259-1311` (`BindHierarchy`,
   `RefreshPrefabActionState`, `LoadedPrefab`); `:1705-1800` (the four `Show…Tab` methods);
   `:1801-1890` (`RefreshOpenPaneSource` and the four handlers); `:3516-3545`
   (`OnSkinnedSourceChanged`); `:3618-3633` (`ResolveHierarchyEmptyMessage`); `:4467-4495`
   (`OnClipSetChanged`).
4. `Editor/ClipEditor/ClipEditorWindow.uxml` in full (145 lines) and `ClipEditorWindow.uss:420-470`
   (toolbar, labels, tab strip) and `:526-540` (`__object-field`).
5. `Editor/ClipEditor/Authoring/RigsPanel.cs:50-160` (`hasUserSelectedThisSession`, `SelectedRig`,
   events, constructor, `SetSource`, `SelectRig`), `:179-200` (`RescanProject`), `:220-250`
   (targets header with the Use button), `:630-640` (`OnUseInEditorClicked`), `:687-730`
   (rename/delete).
6. `Editor/ClipEditor/Authoring/ClipSetsPanel.cs:14-60` (fields, events, constructor), `:270-285`
   (`OnClipSetsListSelectionChanged`), `:290-320` (editor header with the Open button), `:400-460`
   (`OnOpenInEditorClicked`, `SetSource`, `RescanProject`), `:563-600` (`SelectSet`, create).
7. `Editor/ClipEditor/Authoring/RigCatalogColumn.cs:1-160` and `:160-315` — the file
   `ActorProfileCatalogColumn` mirrors, comments and all. `Authoring/RigSaveLocation.cs` in full
   (66 lines) — the file `ActorProfileSaveLocation` mirrors. `Authoring/InlineRenameEditing.cs:9-20`.
8. `Editor/ClipUtilities/RigAssetUtility.cs:14-50` (`CreateRig`), `:185-230` (`RenameRig`,
   `DeleteRig`) — the shapes `ActorProfileAssetUtility` copies. `Authoring/Assets/ActorProfileAsset.cs:17-100`
   (fields, `EnsureStableIds`, `EnsureBookends`, `MarkStableIdPersisted`).
9. `Editor/VatBaking/VatBakePanel.cs:17-44` (fields), `:45-100` (the Source block with
   `sourceBoundHint`), `:184-201` (`SetSource`); `Editor/VatBaking/VatBakeWindow.cs` in full (84 lines).
10. `Editor/ClipEditor/ActorEditor/ActorEditorPanel.cs:18-66` (fields), `:142-200` (`Profile`,
    `SetSource`, `SetTicking`), `:270-290` (`BuildHeaderRow`), `:330-500` (`BuildBody`), `:655-665`
    (the "No rig in the top bar" status).
11. Tests to mirror or edit: `Tests/EditMode/ClipSetsPanelTests.cs:1-60`, `RigsPanelTests.cs:1-60,
    77-100`, `ActorEditorPanelTests.cs:1-64`, `ClipEditorLayoutTests.cs:25-45, 240-260`.

---

## 4. Design

### 4.1 The shared selection — new `Editor/ClipEditor/Shared/ActiveAssetSelection.cs`

```csharp
/// <summary>The clip set and rig one editor window is working on, shared by every tab; each setter raises its event only when the value actually changes.</summary>
public sealed class ActiveAssetSelection
{
    public ClipSetAsset ClipSet { get; private set; }
    public RigAsset Rig { get; private set; }

    public event Action<ClipSetAsset> ClipSetChanged;
    public event Action<RigAsset> RigChanged;

    public void SetClipSet(ClipSetAsset clipSet);   // no-op when ReferenceEquals(clipSet, ClipSet)
    public void SetRig(RigAsset rig);               // same
}
```

The equality guard is the re-entrancy guard: a panel that writes the selection from its own
`ChangeEvent` gets the event back, calls `SetValueWithoutNotify` on its field, and nothing loops.
Use `ReferenceEquals`, not `==`, so a destroyed-but-not-null Unity object still counts as a change
(the `==` overload would report a fake-null asset equal to `null` and swallow the clear).

**Rule for every subscriber:** in the handler, update your control with `SetValueWithoutNotify`
(or the catalog's `SetSelected…`), never through a notifying assignment, and never rebuild a pane
from inside the handler when the handler was entered from your own `ChangeEvent` (vault rule).

### 4.2 Window integration — `Editor/ClipEditor/ClipEditorWindow.cs`

- Field, beside `activeRig` (`:334`): `private readonly ActiveAssetSelection selection = new
  ActiveAssetSelection();`. Subscribe once, in `CreateGUI` before `BindToolbar` (so the first
  `RestoreView` lands on live handlers): `selection.ClipSetChanged += ApplyClipSetSelection;
  selection.RigChanged += ApplyRigSelection;`. Unsubscribe in `OnDisable` (`:767`) beside the other
  four `-=` lines — `rootVisualElement` can outlive a domain reload while this instance does not.
- `BindToolbar` (`:937-943`): the field is still resolved by the same name — it has moved in the
  UXML, not been renamed — and its callback becomes `changeEvent => selection.SetClipSet(changeEvent.newValue as ClipSetAsset)`.
  Same at `:1073-1083` for `skinnedSourceField` → `selection.SetRig(…)`. The rig field's tooltip
  drops "use the Rigs tab to create one" phrasing that referred to the toolbar; new text: *"The
  rig this window animates. Its Source Prefab is what the hierarchy lists and the preview
  instantiates. Shared with every tab — the Rigs tab picks it too."*
- `OnSkinnedSourceChanged(ChangeEvent<Object>)` (`:3516`) becomes
  `ApplyRigSelection(RigAsset rig)`: first line `activeRig = rig;`, second
  `skinnedSourceField?.SetValueWithoutNotify(rig);`, then the existing body **minus** the trailing
  `RefreshOpenPaneSource()` call. Keep the comment block above it; reword its first line to say the
  selection is the source.
- `OnClipSetChanged` (`:4467`) becomes `ApplyClipSetSelection(ClipSetAsset newClipSet)`: `clipSet =
  newClipSet; clipSetField?.SetValueWithoutNotify(newClipSet);` then the existing body minus
  `RefreshOpenPaneSource()`.
- `RestoreView` (`:690-730`): replace the two `field.value = …` writes with `selection.SetRig(restoredRig);
  selection.SetClipSet(restoredClipSet);` — same order (rig first, the comment there says why),
  and if a restored value equals the current one nothing fires, which is correct: the panes already
  show it.
- `LoadedPrefab` (`:1303`): `return activeRig != null ? activeRig.sourcePrefab : null;`.
- `ShowVatBakeTab` / `ShowRigsTab` / `ShowClipSetsTab` / `ShowActorEditorTab` (`:1705-1800`): on
  first construction call `panel.Bind(selection)` right after `new`, **before** `pane.Add(panel)`
  (so the first layout already shows the right asset). On every show, replace `panel.SetSource(…)`
  with: nothing for VAT Bake; `rigsPanel.RescanProject()` / `clipSetsPanel.RescanProject()` (both
  made `public` in wave 2 — they were the rescan half of `SetSource`); `actorEditorPanel.SetSource(previewController)`
  plus `actorEditorPanel.RescanProject()`.
- Delete `RefreshOpenPaneSource` (`:1801-1820`) and its two call sites. Delete
  `OnActorEditorProfileChanged` (`:1829-1838`) and its `+=`. `OnRigUseInEditorRequested` and
  `OnClipSetOpenRequested` become one line each: `SetActiveTab(ClipEditorTab.ClipEditor);` (keep
  the parameter so the panel events do not change shape).
- `FocusWithActorEditorTab` (`:502`) is unchanged — setting `Profile` now writes the shared rig
  from inside the panel (A80-D10).
- `RigOfOpenWindow` (`:338`) unchanged.

### 4.3 The Clip Editor panes — `ClipEditorWindow.uxml`, `ClipEditorWindow.uss`

UXML: delete the two `<ui:Label … class="clip-editor__toolbar-label"/>` and move the two
`<uie:ObjectField>` lines — `clip-set-field` to directly after `clip-list-pane`'s
`</ui:VisualElement>` header close (before `<ui:ListView name="clip-list"`), `skinned-source-field`
to directly after `hierarchy-pane`'s header close (before `hierarchy-empty-label`). Both get
`class="clip-editor__pane-field"`. `hierarchy-empty-label`'s text becomes
`"Pick a rig above to list its transforms."`. The toolbar is then `[tab-strip][validation-badge-slot]`.

USS: `.clip-editor__tab-strip` `margin-left: 0`; leave `.clip-editor__toolbar-label` and
`.clip-editor__object-field` in place (the Cutscene panel's own bar uses both) but reword the
label rule's comment so it no longer describes the Clip Set/Rig pair; add

```css
/* The clip set and rig pickers, one per pane, under the pane's own header. Full width because the
   pane title is the label; the field's own empty text says the type. */
.clip-editor__pane-field {
    margin: 2px 6px 4px 6px;
    flex-shrink: 0;
}
```

Rewrite the comment at `:430-442` to say the tab strip now starts the bar and the fields live in
their panes. No rule is deleted: `CutsceneEditorPanel.cs:394, :402` still apply
`clip-editor__toolbar-label` / `clip-editor__object-field` to the cutscene picker (grep 2026-09-09).

### 4.4 The two catalog tabs — `RigsPanel.cs`, `ClipSetsPanel.cs`

Both gain the same three things; the rest of each file is untouched.

```csharp
private ActiveAssetSelection selection;

public void Bind(ActiveAssetSelection sharedSelection)
{
    // Re-bindable: unsubscribe the old one first, or a re-dock double-subscribes.
    selection = sharedSelection;
    selection.RigChanged += OnSharedRigChanged;          // ClipSetsPanel: ClipSetChanged
    OnSharedRigChanged(selection.Rig);                   // adopt the current value now
}
public void RescanProject();   // was private; the window calls it on every show
```

- `RigsPanel.SelectRig(rig)` (`:140`) adds, as its **last** line, `selection?.SetRig(rig);`.
  `OnSharedRigChanged(RigAsset rig)`: if `rig == SelectedRig` return; else run the body of
  `SelectRig` **without** the trailing `selection.SetRig` (split the body into a private
  `ShowRig(RigAsset rig)` both call). Delete `hasUserSelectedThisSession` and `SetSource`'s
  selection logic; `SetSource` stays for wave 2 as `RescanProject()` only, and is deleted in T9.
  `OnUseInEditorClicked` (`:630`) unchanged — the event still fires, the window just switches tabs.
  `Dispose` (`:110`) unsubscribes from the selection beside the `Undo` unsubscribe.
- `ClipSetsPanel`: identical shape on `SelectSet` (`:563`) / `OnSharedClipSetChanged` /
  `ShowSet`; `SetSource` keeps only its folder-fallback lines plus `RescanProject()`; the
  `if (openClipSet != null && SelectedSet == null)` block is deleted. `Dispose` (`:690`) unsubscribes.
- Both: after **New** (`CreateAndSelectNewClipSet` / `CreateAndSelectNewRig`) and after a rename,
  the existing `Select…` call now also writes the selection — no extra code. After **Delete**, the
  existing "clear selection before rescan" line becomes `ShowRig(null); selection?.SetRig(null);`
  (same for sets).

### 4.5 VAT Bake — `VatBakePanel.cs`, `VatBakeWindow.cs`

- Delete `sourceBoundHint` (field, construction at `:86-92`, and the `display = Flex` line).
- `SetSource(ClipSetAsset, RigAsset)` (`:184`) becomes `Bind(ActiveAssetSelection sharedSelection)`:
  store, subscribe `ClipSetChanged` and `RigChanged`, then call both handlers with the current
  values. Each handler: `field.SetValueWithoutNotify(value)`; the rig handler then
  `RefreshResolvedSources()`, the clip-set handler `RefreshPreview(); RefreshResolvedSources();`
  (exactly what the two field callbacks do today at `:65-69` and `:82`).
- The two field callbacks (`:65-69`, `:82`) become `selection.SetClipSet(changeEvent.newValue as
  ClipSetAsset)` / `selection.SetRig(changeEvent.newValue as RigAsset)`; the refresh work moves
  into the handlers so a pick from any tab refreshes the bake preview the same way. Never
  `SetEnabled(false)` on either field again.
- `Dispose` (`:40`) unsubscribes.
- `VatBakeWindow.CreateGUI` (`:31-48`): `private readonly ActiveAssetSelection standaloneSelection =
  new ActiveAssetSelection();` and `panel.Bind(standaloneSelection);` after `new VatBakePanel()`.
  Three lines; the orchestrator makes this edit in T7, not a worker.

### 4.6 Actor Profiles — `ActorEditorPanel.cs` and three new files

**New `Editor/ClipEditor/ActorEditor/ActorProfileSaveLocation.cs`** — `RigSaveLocation` with the
type renamed, `PrefsKey = "DotsAnimationToolkit.Profiles.SaveFolder"`, `DefaultAssetName =
"NewActorProfile"`. Same five members, same `IsValidFolder` guard in `Recall`.

**New `Editor/ClipUtilities/ActorProfileAssetUtility.cs`** (`Utility` suffix is legal only in this
folder — `Conformance_G`):

```csharp
/// Writes an empty profile with both bookends at assetPath. Null when the path is empty.
public static ActorProfileAsset CreateProfile(string assetPath)
/// Renames the asset on disk; false when the name is empty, unchanged, or the rename fails.
public static bool RenameProfile(ActorProfileAsset profile, string newName)
/// Moves the asset to the OS trash; false when null or unsaved. Never DeleteAsset.
public static bool TrashProfile(ActorProfileAsset profile)
```

`CreateProfile` order, mirroring `CreateRig` (`RigAssetUtility.cs:21-49`): `CreateInstance`,
`EnsureBookends()`, `EnsureStableIds()`, `name = <file name without extension>`,
`AssetDatabase.CreateAsset`, `SaveAssets`, `MarkStableIdPersisted()`. `RenameProfile` copies
`RenameRig`'s guard body (`:185-212`); `TrashProfile` copies `DeleteRig` (`:214-`).

**New `Editor/ClipEditor/ActorEditor/ActorProfileCatalogColumn.cs`** — `RigCatalogColumn` mirrored
member for member with the type swapped: events `NewRequested`, `RefreshRequested`,
`ProfileSelected`, `ProfileRenameRequested(ActorProfileAsset, string)`,
`ProfileDeleteRequested(ActorProfileAsset)`; `SelectedProfile`; `SetProfiles(IReadOnlyList<ActorProfileAsset>)`,
`SetSelectedProfile`, `ClearSelection`. Element names `profile-catalog-column`, `profiles-search`,
`profiles-list`, `profile-row-box`, `profile-row-title`, `profile-row-info`; header title
"Profiles"; empty text `"No actor profiles in this project yet. Press New."` / `"No profiles match
your search."`. Row info per A80-D8. Copy every layout constant and its comment (`fixedItemHeight =
64f`, the search field's `width = 100%` / `minWidth = 0` / zeroed margins, `Color.clear` slot,
`marginTop/Bottom = 4f`), and the `userData` rule for recycled rows.

**New `Editor/ClipEditor/ActorEditor/ActorEditorProfilesColumn.cs`** — the whole first column as
one element, so the panel hosts it the way it hosts `ActorEditorLayersColumn`, and so it can be
built in parallel with the panel's own layout change.

```csharp
/// <summary>The Actor Editor's first column: the shared clip set and rig fields over a searchable profile catalog with New, Refresh, and row Rename/Delete.</summary>
public sealed class ActorEditorProfilesColumn : VisualElement, IDisposable
{
    public event Action<ActorProfileAsset> ProfileSelected;  // a catalog click, New, a rename (the renamed asset), or Delete (null)

    public ActorProfileAsset SelectedProfile { get; }

    public void Bind(ActiveAssetSelection sharedSelection);   // the two fields follow and write it
    public void SetSelectedProfile(ActorProfileAsset profile); // highlight without raising
    public void RescanProject();                               // FindAssets("t:" + nameof(ActorProfileAsset)), sorted OrdinalIgnoreCase
    public void LoadCatalog(IReadOnlyList<ActorProfileAsset> profiles);  // for fixtures; no AssetDatabase
    public void CreateAndSelectNewProfile();
    public void Dispose();                                     // unsubscribes from the selection
}
```

`name = "profiles-column"`, `minWidth = 200f`, padding like `RigCatalogColumn`. Children, top to
bottom: `clipSetField` (`actor-editor-clip-set-field`, `objectType = typeof(ClipSetAsset)`,
`allowSceneObjects = false`, class `clip-editor__pane-field`, tooltip *"The clip set every tab is
working on. Not what this profile plays — a profile lists its own clip sets."*), `rigField`
(`actor-editor-rig-field`, `typeof(RigAsset)`, tooltip *"The rig every tab is working on. Picking
a profile sets it to the profile's rig."*), then an `ActorProfileCatalogColumn`. Field callbacks
write `selection.SetClipSet` / `SetRig`; the selection's events land in the fields with
`SetValueWithoutNotify`. Catalog wiring: `NewRequested += CreateAndSelectNewProfile`,
`RefreshRequested += RescanProject`, `ProfileSelected += raise`, `ProfileRenameRequested +=
RenameProfileAndRefresh`, `ProfileDeleteRequested += RequestDeleteProfile`.
`CreateAndSelectNewProfile`: `saveLocation.Recall()` → `ActorProfileSaveLocation.ResolveTargetAssetPath(folder,
DefaultAssetName)` → `ActorProfileAssetUtility.CreateProfile` → `RescanProject()` → select and
raise. `RenameProfileAndRefresh` / `RequestDeleteProfile` copy `RigsPanel.cs:687-730` (confirm
text: *"Move \"<name>\" to the trash? Actors and cutscenes that reference it will lose their
profile."*); on delete, raise `ProfileSelected(null)` **before** the rescan (A77's rule).

**`ActorEditorPanel.cs`:**

- Fields: delete `profileField` and `windowRig`; add `private ActiveAssetSelection selection;`
  and `private ActorEditorProfilesColumn profilesColumn;`. Every `windowRig` read becomes
  `selection != null ? selection.Rig : null` (two sites: `:184` the assignment goes away, `:659`
  the status check).
- Constructor: `Add(BuildBody())` only — `BuildHeaderRow` is deleted (A80-D12).
- `Profile` setter (`:143-171`): delete the `profileField` lines; add, after `profile = value;`,
  `profilesColumn?.SetSelectedProfile(profile);` and, after the column binds,
  `if (profile != null && profile.rig != null) { selection?.SetRig(profile.rig); }`.
- `SetSource(ClipPreviewController controller, RigAsset rig)` → `SetSource(ClipPreviewController
  controller)`; the rig comes from the selection. New `Bind(ActiveAssetSelection)` stores it and
  forwards to `profilesColumn.Bind`. New `RescanProject()` and `LoadCatalog(...)` forward to the
  column. On delete the column raises `ProfileSelected(null)` and the handler sets `Profile = null`.
- `BuildBody` (`:330`): `profilesColumn = new ActorEditorProfilesColumn(); profilesColumn.ProfileSelected += picked => Profile = picked;`
  then the three splits of A80-D7: `rightSplit` as today; `middleSplit = new TwoPaneSplitView(0,
  SideColumnWidth, Horizontal)` with `minWidth = 680f` holding `[layersColumn | rightSplit]`;
  `body = new TwoPaneSplitView(0, 260f, Horizontal)` with `minWidth = 880f` holding
  `[profilesColumn | middleSplit]`. Move `validationBadge` construction into `viewportHeader`
  inside a `toolkit-pane-actions` element (name stays `actor-editor-validation-badge`).
- `Dispose` (`:222`): `profilesColumn?.Dispose()`.
- `RenderViewport` (`:659`): the status string becomes `"No rig picked — choose a profile, or pick a
  rig in the column on the left."`.

### 4.7 The string sweep (T10, orchestrator) — every "toolbar"/"top bar" the user can read

| File:line (2026-09-09) | Now | Becomes |
|---|---|---|
| `ClipEditorWindow.cs:1298` | "Assign a rig in the toolbar's Rig field, and give that rig a Source Prefab, to edit it." | "Pick a rig above the hierarchy, and give that rig a Source Prefab, to edit it." |
| `ClipEditorWindow.cs:3625` | "Assign a rig to the toolbar's Rig field." | "Pick a rig above the hierarchy." |
| `ClipEditorWindow.cs:7979` | "Assign a prefab in the toolbar's rig field to pick from its bones instead." | "Pick a rig with a Source Prefab above the hierarchy to pick from its bones instead." |
| `ClipEditorWindow.cs:9189` | "Assign a clip set in the toolbar." | "Pick a clip set above the clip list." |
| `ClipEditorWindow.cs:9227` | "Assign a rig with a Source Prefab in the toolbar to pick from the hierarchy …" | "Pick a rig with a Source Prefab above the hierarchy to pick from it …" |
| `ClipEditorWindow.ComponentStack.cs:1115` | "Assign a rigged prefab in the toolbar to pick the bone this should follow." | "Pick a rig with a Source Prefab above the hierarchy to pick the bone this should follow." |
| `ClipEditorWindow.ComponentStack.cs:54` (comment) | "picked in the toolbar" | "picked in the Rig Hierarchy pane or any tab" |
| `Components/ClipComponentModel.cs:239` | "Assign a RigAsset in the toolbar's Rig field, or build …" | "Pick a RigAsset above the hierarchy, or build …" |
| `Preview/ClipPreviewController.cs:391`, `:936` | "Assign a rig to/in the toolbar's Rig field." | "Pick a rig above the hierarchy." |
| `Authoring/ClipEditorDocking.cs:25` (comment) | "the toolbar field now picks the" | "the Rig Hierarchy pane's field now picks the" |
| `ClipEditorWindow.cs:328-334` (comment on `activeRig`) | "the two toolbar pickers above" | "the shared selection every tab writes" |
| `ClipEditorWindow.cs:3516` (summary on the renamed handler) | "Handles a pick in the toolbar's Rig field" | delete the `<summary>` — one per file, and `ApplyRigSelection` carries its own name |
| `Documentation~/clip-editor.md:18, 46, 132-143` | "the toolbar's **Rig** field" (five places), "**New Rig** beside the field" | "the **Rig** field at the top of the Rig Hierarchy pane"; the New Rig sentence is rewritten to point at the Rigs tab |
| `Documentation~/cutout-characters.md:36`, `rigged-characters.md:134`, `sharing-clips.md:56` | "the toolbar's **Rig** field" | "the **Rig** field at the top of the Rig Hierarchy pane" |

Grep afterwards: `grep -rn "toolbar's Rig\|toolbar's Clip\|in the toolbar\." Editor Documentation~`
must return nothing. The wider `grep -rn "toolbar" Editor Documentation~` still legitimately hits
`ClipPreviewController.cs:396` (the badge), `CutsceneEditorPanel.cs:1973` (the cutscene panel's own
bar), `RagdollPreviewSimulation.cs:86` (a status line), `billboarding.md:136` and
`clip-editor.md:54, 55, 151, 179` (viewport-rail toggles, the prefab-stage overlay, Rig Edit) —
leave every one of those.

---

## 5. Tasks

**The shape: one wide wave, one narrow wave, then the orchestrator.** Every task that needs another
task's *public surface* builds against the code block in §4 and is gated together with it at the end
of the wave, so nothing waits on a compile it does not need. Wave 1 is fifteen workers on fifteen
disjoint file sets; wave 2 is the two tasks that must follow them (each edits a file wave 1 already
touched); the rest is grep-and-sed work that is cheaper in the orchestrator than in a brief. If the
harness caps concurrent agents, fill wave 1 in the order listed — the gate is still one, at the end.

Each brief pastes: the spec path, the task text below, its "Read" line, the §4 block it builds, and
CLAUDE.md's hard rules (no `var`, no single-letter names, explicit types; one `<summary>` per file
on the primary type, three lines max, no `<remarks>`, no spec citations in shipped code). Every brief
ends: "at turn 30 stop editing and write your report; report ≤ 30 lines; never call any
`mcp__UnityMCP__*` tool; do not open any file this brief does not name; if a type this brief tells
you to call does not exist yet, code against the signature in the spec — it lands in the same wave."

### T0 — Baseline (orchestrator)
Gate per HANDOFF §3; record EditMode / PlayMode discovered totals in §7 (A78 closed at 814 / 283).
`git status` first; head is `9faa224b`, the tree was clean on 2026-09-09. Line numbers in §3/§4 were
taken against that commit — if `ClipEditorWindow.cs` has moved, re-grep the member names.

### Wave 1 — fifteen workers, all `[parallel-safe]`, gated once

| Task | Files (≤ 2) | Builds | Depends on (surface only) |
|---|---|---|---|
| T1 | new `Shared/ActiveAssetSelection.cs`, new `Tests/EditMode/ActiveAssetSelectionTests.cs` | §4.1 | — |
| T2 | new `ActorEditor/ActorProfileCatalogColumn.cs` | §4.6 catalog paragraph | — |
| T3a | new `ActorEditor/ActorProfileSaveLocation.cs` | §4.6 first block | — |
| T3b | new `ClipUtilities/ActorProfileAssetUtility.cs` | §4.6 second block | — |
| T4a | `ClipEditorWindow.uxml` | §4.3 UXML half | — |
| T4b | `ClipEditorWindow.uss` (`:420-470`, `:526-540`) | §4.3 USS half | — |
| T5a | `Authoring/RigsPanel.cs`, `Tests/EditMode/RigsPanelTests.cs` | §4.4 | T1 |
| T5b | `Authoring/ClipSetsPanel.cs`, `Tests/EditMode/ClipSetsPanelTests.cs` | §4.4 | T1 |
| T5c | `VatBaking/VatBakePanel.cs`, new `Tests/EditMode/VatBakePanelTests.cs` | §4.5 (panel only) | T1 |
| T5d | new `ActorEditor/ActorEditorProfilesColumn.cs` | §4.6 column block | T1, T2, T3a, T3b |
| T5e | `ActorEditor/ActorEditorPanel.cs`, `Tests/EditMode/ActorEditorPanelTests.cs` | §4.6 panel: layout + hosting | T1, T5d |
| T6a | `ClipEditor/ClipEditorWindow.cs` | §4.2 core (see below) | T1 |
| T10b | `Documentation~/clip-editor.md` | §4.7 doc rows | — |
| T10c | `Documentation~/cutout-characters.md`, `Documentation~/rigged-characters.md` | §4.7 doc rows | — |
| T11 | `Tests/EditMode/ClipEditorLayoutTests.cs` (`:25-45`, `:240-260`) | the layout assertion | T4a |

**T1.** Read §4.1 and `Shared/ITransportTarget.cs` (30 lines) for the header/namespace shape.
- `SetRig_RaisesRigChangedOnce_AndNotAgainForTheSameValue`: `CreateInstance<RigAsset>()`; count
  events; `SetRig(rig)` twice → 1; `SetRig(null)` → 2. (Revert-to-fail: drop the equality guard.)
- `SetClipSet_LeavesTheRigAlone`: set a rig, then a clip set → `RigChanged` count still 1,
  `Rig` unchanged. Destroy both assets in `TearDown`.

**T2.** Read `RigCatalogColumn.cs:1-160` then `:160-315` (two reads), `ActorProfileAsset.cs:17-40`,
`InlineRenameEditing.cs:9-20`. Mirror, do not improve. No fixture — T5e's drives it.

**T3a.** Read `RigSaveLocation.cs` in full. Rename the type, the prefs key, the default name. No fixture.

**T3b.** Read `RigAssetUtility.cs:14-50, 185-230` and `ActorProfileAsset.cs:40-100`. No fixture —
`CreateProfile` needs `AssetDatabase`; T12 proves the bookends on the reloaded asset.

**T4a.** Read `ClipEditorWindow.uxml` in full. Move the two fields, delete the two labels, retext
`hierarchy-empty-label`, add the class. **Rename nothing.**

**T4b.** Read `ClipEditorWindow.uss:420-470, 526-540`. The three edits of §4.3; delete no rule.

**T5a.** Read `RigsPanel.cs:50-160, 630-640, 687-730`, `RigsPanelTests.cs:1-60, 77-100`. Add `Bind`,
`ShowRig`, `OnSharedRigChanged`, public `RescanProject`; delete `hasUserSelectedThisSession`;
**keep `SetSource` compiling** as `RescanProject()` only.
- `SelectRig_WritesTheSharedSelection_AndFollowsIt`: `Bind(selection)`; `SelectRig(rigA)` →
  `selection.Rig == rigA`; `selection.SetRig(rigB)` → `panel.SelectedRig == rigB` and the
  catalog agrees. (Revert-to-fail: drop the `selection.SetRig` line in `SelectRig`.) `panel.Dispose()`
  in `TearDown` — it owns a `PreviewRenderUtility`.

**T5b.** Read `ClipSetsPanel.cs:14-60, 270-285, 400-460, 563-600`, `ClipSetsPanelTests.cs:1-60`.
Same shape as T5a; `SetSource` keeps its folder-fallback lines plus `RescanProject()`.
- `SelectSet_WritesTheSharedSelection_AndFollowsIt`, mirror of T5a's, with `LoadCatalog` for the
  two sets.

**T5c.** Read `VatBakePanel.cs:17-100, 184-201`. Add `Bind`; delete `sourceBoundHint`; fields write
the selection; name them `vat-bake-clip-set-field` / `vat-bake-rig-field`; **keep `SetSource`
compiling** (body: `selection?.SetClipSet(clipSet); selection?.SetRig(rig);`). Do not touch
`VatBakeWindow.cs`.
- `Bind_FollowsTheSharedSelectionBothWays`: `selection.SetRig(rig)` → the rig field shows `rig`
  and is enabled; set the field's `value = null` with notify → `selection.Rig == null`.
  (Revert-to-fail: leave `SetEnabled(false)` in.) `panel.Dispose()` in `TearDown`.

**T5d.** Read the `public` lines of `ActorProfileCatalogColumn.cs`, `ActorProfileSaveLocation.cs`
and `ActorProfileAssetUtility.cs` by grep (they may not exist yet — then code to §4.6's blocks),
`RigsPanel.cs:687-730` (rename/delete to copy), `RigCatalogColumn.cs:32-60` (padding), §4.1's code
block, and §4.6's column block. No fixture — T5e's second test drives `LoadCatalog` +
`SetSelectedProfile` through the panel.

**T5e.** Read `ActorEditorPanel.cs:18-66, 142-200, 270-290, 330-500`, `ActorEditorPanelTests.cs:1-64`,
§4.6's `ActorEditorProfilesColumn` code block and the panel bullets **except** the `Profile`-setter
rig line, the `windowRig` removal and the status string (those are T5f). Deliver: the four-column
layout hosting the new column, `BuildHeaderRow` gone, the badge in the Preview header, the
`profileField` lines out of the `Profile` setter with `profilesColumn?.SetSelectedProfile` in,
`Bind`, `RescanProject`, `LoadCatalog`, `Dispose` forwarding, **and** the one-argument
`SetSource(ClipPreviewController)` overload beside the existing two-argument one (which stays until T9).
- Edit `Panel_ExposesThreeNamedColumnsAndTheProfileField` → `Panel_ExposesFourNamedColumns`:
  `profiles-column`, `layers-column`, `viewport-column`, `inspector-column` exist;
  `actor-editor-profile-field` is absent.
- Edit `AssigningAProfile_RaisesProfileChangedAndUpdatesTheField` → drop the field assertion, keep
  the `ProfileChanged` one, add `panel.LoadCatalog(new[] { profile })` first and assert the
  column's `SelectedProfile == profile` after. (T5f extends it.)

**T6a.** Read `ClipEditorWindow.cs:126, :312, :327-353, :620-633, :690-730, :767-815, :900-943,
:1073-1083, :1303-1311, :3516-3545, :4467-4495` and §4.2's first six bullets (through
`LoadedPrefab`). Deliver: the `selection` field, subscribe in `CreateGUI` before `BindToolbar`,
unsubscribe in `OnDisable`, the two field callbacks writing the selection, `ApplyRigSelection` /
`ApplyClipSetSelection` (renamed from the two `On…Changed` handlers, mirror lines first, the old
`RefreshOpenPaneSource()` calls **kept** for now), `RestoreView` writing the selection, `LoadedPrefab`
reading `activeRig`, the new rig-field tooltip. **Do not touch `:1705-1890`** — that is T6b.

**T10b.** Read `clip-editor.md:10-60, 125-150` and §4.7's doc rows. Rewrite the five "toolbar's
Rig field" sentences and the New Rig paragraph (`:132-143`) to point at the Rig Hierarchy pane's
field and the Rigs tab. Leave `:54, 55, 151, 179`.

**T10c.** Read `cutout-characters.md:30-40`, `rigged-characters.md:130-138`, §4.7. One sentence each.

**T11.** Read `ClipEditorLayoutTests.cs:25-45, 240-260` and §4.3. Fix the ordering comment at
`:35-36`; add `AssetFields_LiveInsideTheirPanes_NotTheToolbar`: on `CloneLayout()`, `clip-set-field`
is a descendant of `clip-list-pane`, `skinned-source-field` of `hierarchy-pane`, and
`clip-editor-toolbar` contains **no** `ObjectField`. (Revert-to-fail: move either field back.)

**Wave-1 gate (orchestrator):** one compile gate, then by `test_names`: T1's two, T5a's, T5b's,
T5c's, T5e's two, and the whole `ClipEditorLayoutTests` fixture. Commit `A80 wave 1: T1–T6a, T10b,
T10c, T11`.

### Wave 2 — two workers, `[parallel-safe]` with each other, gated once

**T5f.** Files: `ActorEditor/ActorEditorPanel.cs`, `Tests/EditMode/ActorEditorPanelTests.cs`. Read
the file as T5e left it (`grep -n "windowRig\|Profile\b\|RenderViewport"` and 30 lines around each
hit), §4.6's remaining panel bullets. Deliver: `windowRig` gone (two reads → the selection), the
`Profile` setter's `selection?.SetRig(profile.rig)` line, the new status string.
- Extend the second test into `AssigningAProfile_RaisesProfileChanged_AndSetsTheSharedRig`: after
  `Bind(selection)`, a profile with a rig → `selection.Rig == profile.rig`. (Revert-to-fail: drop
  the `SetRig` line.)

**T6b.** Files: `ClipEditor/ClipEditorWindow.cs`. Read `:1705-1890` as T6a left it (re-grep
`ShowVatBakeTab` — T6a's edits above it shift nothing there, but check) and §4.2's last four
bullets. Deliver: `panel.Bind(selection)` after each `new …Panel()` and before the `Add`; the show
paths call `RescanProject()` / `actorEditorPanel.SetSource(previewController)` instead of
`SetSource(...)`; delete `RefreshOpenPaneSource` and its two calls (in the two `Apply…` handlers);
delete `OnActorEditorProfileChanged` and its `+=`; the two `…Requested` handlers become
`SetActiveTab(ClipEditorTab.ClipEditor);`.

**Wave-2 gate (orchestrator):** compile gate, `ActorEditorPanelTests` by `group_names`. Commit
`A80 wave 2: T5f, T6b`.

### Wave 3 — orchestrator only

- **T7** — `VatBakeWindow.cs`: §4.5's last bullet, three lines.
- **T9** — delete the scaffolding by grep: `RigsPanel.SetSource`, `ClipSetsPanel.SetSource`,
  `VatBakePanel.SetSource`, `ActorEditorPanel.SetSource(ClipPreviewController, RigAsset)`.
  `grep -rn "\.SetSource(" Editor` must show only `actorEditorPanel.SetSource(previewController)`
  and the preview controller's own `SetSkinnedSource` / `SetClipSet` / `SetRig` family.
- **T10a** — the code rows of §4.7 by `sed`/Edit, plus `sharing-clips.md:56`; run the closing grep.
- Gate; commit `A80 wave 3: T7, T9, T10a`.

### Wave 4 — the drive, with one worker beside it

**T12a** `[parallel-safe with the orchestrator's drive]` — files: `CHANGELOG.md` (top 25 lines),
`package.json` (`version` only). Read `CHANGELOG.md:1-25` for the section shape and §1 + §2 of this
spec. Write the `## [<next>] — A80 — one selection, every tab` section: Changed (top bar, Clip
Editor panes, catalog click = pick, VAT Bake fields live, Actor Profiles four columns), Added
(`ActiveAssetSelection`, the profiles catalog with New/Rename/Delete), Removed (the two toolbar
fields, the VAT Bake hint, the Actor Editor Profile field). Bump `package.json`.

**T12 (orchestrator).**
1. Full suites (HANDOFF §3 steps 3–4). EditMode ≥ T0 + 6 (T1 ×2, T5a, T5b, T5c, T11; T5e/T5f
   edit two existing tests); PlayMode unchanged.
2. Drive over `mcp__UnityMCP__execute_code` (CodeDom C# 6, no `using`, fully-qualified names,
   `resolvedStyle` in a second call). Scratch assets under `Assets/A80Scratch/` via
   `AssetDatabase.CopyAsset` of `MaleCitizen`'s set, rig and profile — never the originals.
   - **Rigs** → `SelectRig(scratchRig)`; **Clip Editor**: hierarchy has rows,
     `skinned-source-field.value == scratchRig`. **Clip Sets** → `SelectSet(scratchSet)`; back:
     `clip-list.itemsSource.Count > 0`, `clip-set-field.value == scratchSet`. **VAT Bake**: both
     fields enabled and showing the scratch assets; rig field `value = null` with notify; Clip
     Editor's hierarchy empties, empty label reads "Pick a rig above the hierarchy." Restore.
   - **Actor Profiles**: `RescanProject()`; second call: four named columns, non-zero
     `layout.width`, roughly `260 : 220 : rest : 260`; `profiles-list.itemsSource.Count >= 1`.
     `CreateAndSelectNewProfile()` through the column; `AssetDatabase.Refresh()`; **reload the new
     asset from its path**: `layers.Count == 2`, `Base` first, `Override` last. Select the scratch
     profile → `selection.Rig == scratchProfile.rig`, and the Clip Editor's rig field agrees. Remove
     the new profile with `ActorProfileAssetUtility.TrashProfile` (the confirm dialog cannot be
     answered from a script), rescan, assert it is gone and `Profile == null`.
   - Session restore proxy: call the private `RestoreView` by reflection with the scratch set and
     rig; both fields and both catalog tabs agree. Note the result either way.
3. Capture `Library/A80Captures/clip-editor.png`, `vat-bake.png`, `actor-profiles.png`
   (`pixelsPerPoint`, `isFocused` first, positive-x monitor) and **look at them**: no object fields
   in the top bar, the strip flush left, the two pane fields full-width under their headers, four
   legible Actor Profile columns. If a capture is stale, say so and record `resolvedStyle` numbers.
4. Delete `Assets/A80Scratch` and its `.meta`; `git status` must show only your files.
5. HANDOFF §4 paragraph; `Documentation~/actor-profiles.md` (grep "Profile" for the header-field
   sentence, rewrite around the catalog column); the vault note `AnimationToolkit.md`: one section
   "Shared asset selection (A80)" carrying the three traps — `ReferenceEquals` in the setter guard,
   `SetValueWithoutNotify` in every subscriber, and the panels-write-the-selection rule that
   supersedes A75's "panel reports, window acts" for these two values. Traps only, no inventory.
6. Commit `A80 wave 4: T12, T12a`; push.

### T13 — ⏸ owner checkpoint
End the session with this message, verbatim in spirit:

> The top bar is tabs and the validation badge, nothing else. Open **Clip Sets**, click a set;
> open **Rigs**, click a rig; open **Clip Editor** — the set is above the clip list and the rig is
> above the hierarchy, both already filled. Change either one there and flip to **VAT Bake**: both
> fields follow, and they are live now, no hint line. **Actor Profiles** is four columns — the two
> shared fields, then a Profiles catalog with search, New, Refresh and a right-click Rename/Delete;
> then Layers, Preview, Actor Inspector; every divider drags. Picking a profile sets the shared rig.
> Judge three things: (1) I put *both* shared fields at the top of the Profiles column — you said
> "clip"; if you meant only the clip set, or neither, say which. (2) On VAT Bake I kept the two
> fields and made them editable rather than removing them; if you wanted them gone entirely, say so.
> (3) Pressing New on Clip Sets or Rigs now makes the new empty asset the active one everywhere,
> which empties the Clip Editor until it has content — is that what you want, or should New create
> without selecting?

---

## 6. Deliberately out of scope (follow-ups, not omissions)

- Extracting `RigCatalogColumn` / `ActorProfileCatalogColumn` / `ClipSetsPanel`'s catalog into one
  generic control — three near-identical files exist after this; do it once all three have passed
  the owner's eye.
- Persisting the four divider positions (A76's open question, still open).
- The window's `minSize` is 820 wide; the Actor Profiles floor is 880. A window narrower than that
  overflows the cover pane horizontally rather than collapsing. Raise `minSize` only if the owner
  hits it.
- A profile "Name" text field in an editor column (the Rigs/Clip Sets tabs have one); the profile
  tab has no editor column, so rename lives on the row only.
- Sharing the *profile* across tabs (Cutscene Director casts by profile) — not asked for.

---

## 7. Build log

- **T0 (2026-09-09):** head `df0ffeb4`, tree clean. EditMode **814/814** discovered, one failure =
  the standing `Conformance_A` asmdef drift (`Unity.RenderPipelines.Universal.Runtime` extra).
  PlayMode **283/283**. Window anchors re-verified against head: every §3/§4 line number holds.
- **Wave 1 (2026-09-09):** fifteen workers in parallel, peak 79k tokens (T5e), none capped. One
  compile error at the gate (`VatBakePanelTests` lacked `using UnityEngine.UIElements` for `Q`),
  fixed by the orchestrator. `VatBakePanelTests.Bind_FollowsTheSharedSelectionBothWays` could not
  assert the field→selection direction: a panel-less element never dispatches its `ChangeEvent`, so
  the test became `Bind_FollowsTheSharedSelection_AndLeavesTheFieldEditable` (selection→field plus
  the enabled assertion; revert-to-fail is still the `SetEnabled(false)` line). Six touched fixtures
  **25/25**. Commit `df3334b4`.
- **Wave 2:** T5f + T6b in parallel (61k / 63k tokens). Gate 14/14. Commit `007262e9`.
- **Wave 3 (orchestrator):** T7, T9 (ClipSetsPanel's folder fallback moved onto `ShowSet` as
  `RememberFallbackFolderOf`), T10a. Six fixtures 25/25. Commit `9d31a0de`.
- **Wave 4:** T12a worker wrote the 0.27.0 changelog and bump (51k). Full EditMode **820/820**
  (only `Conformance_A`), PlayMode **283/283**. Drive: every §5 T12.2 assertion held. Layout after
  a hide/show cycle: profiles 200 · layers 220 · viewport rest · inspector 260 in a 969px window —
  the profiles column settles at its floor, the A75/A76 settle-to-floor behaviour. Captures:
  `Library/A80Captures/{clip-editor,vat-bake,actor-profiles}.png`, all taken with the Editor
  focused and looked at. Scratch folder deleted. Commits `a7b149d7` + the docs follow-up.
