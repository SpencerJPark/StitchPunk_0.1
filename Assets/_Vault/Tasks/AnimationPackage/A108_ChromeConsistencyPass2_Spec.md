# A108 — Chrome consistency pass 2: gutters, one button family, one-tone glyphs

> **Status:** ✅ built 2026-09-17 as **0.57.0** (EditMode 962/962, PlayMode 304/304); specced the same
> day from the owner's review of the A105 pass. One owner checkpoint open in §7.
> **Takes** the next unused minor — `0.57.0` unless A105's close already claimed it. The stage reads `package.json`
> at T0 and records the number in §7 (it read `0.56.0` while this spec was written, with A105's eight commits
> `eb8869e3…29e9ba15` already on trunk).
> **Style guide (binding):** [`Docs/AnimationToolkit/EditorStyleGuide.md`](../../../../Docs/AnimationToolkit/EditorStyleGuide.md) — rules R01–R22, owner decisions SG-D1–SG-D8.
> **Executor:** `spec-lead` (or the orchestrator directly) for §5's waves; the stage owns §6 — every Unity MCP call,
> every capture, the merge and the checkpoint. **No subagent touches Unity MCP.**
> **Runs alone** on the shared style layer (`ToolkitComponents.uss`, `ToolkitChrome`, `ToolkitIcons`); A106 and A107
> must be merged or paused first, because both read those files.

## 0. Session prompt

`Assets/_Vault/Spencer/next-session-a108-prompt.md` — paste it as the first message of the builder session.

## 0.1 The owner's words (2026-09-17)

> "add space between cards and the boarders, in actor profiles layers and Actor Inspector, also make buttons
> consistant, in flipbooks the bake button is big and white with rounded corners while the one next to it is super
> small square corners. try to make buttons consistent and when it is a white one, make it so the icons are a
> different enough color from white that they dont blend in. also for the icon I want all to be one tone color and
> for you to recenter and improve the ones for the tab bar when collapsed. also make them more obvious and different,
> like rig show bones and ragdoll show a body."

Four asks: **gutters**, **one button family**, **icons that read on a white fill**, **a one-tone, centred, legible
tab glyph set**.

## 1. Decisions (recorded, not to be re-asked)

- **A108-D1 — A card never touches its column's edge.** Every column that stacks cards or boxes carries the 12px
  style-guide inset (R05) on both sides, and the stack carries an 8px gap. The two named columns are Actor Profiles'
  layers column and the Actor Inspector; the same class is applied wherever else a card stack currently runs edge to
  edge. The inset is a shared class, never a per-panel inline style (Conformance_I).
- **A108-D2 — One button family.**
  - Every action button in the package carries exactly **one** variant class. `MakeIconTextButton` applies
    **Secondary** by default, so a call site that names no variant can no longer produce an unstyled control — that
    is exactly what the Flipbooks Save button is today.
  - **Every** button — primary, secondary, ghost, destructive, icon-only — uses `--toolkit-radius-button`. No square
    corners anywhere. The base `.toolkit-icon-button` is what is missing it today.
  - **A run of actions shares one height.** A header actions run containing the primary is 28px throughout; a run
    with no primary is 24px throughout. A 28px primary beside a 22px icon button is the defect the owner named.
  - Icon-only buttons in toolbars and rails read as **ghost** by default (transparent, radius, hover fill) without a
    call-site change, because the base class carries that look.
- **A108-D3 — Icons are one tone, tinted from the button's own text colour.** Every `toolkit-icon-button__icon` and
  every tab glyph is tinted to the colour its control resolves — dark on the white primary fill, muted on an
  inactive tab, text-coloured on hover and on the active tab. The tone comes from the stylesheet, never from a
  colour literal in C# (R15). The mechanism is decided by the T0 probe (§4.1) and lives in `ToolkitIcons`, not in
  any panel.
- **A108-D4 — The tab strip gets a drawn glyph set, not Unity's built-ins.** Unity's `Avatar Icon`,
  `Material Icon`, `Texture Icon` and friends are multi-hue and were never drawn to sit together. All fifteen tabs
  are drawn as one-tone signed-distance glyphs by the same recipe as the existing `ToolkitIcons.GhostGlyph`, on one
  optical grid, at one stroke weight (R21). **Rigs is bones. Ragdoll is a body.** §2.3 pins all fifteen.
- **A108-D5 — Centred means measured.** A compact tab is 28px wide and its glyph is 16px; the glyph's ink is
  centred in both axes to within half a pixel, and the capture proves it. See finding TB2 for the cause of the
  current offset.
- **A108-D6 — The drawn glyphs replace the built-ins only in the tab strip.** Transport, rails and row buttons keep
  their resolved editor icons; D3's tint is what makes those "one tone". Redrawing every icon in the package is not
  in this pass.

## 2. Audit — findings to fix

