# A109 — Consistency pass 3: the owner's circled screenshots

> **Status:** planned 2026-09-29, not built. Worktree `a109-consistency` (branch `spec/a109-consistency`, from `main`
> at `0dc25367`, package `0.60.0` → ships as `0.61.0`).
> **Source:** the owner's 14 annotated captures in `Assets/_Vault/Tasks/AnimationPackage/Issue.png`, `issue2.png` …
> `issue14.png` (in the stage checkout, untracked there). Red circles mark the problem areas. Read the capture before
> the task that cites it.
> **Binding:** `Docs/AnimationToolkit/EditorStyleGuide.md` (R01–R22, SG-D1–SG-D8). This spec overrides the guide only
> where §1 says so.

Owner's words, 2026-09-29: "In general make things the same and consistent. It really is a little all over throughout
the package for spacing and not." The problems are spacing, inconsistent button colour/design, and missing info.

## 1. Decisions (recorded by the orchestrator under the delegated-calls rule — owner may overturn at the checkpoint)

| Id | Decision | Why |
|---|---|---|
| D1 | **The primary button is the same height as its row (24px).** It keeps the light fill; it loses the extra 4px. `--toolkit-height-primary` becomes 24px and the `toolkit-action-run--primary` 28px rule goes. Overrides the guide's "28 primary" height token. | Owner circled every primary next to smaller buttons: Texture Packer Bake, Clip Sets / Rigs "Open/Use in Clip Editor", Materials "Create and assign", VAT Bake, Health "Scan project", Cutscenes "New". "Bake is a different size than the other buttons." |
| D2 | **Pane-header actions are boxed icon squares.** 24×24, 1px outline, one 16px one-tone glyph, tooltip names the action. Destructive squares use the same box with a red glyph. Applies to every catalog/pane header (New, Refresh, Hide/Show hidden, Save, +, trash, Edit, link) on every tab. Word buttons stay only in asset bars and detail headers. | "Make the buttons on the left of the clip editor look the same, I like them visually being boxed out, should be same size". Also fixes the New-boxed/Refresh-ghost pairs circled on Rigs, Materials, Events and Actor Profiles, and the cramped Recipes header, where three word buttons do not fit next to the segmented control. |
| D3 | **Header → content gap is 8px** under every detail header and asset bar. No control touches the row above it. | Clip Sets "Open in Clip Editor … has no spacing below it"; Rigs "Use in Clip Editor" crowds the Name field. |
| D4 | **A disabled control always says why**, in a tooltip, plus a hint line when the whole pane's main action is disabled. | "I can't figure out how to make bake and save not be greyed out." |
| D5 | **Badge + button pairs share one row:** 24px, centre-aligned, 4px gap, and the badge is a real `MakeBadge`. | Clip Sets Unbaked/Rebake and VAT Bake Issue/Unbaked/Bake are misaligned. |
| D6 | **Sound events: one `Sound` key, and the int param picks the sound.** The package already carries `intParam` on every `AnimEventOutput`. Only the game's lookup (`AnimSoundEventMappingBlob`: event key → `SoundType`) forces one key per sound. The Events tab's "Int value names" field names the sounds. | Owner asked whether a separate event per sound type is intended, and noted that 64 may not be enough. It is not intended. Also, 64 is not a key cap: it is only the number of *maskable* keys, the ones that can hold a window open. Keys above that are legal and pulse-only, which is all a sound needs. |

## 2. Findings per tab (capture → problem → file)

Paths are relative to `Packages/com.dotsanimationtoolkit/Editor/`.