Grounded by reading the files on 2026-09-17; every quoted string and line number below was verified.

### 2.1 Gutters

- **GT1** — `ActorEditorLayersColumn`'s root class `actor-editor__layers-column-root`
  (`ActorEditorLayersColumn.cs:21`) **has no USS rule at all** — it is declared and never styled, so the layer boxes
  sit flush against both column edges. `.toolkit-box` (`ClipEditorWindow.uss:333`) gives 6px top and bottom and
  **zero left and right**.
- **GT2** — `ActorEditorPanel`'s `actor-editor__inspector-column` (`ActorEditorPanel.cs:22`) likewise has no rule;
  `ActorEditorInspectorColumn`'s constructor sets only `style.flexGrow = 1f` (`:69`). Its three cards
  (`MakeCard` at `:220`, `:243`, `:382`) therefore touch the left and right borders. `.toolkit-card`
  (`ToolkitComponents.uss:272`) carries `margin-bottom: 8px` and no horizontal margin.
- **GT3** — Neither column has a top gap either: the first card's border sits on the pane header's divider.

### 2.2 Buttons

- **BT1** — Flipbooks, the owner's example: `bakeButton = ToolkitChrome.MakePrimaryAction(…)`
  (`FlipbooksPanel.cs:113`) → 28px, white fill, `--toolkit-radius-button`. `saveButton =
  ToolkitIcons.MakeIconTextButton(Save, "d_SaveAs", …, "Save")` (`:116`) → **no variant class**, so it falls through
  to the bare `.toolkit-icon-button` (`ClipEditorWindow.uss:134`): 28×22px, `padding: 1px`, **no border-radius**.
  Both are added to the same `headerActions` run (`:122–123`). Big/white/rounded beside small/square, exactly as
  described.
- **BT2** — Only **six** call sites in the whole package call `StyleButton`
  (`ActorEditorInspectorColumn.cs:252`, `RigsPanel.cs:345` and `:356`, `ClipEditorWindow.ComponentStack.cs:1073`,
  `HealthFindingDetailElement.cs:202`, `RetargetTrackTableElement.cs:114`). Every other icon+word button in §5's W6
  table is unstyled.
- **BT3** — `.toolkit-icon-button` has no `border-radius` and no background rule, so an icon-only button is a
  square hole next to every rounded control.
- **BT4** — `.toolkit-icon-button--with-text` (`ClipEditorWindow.uss:155`) sets `height: auto`, so an icon+word
  button's height is whatever its label measures — it cannot line up with a 24 or 28px neighbour by construction.
- **BT5** — `.toolkit-icon-button__icon` is `opacity: 0.55` (`:176`) and `.toolkit-icon-button--with-text >
  .toolkit-icon-button__icon` is `opacity: 0.85` (`:164`). On the white primary fill a 55%-opaque light glyph is the
  "blends in" the owner saw, on top of the tint problem in BT6.
- **BT6** — `ToolkitComponents.uss:152` already tries to fix the contrast:
  `.toolkit-primary-action.unity-button .toolkit-icon-button__icon { -unity-background-image-tint-color: … }`.
  **It has no effect as written**, because the glyph is an `Image` element carrying `image = texture`
  (`ToolkitIcons.cs:308–312`, `ApplyIcon` at `:417`), and `-unity-background-image-tint-color` styles a
  *background-image*, not an `Image`'s content. This is the root cause of the owner's white-button complaint, and
  the T0 probe (§4.1) decides which of the two fixes lands.

### 2.3 Tab glyphs

- **TB1 — They are Unity's multi-hue built-ins.** `TabIconNameByElementName` (`ClipEditorWindow.cs:225–241`) maps
  the fifteen tabs to `Texture Icon`, `Sprite Icon`, `AnimationClip Icon`, `Avatar Icon`, `Material Icon`,
  `Animation.AddEvent`, `UnityEditor.AnimationWindow`, `AvatarMask Icon`, `RenderTexture Icon`,
  `UnityEditor.SceneHierarchyWindow`, `CharacterJoint Icon`, `UnityEditor.Timeline.TimelineWindow`, `FrameCapture`,
  `UnityEditor.ProfilerWindow`, `console.warnicon`. Three of them are blue-green asset icons that read as the same
  smudge at 16px, one is a yellow warning triangle, and none of them says "rig" or "ragdoll".
- **TB2 — The off-centre cause.** `SetUpTabCompactIcon` does `toggle.Insert(0, tabIconImage)`
  (`ClipEditorWindow.cs:1511`), so the glyph is a sibling *before* `.unity-toggle__input`. Compact mode hides only
  the text (`.toolkit-tablist--compact .toolkit-tablist__tab .unity-toggle__text { display: none }`,
  `ToolkitComponents.uss:461`); the input element itself stays in the row with `flex-grow: 1` (`:61–65`), so it eats
  the remaining width and pushes the 16px glyph to the left of a 28px tab. Hiding or zeroing the input in compact
  mode is the fix; whichever is chosen must be proven by capture (D5).