| Id | Tab (capture) | Problem | Owner file(s) |
|---|---|---|---|
| TP1 | Texture Packer (`Issue`, `issue2`) | Bake is 28px, Bake As / Save as recipe are 24px, and Clear is a ghost. The action bar should be one run. | `TexturePacker/TexturePackerPanel.cs` |
| TP2 | Texture Packer (`issue2`) | Images header: the Hide icon is on its own, and Refresh is an unboxed icon + word. | `TexturePacker/ImageCatalogColumn.cs` |
| TP3 | Texture Packer (`Issue`) | Recipes header crams New / Save / Refresh beside the segmented control. The sidebar "Save" should say "Save as recipe" (RC1 carry-over, `RecipeCatalogColumn.cs:25`). | `TexturePacker/RecipeCatalogColumn.cs`, `TexturePacker/TexturePackerSidebar.cs` |
| FB1 | Flipbooks (`issue3`) | Same Hide/Refresh header as TP2 (shared column). | covered by TP2 |
| FB2 | Flipbooks (`issue3`) | Bake and Save are greyed out with no reason. **Cause (verified):** `MaleHairTextureArray` is an *imported* Texture2DArray. `FlipbooksPanel.RefreshModeControls` disables Bake whenever `IsImportedArray` is true, and enables Save only once a frame's name differs from its layer index. The UI never says so. **Owner, 2026-09-29: "allow me to set up the flipbooks in that section."** Two parts. (a) On an imported array, the import-settings fields stop being greyed out: they write straight to the array's `TextureImporter` and reimport. (b) A secondary **"Make editable"** button: each layer is blitted out (`Graphics.Blit(array, rt, layer, 0)` → `ReadPixels` → PNG) into a `<Array>_Frames/` folder beside the array. The flipbook then gets those frames as sources and outputs to `<Array>_Flipbook.asset`, so Bake, Save, reorder and Remove all work. The imported source stays untouched, and the hint says materials still point at it until they are re-pointed. Disabled reasons (D4) cover whatever is still off. | `Flipbooks/FlipbooksPanel.cs` (a, the button, hints); `ClipUtilities/FlipbookAssetUtility.cs` (b, `ExtractArrayLayersToEditableFlipbook(Texture2DArray array)`) |
| FB3 | Flipbooks (`issue3`) | Frames "Remove" is a red text ghost, a different style from every other destructive control. | `Flipbooks/FlipbookFramesColumn.cs` |
| FB4 | Flipbooks (`issue3`) | The Import settings card runs into the Frames/Zoom row with no gap. "Match import settings of" is clipped. The Zoom row has no inset. | `Flipbooks/FlipbooksPanel.cs` (C#), USS worker |
| CS1 | Clip Sets (`issue4`) | "Open in Clip Editor" is oversized, with no gap below it (D1, D3). The folder icon floats between the Name and Folder rows. | `ClipEditor/Authoring/ClipSetsPanel.cs` |
| CS2 | Clip Sets (`issue4`) | The Unbaked badge and the Rebake button are on different baselines, and Rebake is an unstyled Unity button (D5). | `ClipEditor/Authoring/ClipSetsPanel.cs`, `VatBaking/VatFreshnessBadgeElement.cs` |
| RG1 | Rigs (`issue5`) | New boxed, Refresh unboxed (D2). | `ClipEditor/Shared/ToolkitCatalogColumn.cs` (fixes Rigs, Materials, Clip Sets, Actor Profiles) |
| RG2 | Rigs (`issue5`) | "Use in Clip Editor" is oversized and crowds the Name field (D1, D3). | `ClipEditor/Authoring/RigsPanel.cs` |
| MT1 | Materials (`issue6`) | The Target dropdown is a tiny, unstyled popup next to an oversized primary. They should be one 24px control row with the label in muted meta. | `Materials/MaterialsPanel.cs` |
| MT2 | Materials (`issue6`) | The Contract card is empty (missing info). Find what should fill it. If a material legitimately has nothing, show a designed empty line (R13): what a contract is and why this one is empty. Each card gets one hint line so the tab reads clearly. | `Materials/MaterialInspectorColumn.cs` |
| EV1 | Events (`issue7`) | The Keys header crams the title, the "4/64" badge, a bare "0" badge, New and Refresh. Badges need tooltips ("4 of 64 maskable keys used"). Find what "0" counts and label it, or drop it if it repeats (R03). Actions follow D2. | `Events/EventKeyCatalogColumn.cs` |
| EV2 | Events (`issue7`) | The "Event key budget" footer is clipped at the window's bottom edge. The "Default window frames" field is not on the label column (R08). | `Events/EventKeyCatalogColumn.cs`, `Events/EventKeyInspectorColumn.cs` |
| EV3 | Events | Explain D6 in the tab: a hint under "Int value names" on the key inspector ("One key per kind of event; the int param picks the variant — e.g. one Sound key, int = which sound"). | `Events/EventKeyInspectorColumn.cs` |
| CE1 | Clip Editor (`issue8`) | Left pane: Clips "+" / red trash are unboxed; Rig Hierarchy "Edit" is a boxed word and the link is a boxed icon. All four become D2 squares of one size. | `ClipEditor/Panes/ClipListPane.cs`, `ClipEditor/Panes/RigHierarchyPane.cs` |
| CE2 | Clip Editor (`issue8`) | Timeline toolbar right side: Zoom, the All/Selected segmented control (clipped), Add Event, Snap, Auto Key and the Scale From Start dropdown are all different heights and styles. They should be one 24px run, with toggles filled when on (R17). | `ClipEditor/Panes/TimelinePane.View.cs` (+ `ClipEditorWindow.uxml` if the controls are declared there — check first) |
| RT1 | Retarget (`issue9`) | The "Skipped" badge has no gap before the bone name. The column headers (Clip track / Rig part / Status) do not line up with their row cells. | `Retarget/RetargetTrackTableElement.cs` |
| RT2 | Retarget (`issue9`) | Roster footer: chips crowd each other and the "Roster:" label. The transport is a green-filled pause unlike every other transport; use `TransportCoreElement`'s look. | `Retarget/RosterCoverageStripElement.cs`, `Retarget/RetargetPreviewElement.cs` |
| VB1 | VAT Bake (`issue10`) | Issue badge, Unbaked badge and Bake are misaligned (D5). "Issue" does not say what the issue is: the badge tooltip carries the message, and the footer status line (currently clipped at the bottom) shows it in full. | `VatBaking/VatBakePanel.cs` |
| VB2 | VAT Bake (`issue10`) | Output Folder shows only a folder icon, with no path and no placeholder. | `ClipEditor/Shared/PathPickerRowElement.cs` (also fixes Clip Sets' floating folder icon) |
| AP1 | Actor Profiles (`issue11`) | "+ Layer" is an unboxed word that crowds the Preview title (D2). The warning "3" badge in the preview header does not say what it counts: add a tooltip, and click to open Health filtered to it. | `ClipEditor/ActorEditor/ActorEditorPanel.cs` |
| AP2 | Actor Profiles (`issue11`) | The Actor Inspector Animation card has labels on no common column, and "Layer: Base" / "Stopped" are loose lines. Use a property grid, with state as a badge. | `ClipEditor/ActorEditor/` inspector file (grep "Use Clip Default Blend-In") |
| RD1 | Ragdoll (`issue12`) | Bodies "+" / trash are unboxed (D2). The Rig object field clips into the badges. | `Ragdoll/RagdollBodiesColumn.cs`, `Ragdoll/RagdollPanel.cs` |
| RD2 | Ragdoll (`issue12`) | Viewport header: transport, the boxed "Ground only" toggle, and "Pose from" with a checkbox and a "No clip bound" label are three different idioms. They should be one run: transport, divider, segmented Ground/Clip pose, and muted state text. | `Ragdoll/RagdollViewportElement.cs` |
| CU1 | Cutscenes (`issue13`) | Asset bar: "New" is oversized (D1). The wrong-scene warning and "Open Scene" should be a status badge + secondary button (D5). | `ClipEditor/Cutscene/CutsceneEditorPanel.cs` |
| CU2 | Cutscenes (`issue13`) | Cast header: "+ Actor", "+ Prop" and the refresh icon are unboxed words (D2 with tooltips). The hint text overflows the column with no inset. Cast row icon buttons are a different size from D2. | `ClipEditor/Cutscene/CutsceneCastPanel.cs` |
| CU3 | Cutscenes (`issue13`) | Inspector: the "Slot" header is clipped. The slot buttons ("Standing: Idle", "Defaults From Profile", "Remove Slot") are raw full-width Unity buttons. There is no left inset. Fix: property rows, secondary buttons, and "Remove Slot" as destructive. | `ClipEditor/Cutscene/CutsceneEditorPanel.cs` (inspector section; coordinate with CU1 — same file, one worker) |
| HE1 | Health (`issue14`) | The filter segmented control's "All (6)" pill is clipped against the search field. "last scan …" is clipped. "Scan project" is oversized (D1). | `Health/HealthPanel.cs` |
| HE2 | Health (`issue14`) | How to fix: the first fix is an oversized light primary and "Locate" is secondary. Every fix button should be secondary with one width. | `Health/HealthFindingDetailElement.cs` |
| G1 | Game (not the package) | D6: `AnimSoundEventLibrary` maps key → `SoundType`. Add the int-param route: an entry flagged `soundFromIntParam` maps its key to `(SoundType)intParam`. Old entries keep working. Fill the Sound key's "Int value names" from `SoundType`. | `Assets/_Scripts/Data/Structs/AnimSoundEventBlobs.cs`, `Assets/_Scripts/Systems/SoundSystemGroup/AnimEventSoundSystem.cs` (+ the SO and the baking system — a 2-worker task) |

## 3. Build — waves of small Sonnet `worker`s

Every worker brief follows root `CLAUDE.md` §Subagent Delegation: at most two named files, line ranges pasted in, the
task's capture path, "turn 30 stop and report", a ≤30-line report, and no Unity MCP. Workers edit **C# only**. The
single USS worker per wave owns `ClipEditorWindow.uss` and `ToolkitComponents.uss`, and it takes the class names
pinned in this spec. No inline visual styles from C# (Conformance_I), and no colour literals or off-scale sizes in USS
(Conformance_J).

**Wave 1 — foundation (2 workers, parallel)**
- W1a `ClipEditor/Shared/ToolkitComponents.uss` (+ `ToolkitTokens.uss`): D1 (primary 24px, drop the 28px run rule);
  new `.toolkit-icon-square` (24×24, 1px `--unity-colors-default-border` outline, 5px radius, hover +6%, disabled 40%)
  and `.toolkit-icon-square--destructive` (red glyph tint only); `.toolkit-detail-header` margin-bottom 8px (D3);
  `.toolkit-badge-row` (24px, align-items center, 4px gap) for D5.
- W1b `ClipEditor/Shared/ToolkitChrome.cs` (+ `ToolkitIcons.cs`): `MakeIconSquare(Action onClick, string iconName,
  string tooltip)` and `MakeDestructiveIconSquare(...)`, which use those classes and `ApplyIconTone`; `MakeBadgeRow()`.
- **Gate 1** (orchestrator): compile, `Conformance_*` fixtures, capture Texture Packer + Clip Sets.

**Wave 2 — shared columns (4 workers, parallel):** RG1 · TP2 · TP3 · VB2+CS2's badge (`VatFreshnessBadgeElement.cs`
paired with `PathPickerRowElement.cs`), plus the wave's USS worker. **Gate 2**, capture Rigs, Materials, Texture
Packer, Flipbooks.

**Wave 3 — per-tab panels (≈12 workers, parallel, every file disjoint):** TP1 · FB2+FB4 · FB3 · CS1+CS2 · RG2 ·
MT1 · MT2 · EV1+EV2 · EV2 inspector+EV3 · CE1 · CE2 · RT1 · RT2 · VB1 · plus the USS worker. **Gate 3**, capture every
touched tab.

**Wave 4 — the rest (≈7 workers):** AP1 · AP2 · RD1 · RD2 · CU1+CU3 (one worker) · CU2 · HE1 · HE2 · USS worker.
**Gate 4**, capture all 15 tabs, then audit against R01–R22 and this spec in §5.

**Wave 5 — game side (2 workers):** G1. Compile gate plus the sound fixtures. The key-data change needs a rebake;
play-testing is the owner's.

A `verifier` checks each wave's diffs against the briefs before its gate. It never re-derives the work.

## 4. Gating constraint (verified 2026-09-29)

`worktree.py doctor` reports **stage blockers**: the stage checkout has another session's uncommitted materials,
scenes and shader work. A gate stages this branch onto the trunk checkout the Editor is running, so gates and
captures wait until that work is committed or the owner pauses the other session. Workers can edit ahead of it, and
all four UI waves can be written before the first gate if needed. The cost is a larger first compile.

## 5. Log

**Built 2026-09-29, all four UI waves + G1, on branch `spec/a109` (worktree `.claude/worktrees/a109-consistency`).**
Every wave gated through the broker: compile clean, `EditorStyleConformanceTests` 5/5. Not merged, not version-bumped
(still reads 0.60.0; bump to 0.61.0 + CHANGELOG at merge).

**Paused for usage (owner at 94%). Left for the next session, in order:**
1. **Captures were never taken:** the Editor was unfocused every time. Stage the branch
   (`worktree.py stage-commit <sha>`, then `refresh_unity`), have the owner focus Unity, and capture all 15 tabs against
   `issue*.png`. Then `restore-trunk`.
2. **Unverified guesses to check in captures:**
   - Events footer clipping (list `flexShrink`/`minHeight 0`).
   - VAT Bake footer clipping (split `minHeight 0`).
   - The Cutscenes "Slot" clip (inset only).
   - The All/Selected segmented clip (track padding 0 in transport rows).
   - Whether `.toolkit-icon-square` matches the uxml `ToolbarButton`/`ToolbarToggle` (rules for `.unity-toolbar-button`/`-toggle` added).
   - The cutscene slot picker icon `d_Animation.Play`.
3. **FB2 "Make editable" never ran:** drive it once on `MaleHairTextureArray` in memory (it writes PNGs + two assets beside the array).
4. **Behaviour changes to confirm:**
   - VAT Bake disables Bake while a source issue stands (tooltip says why).
   - Health fixes are all secondary except Delete, which stays red.
   - Clicking an Actor Profiles count badge now opens Health filtered to the profile name (wired 2026-09-30, offline-compiled only; drive it once).
5. **G1 (game) needs a rebake:** tick `soundFromIntParam` on a `_AnimSoundEventMapping` entry for the Sound key. The Sound key's
   "Int value names" should list `SoundType` in order. Play-test is the owner's.
6. **Full EditMode suite** (package + game) once, then the owner's checkpoint on D1/D2, then merge (`worktree.py merge a109`, owner's word).

**2026-09-30 capture pass (Editor focused, all 15 tabs captured in `Library/A109Captures/`, `r2_*`/`r3_*` = after fixes).**
Steps 1–4 and 6 above are done; step 5 (G1 rebake + play) and the merge are the owner's.
- Confirmed in captures: D1 primaries, D2 squares (Clip Editor left pane included), D3 gaps, VAT Bake issue badge +
  disabled Bake + unclipped footer, Events footer, All/Selected, Cutscene Slot inspector, Health pills and fix buttons.
- Fixed from captures: destructive word buttons are now outlined red (were borderless ghosts; Clear, Remove, Remove Slot);
  Flipbooks info badge sits in the actions run and hides when empty (was a floating "-"), Output row no longer clipped;
  Rigs target hint was a broken half-sentence; Retarget captions moved to a row-shaped caption row + column rules that
  outrank the shared list-row title/meta rules; validation placeholders ("No profile") are neutral pills in a box-less
  button; Cutscene cast Actor/Prop are a drawn figure glyph and a cube (were two identical Plus squares), stage status
  moved under the header (it wrapped the squares); Ragdoll pose source is a Rest/Clip segmented control and the bodies
  search is clamped to its column.
- Make editable drove clean on `MaleHairTextureArray` (64 PNG frames + `_Editable` flipbook, Bake/Save/Remove live,
  source untouched). It also created a `_Flipbook` names wrapper that renamed the source's catalog row — fixed (lookup
  only). Test outputs deleted afterwards.
- The Actor Profiles badge now opens Health **unfiltered**: Health files a profile's problems under the clip/rig at
  fault, so a profile-name filter showed 0 findings.
- EditMode 969/969. The run caught a real AP2 bug: Blend In was hidden before its property row existed, so it never came
  back — fixed, with a regression test proven to fail on the old order.
- Left as is: Retarget's green pause is the shared transport's "playing" tint (Retarget autoplays); not A109-specific.

**Owner checkpoint answered 2026-09-30:** keep D1 (24px, "same height") and D2; merge approved ("merge now"). Shipped as 0.61.0.