- **TB3 — No hue discipline.** A tab glyph has no tint rule at all, so the active and inactive tabs show the same
  full-colour icon and the strip gives no state feedback from the glyph (R17).

## 3. Files

All paths under `Packages/com.dotsanimationtoolkit/Editor/`. **Ownership is exclusive per wave** — no two tasks in
the same wave name the same file.

| Owner | Files |
|---|---|
| W1 | `ClipEditor/Shared/ToolkitGlyphs.cs` *(new)* |
| W2 | `ClipEditor/Shared/ToolkitComponents.uss`, `ClipEditor/Shared/ToolkitTokens.uss` |
| W3 | `ClipEditor/Shared/ToolkitIcons.cs`, `ClipEditor/Shared/ToolkitChrome.cs` |
| W4 | `ClipEditor/ActorEditor/ActorEditorPanel.cs`, `ClipEditor/ActorEditor/ActorEditorLayersColumn.cs` |
| W5 | `ClipEditor/ActorEditor/ActorEditorInspectorColumn.cs` |
| W6a–W6d | the panel files in §5's variant table, split four ways |
| W7 | `ClipEditor/ClipEditorWindow.uss` (the `.toolkit-icon-button` family only) |
| X1–X5 | `ClipEditor/Shared/ToolkitGlyphs.Assets.cs`, `.Surfaces.cs`, `.Rig.cs`, `.Motion.cs`, `.Body.cs` *(all new)* |
| X6 | `ClipEditor/ClipEditorWindow.cs` (the two tab-icon members only) |
| Y1 | `Tests/Editor/ToolkitButtonStyleTests.cs` *(new)*, `Tests/Editor/EditorStyleConformanceTests.cs` |
| Y2 | `CHANGELOG.md`, `package.json`, `Documentation~/` |

## 4. Before anything is built

### 4.1 The tint probe — the stage runs it, once (`execute_code`, no subagent)

BT6 turns on one platform fact. Probe it in a **floating utility window** (a docked pane that is not the visible tab
has no panel: `resolvedStyle` reads transparent and width reads NaN — style guide §5):

1. Build an `Image` with `image = EditorGUIUtility.IconContent("d_SaveAs").image`.
2. Apply a stylesheet rule setting `-unity-background-image-tint-color: rgb(255,0,0)` on it.
3. Read back whether the drawn glyph is tinted (`resolvedStyle.unityBackgroundImageTintColor` **and** a
   `GrabPixels` of the element — the resolved style can report the value while the content ignores it).

- **Verdict TINTS** → D3 is pure USS: the rules at `ToolkitComponents.uss:152` start working once the selector set
  is completed for every variant, and W3's helper only has to make sure the class is on the `Image`.
- **Verdict IGNORES** (expected) → D3 lands as `ToolkitIcons.ApplyIconTone(Image icon, VisualElement control)`:
  the helper reads `control.resolvedStyle.color` — the colour the **stylesheet** resolved, so no literal enters C#
  — and writes `icon.tintColor`. It re-runs on `AttachToPanelEvent`, on `CustomStyleResolvedEvent` and on the
  button's hover/disabled state changes. Record the verdict in §7 before wave A starts; every W3/X-task brief
  quotes it.

### 4.2 Before captures

`Library/UIAudit/before-a108/` — the stage captures, at the docked window's real size:
`01_ActorProfiles.png`, `02_Flipbooks.png`, `03_TabStripCompact.png` (window narrowed until the strip is in icon
mode), `04_TabStripFull.png`. R22 requires the matching `after-a108/` set at §6.

### 4.3 Read (every worker, only these)

- This spec's §1, §2 and its own task brief.
- `Docs/AnimationToolkit/EditorStyleGuide.md` §2–§4.
- The ranges its brief names. **The read guard refuses whole-file reads over 300 lines** — grep the member, read 40
  lines either side.

## 5. Waves

Each task is one `worker`. Hard rules for every brief: at most two files to edit, named; reading limited to named
ranges; the snippets it needs pasted into the brief; **no Unity MCP, no test run, no compile gate inside the
agent**; "at turn 30 stop editing and write your report"; report ≤30 lines. The orchestrator gates **once per
wave** (§5.4), not per task. A capped agent is never resumed — read its diff and respawn a fresh worker with the
remaining scope.

### 5.1 Wave A — the shared layer and the gutters (7 workers, all `[parallel-safe]`)

Every task in this wave writes against the **class names pinned here**, so the workers never have to see each
other's output.

- **W1 — `ToolkitGlyphs.cs` (new).** The glyph framework. Public surface, pinned:
  - `public enum ToolkitGlyphId { TexturePacker, Flipbooks, ClipSets, Rigs, Materials, Events, ClipEditor,
    Retarget, VatBake, ActorProfiles, Ragdoll, Cutscenes, Capture, Stats, Health }`
  - `public static Texture2D Resolve(ToolkitGlyphId id)` — cache-backed, `HideFlags.HideAndDontSave`, rebuilt when
    `EditorGUIUtility.isProSkin` flips.
  - `internal static Texture2D Rasterize(Func<Vector2, float> signedDistance)` — 32×32 RGBA32, bilinear, clamp,
    white ink, alpha from the distance field with the existing `1.2f / size` edge softness, so the tone comes from
    D3's tint rather than from a baked colour.
  - `internal static float` primitives: `CircleDistance`, `BoxDistance` (the one already in `ToolkitIcons.cs:154`),
    `RoundedBoxDistance`, `SegmentDistance` (capsule; the stroke primitive every glyph shares),
    `Union`, `Subtract`, `Intersect`.
  - Partial class: `public static partial class ToolkitGlyphs`, with `Resolve` switching to
    `BuildTexturePacker()`… one `internal static Texture2D Build<Name>()` per id, **declared** here and
    **implemented** in wave B's partials. W1 ships all fifteen as `partial` declarations plus the switch; the file
    must not compile-error on its own — if a partial method body is required for that, W1 writes each build method
    in its own partial file as a stub returning `Rasterize(CircleDistance…)` placeholder and wave B replaces the
    body. **Decide this once in W1's report** so X1–X5's briefs quote it.
  - **Stroke weight: 0.075 in unit space** (2.4px at 32px, 1.2px at the drawn 16px) for every line in every glyph.
  - `ToolkitIcons.GhostGlyph`, `VatPartsGlyph` and `CutoutPartsGlyph` stay where they are; W1 does not move them.
- **W2 — `ToolkitComponents.uss` (+ `ToolkitTokens.uss` if a token is missing).** Owns every shared-sheet rule this
  pass needs:
  - `.toolkit-column--inset { padding-left: 12px; padding-right: 12px; padding-top: 8px; }` (D1, R05).
  - `.toolkit-card-stack > * { margin-left: 0; margin-right: 0; margin-bottom: 8px; }` — the stack's gap, so no
    panel sets a margin inline.
  - **Action runs (D2):** `.toolkit-action-run { flex-direction: row; align-items: center; }`,
    `.toolkit-action-run > .unity-button { height: var(--toolkit-height-control); min-height: var(--toolkit-height-control); margin-left: 4px; margin-right: 0; }`,
    and `.toolkit-action-run--primary > .unity-button { height: var(--toolkit-height-primary); min-height: var(--toolkit-height-primary); }`.
  - Every variant gets `border-radius: var(--toolkit-radius-button)` — including the icon-only base once W7 has
    moved it (W7 owns `ClipEditorWindow.uss`; W2 owns the component sheet; the rule lives in exactly one of them and
    W2's report says which).
  - Completes the icon-tone selector set for **every** variant, per §4.1's verdict:
    primary → `var(--unity-colors-default-background)`; secondary/ghost → `var(--toolkit-label)`;
    ghost:hover / active → `var(--toolkit-text)`; destructive → `var(--toolkit-color-error)`.
    Under the IGNORES verdict these are `color` declarations that W3's helper reads, not tint declarations.
  - `.toolkit-icon-button__icon` opacity → `1` on every path (BT5). The tone carries the state now, not the alpha.
  - **Compact tab centring (TB2):** `.toolkit-tablist--compact .toolkit-tablist__tab .unity-toggle__input
    { display: none; }` (or `flex-grow: 0; width: 0;` if the toggle stops registering clicks — W2's report names
    which it used and why), plus `.toolkit-tablist__tab-icon { align-self: center; margin: 0; }` and a
    `justify-content: center` on the compact tab itself.
  - `.toolkit-tablist__tab-icon` tone rules: muted inactive, `--toolkit-text` on hover and on
    `.toolkit-tablist__tab--active` (TB3).
  - **No literal greys** (R15). **Conformance_J's pinned literal and font-size counts must not rise** — W2 reports
    its own before/after counts.
- **W3 — `ToolkitIcons.cs` + `ToolkitChrome.cs`.**
  - `ApplyIconTone(Image icon, VisualElement control)` per §4.1's verdict, called from `ApplyIcon`,
    `SetButtonIcon` and `SetButtonIconAndText`, re-applied on `AttachToPanelEvent` and `CustomStyleResolvedEvent`.
  - `MakeIconTextButton` applies **Secondary** unless the caller has already added a variant class (D2). It stays
    source-compatible — no signature change, so the twenty call sites keep compiling.
  - `ToolkitChrome.MakeSecondaryAction` / `MakeGhostAction` / `MakeDestructiveAction`, mirroring
    `MakePrimaryAction` (`ToolkitChrome.cs:140`), so new code never reaches for the raw icon builder.
  - `MakePaneHeader`'s `actions` element gains `toolkit-action-run`; a `MakePrimaryAction` added to an actions run
    adds `toolkit-action-run--primary` to its parent on attach. **One** mechanism, in `ToolkitChrome`, not per panel.
  - `SetButtonGlyph(Button button, ToolkitGlyphId id)` for W1's drawn glyphs, alongside the name-based path.
- **W4 — `ActorEditorPanel.cs` + `ActorEditorLayersColumn.cs`.** Adds `toolkit-column--inset` and
  `toolkit-card-stack` to the layers column root and to the inspector column host (GT1, GT2, GT3). Class names
  only — **no `style.padding`/`style.margin` in C#** (Conformance_I). Reads `ActorEditorLayersColumn.cs:75–95` and
  `:175–260`, `ActorEditorPanel.cs:15–40` and `:330–360`.
- **W5 — `ActorEditorInspectorColumn.cs`.** Same two classes on the scroll host that holds the three
  `MakeCard` calls (`:220`, `:243`, `:382`); drops nothing else. Reads `:60–90` and 25 lines around each card call.
- **W6a–W6d — the variant sweep.** Each worker takes one block, sets the variant with
  `ToolkitChrome.StyleButton(button, ToolkitButtonVariant.X)` immediately after the button is built, and changes
  nothing else:

  | Worker | File:line | Button | Variant |
  |---|---|---|---|
  | W6a | `Flipbooks/FlipbooksPanel.cs:116` | Save | Secondary *(the owner's example)* |
  | W6a | `Flipbooks/FlipbookFramesColumn.cs:43` | Remove | Destructive |
  | W6a | `Capture/CapturePanel.cs:366` | Cancel | Secondary |
  | W6b | `ClipEditor/Shared/ToolkitCatalogColumn.cs:66` / `:71` | New / Refresh | Secondary / Ghost |
  | W6b | `Events/EventKeyCatalogColumn.cs:40` / `:44` | New / Refresh | Secondary / Ghost |
  | W6b | `Events/EventUsageColumn.cs:170` | Open | Ghost |
  | W6c | `TexturePacker/TexturePackerPanel.cs:98` / `:103` | Bake as / Clear | Secondary / Ghost |
  | W6c | `TexturePacker/ImageCatalogColumn.cs:74` | Refresh | Ghost |
  | W6c | `TexturePacker/RecipeCatalogColumn.cs:24` | Save | Secondary |
  | W6d | `Ragdoll/RagdollBodiesColumn.cs:43` / `:48` | Add body / Delete body | Secondary / Destructive |
  | W6d | `Materials/MaterialInspectorColumn.cs:35` | Select | Ghost |
  | W6d | `Health/HealthFindingDetailElement.cs:151` | Open | Ghost |

  The cutscene panel's four icon+word buttons (`CutsceneEditorPanel.cs:406`, `:550`, `:2411`,
  `CutsceneCastPanel.cs:87`) are **out of scope** — A107 owns that file and a 5,510-line file is not worth a
  collision. They inherit D2's Secondary default, which is already an improvement. Record it in §7 as carried.
- **W7 — `ClipEditorWindow.uss`, the `.toolkit-icon-button` family only** (`:134–197`): radius on the base class,
  the ghost look (transparent fill, hover overlay), `--with-text` gets a real height instead of `height: auto`
  (BT4), opacity overrides removed so W2's tone rules win. Touches no other rule in the 1,915-line sheet.

### 5.2 Wave B — the glyphs (6 workers, all `[parallel-safe]`, after W1 lands)

Each X-worker owns **one new partial file**, implements **three** `Build…` methods against W1's primitives, and
reads only `ToolkitGlyphs.cs` plus its brief. Every glyph: ink inside a 0.10–0.90 unit box, optically centred,
stroke 0.075, no fill heavier than the others, legible as a silhouette at 16px.

| Worker | File | Glyphs — the shape, pinned |
|---|---|---|
| X1 | `ToolkitGlyphs.Assets.cs` | **TexturePacker:** a square split into four quadrants, two filled diagonally (channel packing). **Flipbooks:** three stacked frames, offset up-right, the front one outlined. **ClipSets:** a film strip — a rounded rect with four sprocket holes down each side. |
| X2 | `ToolkitGlyphs.Surfaces.cs` | **Materials:** a sphere with a crescent highlight cut out of it. **VatBake:** a grid square with a sine wave running through it. **Capture:** an aperture — a circle with a smaller filled circle and a shutter notch. |
| X3 | `ToolkitGlyphs.Rig.cs` | **Rigs — bones (owner's ask):** two joint circles joined by a tapered shaft, crossed by a second short bone. **Retarget:** two small stick figures with an arrow from one to the other. **ActorProfiles:** a head-and-shoulders bust in profile. |
| X4 | `ToolkitGlyphs.Motion.cs` | **ClipEditor:** a horizontal timeline with three diamond keys and a playhead notch. **Events:** a pin with a round head on a baseline. **Cutscenes:** a clapperboard — a rectangle with a hinged striped top bar. |
| X5 | `ToolkitGlyphs.Body.cs` | **Ragdoll — a body (owner's ask):** head, torso, two arms and two legs hanging slack, with filled joint dots at shoulders, hips and knees — unmistakably different from ActorProfiles' bust. **Stats:** three bars of different heights on a baseline. **Health:** a clipboard with a tick. |
| X6 | `ClipEditorWindow.cs` | Replaces `TabIconNameByElementName` (`:225–241`) with a `Dictionary<string, ToolkitGlyphId>`, and `SetUpTabCompactIcon` (`:1492–1512`) resolves through `ToolkitGlyphs.Resolve`. Keeps the unresolved-icon fallback class exactly as it is. Reads `:220–245` and `:1470–1530` only. |

X6 is parallel-safe with X1–X5 **only if** W1 shipped all fifteen `Build…` declarations; that is why W1 owns the
switch. If W1's report says otherwise, X6 moves to wave C.

### 5.3 Wave C — proof and close (2 workers)

- **Y1 — the ratchet.** `ToolkitButtonStyleTests` (EditMode): `MakeIconTextButton` returns a button carrying
  **exactly one** variant class; `StyleButton` replaces rather than accumulates; `ToolkitGlyphs.Resolve` returns a
  non-null, non-blank 32×32 texture for **all fifteen** ids and returns the cached instance on a second call.
  Extends `EditorStyleConformanceTests` with the pinned `new Button(` count so a future unstyled button fails the
  suite. **Per the project's test rule: revert each fix and watch the test fail, or delete the test** — Y1's report
  names which assertions it proved that way.
- **Y2 — the close.** `CHANGELOG.md` section for the version §7 records, `package.json`, the conformance pin, and
  any `Documentation~/` line that describes the old tab icons. No code.

### 5.4 Gates

One gate per wave, run by the stage (never inside a worker): save → `refresh_unity` → poll
`editor_state.isCompiling` → `read_console` for `error CS####`. Wave C's gate also runs the touched fixtures only
(`ToolkitButtonStyleTests`, `EditorStyleConformanceTests`, `ToolkitPaletteTests`) — **not** the ~700-test suite,
which runs once at the close. If the Editor is closed, grep the project-relative `Logs/Editor.log`, confirm its
mtime is newer than the edits, and say so plainly rather than claiming a compile.

## 6. The stage's own work (no subagent)

1. §4.1's probe, before wave A. Record the verdict in §7.
2. The four before captures (§4.2), and the matching `after-a108/` set at the end. Captures need the Editor
   **focused** — an unfocused `GrabPixels` returns a stale frame — and scale by `pixelsPerPoint`.
3. A capture of the tab strip at **compact width** with all fifteen glyphs visible, judged against D4/D5: one tone,
   centred, and fifteen distinguishable silhouettes. Rigs reads as bones; Ragdoll reads as a body.
4. A capture of Flipbooks' header showing Bake and Save at one height, one radius, and the Bake glyph legible on
   the white fill.
5. Full EditMode + PlayMode suites once, at the close.
6. Merge, then the §7 log, HANDOFF §4 paragraph, roadmap tick, and this file's status line.

## 7. Log (filled while building)

- **T0 (2026-09-17) —** version taken: **0.57.0** (`package.json` read `0.56.0`; A105's eight commits are on trunk and
  claimed no minor). Probe verdict: **IGNORES**, settled statically because the pixel path was unusable while the
  Editor was unfocused. `UnityEngine.UIElements.Image.OnGenerateVisualContent`'s IL reads
  `Image.get_tintColor` into `RectangleParams.color` and **never** touches `unityBackgroundImageTintColor`
  (member-token dump of the method body, 2026-09-17); live corroboration: an `Image` carrying an inline
  `-unity-background-image-tint-color: red` reported `resolvedStyle.unityBackgroundImageTintColor == RGBA(1,0,0,1)`
  while its own `tintColor` stayed white. So `ToolkitComponents.uss:152` is dead as written, exactly as BT6 says, and
  D3 lands as `ToolkitIcons.ApplyIconTone` writing `Image.tintColor` from the control's resolved `color`. The
  positive control (a plain element with `background-image` + tint) could not be photographed, so nothing here
  claims the USS tint works for backgrounds — only that `Image` ignores it.
- **T0 captures —** `Library/UIAudit/before-a108/` at the owner's docked width (974.4×646.8 pt, `pixelsPerPoint` 2.5):
  `01_ActorProfiles.png`, `02_Flipbooks.png`, `03_TabStripCompact.png`, `04_TabStripFull.png` (the last one at
  1382.4 pt, the window briefly maximized and restored). What they prove: GT1/GT2/GT3 — the four layer boxes run
  edge to edge in the layers column and the first sits on the header divider; BT1 — Bake is 28px/rounded beside a
  22px square Save in one run; TB1/TB3 — the compact strip is purple, two near-identical teal humanoids, a cyan
  sphere, a green arrow and a yellow warning triangle; TB2 — the active tab's glyph sits visibly left of centre.
  **Found while capturing (not in §2):** the full-width strip shows **words only** —
  `.toolkit-tablist__tab-icon { display: none }` outside compact mode — so the drawn glyph set is a compact-mode
  surface only, and 04 is a no-regression control rather than a glyph shot.
- **T0 capture recipe (works unfocused too, contrary to the standing note):** `GrabPixels` returns a real current
  frame after ~20 reflected `RepaintImmediately` calls on the host `GUIView`, whether or not the Editor has OS
  focus — what is stale without them is the *first* frame of a window built in the same call. Source row 0 is the
  window's **top** for both `DockArea` and `MaximizedHostView`; the two are not mirrored (an earlier "they differ"
  reading was a replayed wrong history entry). Switch the tab in one call and grab in the next.
- **Wave A (8 workers, one gate) —** W1 glyph framework, W2 component sheet, W3 icons+chrome, W4 both Actor
  Editor columns, W6a/W6b/W6c the variant sweep, W7 the icon-button family. Gate: zero `error CS####`.
  Conformance_J counts unchanged end to end (`ToolkitComponents.uss` 0 colour literals / 14 font sizes,
  `ClipEditorWindow.uss` 135 literals against a pin of 135 — no ratchet raised). W2's TB2 choice:
  `display: none` on `.unity-toggle__input` in compact mode, **plus** a rule putting the input back for an
  `--icon-unresolved` tab, or that tab would lose the word it falls back to. Proven safe by drive rather
  than by reasoning: `BaseBoolField`'s `Clickable` targets the Toggle itself (reflection), and `panel.Pick`
  at the centre and all four edges of a 28x24 tab routes to the toggle. The radius rule for the icon-only
  base went into `ClipEditorWindow.uss` (W7), which owns that family; `ToolkitComponents.uss` (W2) took the
  action-run rules.
- **Deviations from §5, decided and recorded:**
  - **`.toolkit-card-stack` dropped.** `.toolkit-card` already carries `margin-bottom: 8px` and
    `.toolkit-box` its own 6px, so a second rule over the same children would only make the rhythm
    uneven. The 12px inset is the whole fix.
  - **W4 and W5 merged** into one worker: two one-line class additions. The inset goes on the column
    *views* (which carry `toolkit-list-surface`), never on their hosts in `ActorEditorPanel.cs` — the
    surface's -10px pull-back is settled, and a second inset there is the double-gutter trap this repo has
    already paid for once.
  - **`SetButtonGlyph` skipped in W3, then added in X3** when six call sites turned out to need it (below).
  - **W6 shrank from four workers to three.** Once `MakeIconTextButton` defaults to Secondary, the nine
    Secondary buttons in §5's table need no edit at all; only the Ghost and Destructive ones do.
    `EventUsageColumn.cs:170` also dropped out — it builds through `MakeIconButton`, so it is icon-only and
    now reads as a ghost from the base class.
- **Wave B (6 workers, one gate) —** X1–X5 the fifteen shapes, X6 the tab wiring. W1's design call, the one
  that made the two waves parallel: `ToolkitGlyphs` registers shapes through five **erasable
  `static partial void`** hooks called from its static constructor, so the framework compiled on its own
  before a single shape existed. No `switch` over ids, no stub `Build...()` methods.
  Glyph judgement, from a contact sheet rendered at 64px and at the real 16px
  (`Library/UIAudit/a108-glyphs/contact_sheet.png`): twelve of fifteen landed first time. Three were
  rejected on sight and reworked by the stage — **Materials** twice (a thin crescent read as a moon, then a
  half-filled disc read as a prohibition sign; it is now a ring with one specular highlight), **Capture**
  (one shutter notch read as a dial with a hand; three blades at 120 degrees read as a lens), and **Rigs**
  (a crossed pair read as a node graph; it is now one bone with a pair of knuckles at each end). Rigs reads
  as bones and Ragdoll as a body, which were the owner's two named asks.
- **Two defects the gates could not see and the drive did.**
  1. **The tone came out pure black on 14 of 15 glyphs.** `AttachToPanelEvent` fires before the element's
     styles resolve, so `resolvedStyle.color` returned UI Toolkit's initial opaque black — alpha 1, so the
     `a <= 0` guard let it straight through. Every dark button in the window had an invisible glyph while
     five green gates and 962 fixtures saw nothing. Every trigger is now deferred or post-layout (attach
     via `schedule.Execute`, plus `GeometryChangedEvent`); `CustomStyleResolvedEvent` alone is not enough,
     since it only fires for elements declaring `--custom` properties, which is why exactly one button in
     the window had recovered. Re-measured: 16/16 button glyphs and 15/15 tab glyphs now carry their own
     control's resolved colour.
  2. **The Health tab stayed 104px wide in compact mode.** `.clip-editor__tab--health`'s `flex-basis`
     reserves room for its issue count, and `flex-basis` beats the compact rule's `width`, so its glyph sat
     centred in a 104px slot and the strip ended in a gap that read as a missing icon. Compact mode now
     overrides the basis. This was invisible to the centring measurement, which reported 0.000px on all
     fifteen tabs — the glyph *was* centred, in a wrong-sized tab. The capture is what caught it.
- **A tint multiplies; it cannot desaturate (this goes past D6's letter).** D3's tint gives one tone only
  where the source already is one. Driven: exactly six visible call sites were multi-hue — `d_PreTextureRGB`
  on four Bake buttons (Flipbooks, VAT Bake, Texture Packer's Bake and Bake As...) and `d_Avatar Icon` on
  both Ragdoll preview toggles. All six now use drawn glyphs (`ToolkitGlyphId.VatBake`,
  `ToolkitGlyphId.Ragdoll`), keeping the icon name as the fallback. D6's stated reason was not wanting to
  redraw every icon in the package; this redraws none, it points six call sites at glyphs that already
  existed. A built-in icon's texture is not CPU-readable (`GetPixels` throws), so the name is the only
  signal for finding the rest.
- **Wave C —** `ToolkitButtonStyleTests` (5 tests) + `Conformance_K` (1). **Every one proven by reverting
  its own fix and watching it go red**, in three rounds: round A (the Secondary default / one
  `RegisterShape` line / the `Resolve` cache / the USS tint declaration) took exactly tests 1, 4, 5 and K
  red with tests 2 and 3 still green; round B (`MakePrimaryAction` back to `AddToClassList`) took only
  test 2; round C (`StyleButton`'s four `RemoveFromClassList` calls) took test 3, and test 2 with it — the
  same defect seen from the other end. Test 4 named `Rigs` by id, which is the point: a partial file
  dropped from compilation would otherwise make three tabs fall back to words silently.
  Two release pins moved by design rather than to fit a change: `ToolkitGlyphs` joins `Conformance_G`'s
  plain-noun allowlist beside `ToolkitIcons` and `ToolkitChrome`, and the manifest identity pin tracks
  0.57.0.
- **Full suites at the close —** EditMode **962/962**, PlayMode **304/304**, zero failures.
- **Captures —** `Library/UIAudit/before-a108/` and `after-a108/`, both at the owner's docked width
  (974.4x646.8 pt, ppp 2.5), plus `after-a108/02b_BakeSaveRun.png` at 3x and the glyph contact sheet.
  Measured rather than squinted at: Bake and Save now share height 28.0, radius 5.0 and one family; the
  Bake glyph reads at **15.17:1** against its own fill; the whole run measures **saturation 0.00**; the
  compact strip is fifteen 28px tabs with the ink centred to 0.000px. `04_TabStripFull` is unchanged from
  before, which is the point — full mode shows words only, because `.toolkit-tablist__tab-icon` is
  `display: none` outside compact mode, so the drawn set is a compact-mode surface and full mode cannot
  regress from it.
- **Observed, not caused by this pass:** at 974pt with all four Actor Profiles columns open, the 260px Actor
  Inspector runs about 1.2px past the window's right edge, so its last control looks clipped. Measured:
  **zero** elements overflow the column itself. The 12px inset does cost the inspector's fields 24px of
  width, which is the trade the gutter was asked for; the `Rig` ObjectField truncates its label at this
  width where before it did not.
- **Carried —** the four cutscene-panel icon+word buttons (`CutsceneEditorPanel.cs:406`, `:550`, `:2411`,
  `CutsceneCastPanel.cs:87`) are still A107's, and now inherit the Secondary default. The cutscene panel's
  own `ToolkitIcons.Resolve(iconName)` call was left alone for the same reason.
- **For the owner (checkpoint, closed as accepted unless something is game breaking):**
  1. The tab glyph set at compact width — `after-a108/03_TabStripCompact.png`, and the contact sheet at
     `Library/UIAudit/a108-glyphs/contact_sheet.png`, where each glyph is drawn at 64px and at its real
     16px. Rigs is a bone with knuckles; Ragdoll is a whole slack body with joint dots.
  2. The action-run height rule: a run carrying the primary is 28px throughout, so Save grew to match Bake
     rather than Bake shrinking to match Save. `after-a108/02b_BakeSaveRun.png` is that run at 3x.
  3. D6 was widened, as above: six coloured call sites now use drawn glyphs, because a tint cannot take the
     colour out of an icon. Everything else still uses the editor's own icons, one-toned by tint.
