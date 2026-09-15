# A104 — Style foundation: the shared layer every tab draws with

> **Status:** ✅ **built 2026-09-15 as `0.56.0`** (merged `dbaf12c8`; EditMode 876, PlayMode 285; S5 is the owner's, open). First of four
> specs (A104 → A105 + A106 in parallel → A107) that bring every Clip Editor tab to the approved style guide.
> **Style guide (owner-approved 2026-09-15, binding):** [`Docs/AnimationToolkit/EditorStyleGuide.md`](../../../../Docs/AnimationToolkit/EditorStyleGuide.md)
> and its rendered page `EditorStyleGuide.html`. Rules are cited as R01–R22; owner decisions as SG-D1–SG-D8.
> **Executor:** one `spec-lead` for §5 (runs **alone**, so it is granted the window files named in A104-D10); the stage
> orchestrator for §6 (captures, merge, close, checkpoint).
> **Why first:** 11 of the audit's worst findings come from six shared pieces, so fixing those once changes all 15
> tabs: the tab strip, catalog rows, mode toggles, the primary button colour, the list row slot and the preview
> proxy material. The per-tab specs then only fix what is local.

## 0. Session prompt

See `Assets/_Vault/Spencer/next-session-a104-prompt.md`.

## 1. Decisions (recorded 2026-09-15 — do not re-ask)

- **A104-D1 — Two new stylesheets, loaded after the window sheet.** `Editor/ClipEditor/Shared/ToolkitTokens.uss`
  (tokens only) and `Editor/ClipEditor/Shared/ToolkitComponents.uss` (component classes only). `ClipEditorWindow` and
  `VatBakeWindow` add both after `ClipEditorWindow.uss`, so at equal specificity the newer rules win. This overturns
  A72-D9 ("one stylesheet"). A single 1,984-line sheet cannot be edited by parallel workers, and the shared layer
  needs a home that tab specs read but never touch.
- **A104-D2 — Token names** (paste into every brief; the values are in the style guide §2):
  - **Surfaces:** `--toolkit-surface` → `var(--unity-colors-default-background)`, `--toolkit-window` →
    `var(--unity-colors-window-background)`, `--toolkit-raised` → `var(--unity-colors-inspector_titlebar-background)`.
  - **Lines and fields:** `--toolkit-divider` → `var(--unity-colors-default-border)`, `--toolkit-field` / `--toolkit-field-border`
    → `var(--unity-colors-input_field-background)` / `var(--unity-colors-input_field-border)`.
  - **Text:** `--toolkit-text` → `var(--unity-colors-default-text)`, `--toolkit-label` → `var(--unity-colors-label-text)`,
    `--toolkit-color-muted` rgb(143,143,143).
  - **Interaction colours:** `--toolkit-selection` → `var(--unity-colors-highlight-background)`, `--toolkit-hover` →
    `var(--unity-colors-highlight-background-hover)`, `--toolkit-color-hairline` rgba(255,255,255,0.08),
    `--toolkit-color-focus` rgb(77,158,242).
  - **Type sizes:** `--toolkit-text-meta` 11px, `--toolkit-text-body` 12px, `--toolkit-text-title` 13px,
    `--toolkit-text-detail` 16px.
  - **Heights:** `--toolkit-height-field` 20px, `--toolkit-height-row` 22px, `--toolkit-height-control` 24px,
    `--toolkit-height-primary` 28px, `--toolkit-height-pane-header` 32px, `--toolkit-height-asset-bar` 36px.
  - **Spacing:** `--toolkit-inset` 12px.
  - **Radii:** `--toolkit-radius-field` 4px, `--toolkit-radius-button` 5px, `--toolkit-radius-card` 6px, `--toolkit-radius-track` 7px.

  Whether `var()` inside a custom property resolves in UI Toolkit is §6 S0's probe. If it does not, tokens repeat
  the `var(--unity-colors-*)` reference in each component rule instead, and the names above stay as documentation.
- **A104-D3 — The tab strip becomes the tab list, keeping `ToolbarToggle`.** The strip gets `toolkit-tablist`: a
  field-coloured track with a 1px divider border, `--toolkit-radius-track`, 3px padding, 2px gap, and no flex-grow on
  tabs. Each tab gets `toolkit-tablist__tab`:
  - 24px tall, 10px horizontal padding, no borders;
  - muted 12px/500 text;
  - an explicit `-unity-text-align: middle-center` and `height`/`min-height` that override `.unity-toolbar-toggle`'s
    fixed toolbar height (the cause of the clipped descenders, R01).

  The active tab gets `toolkit-tablist__tab--active`: raised fill, text colour, 1px hairline. The Health count stays
  text. `ClipEditorTab`, `tabToggles`, the element names and `ClipEditorLayoutTests` are untouched. The window bar
  holding the strip uses the surface colour with 6px/8px padding.
- **A104-D4 — The segmented control.** New builder
  `ToolkitChrome.MakeSegmentedControl(string elementName, IReadOnlyList<string> labels, int selectedIndex, Action<int> onSelected)`
  returns a `VisualElement` with class `toolkit-segmented`; each segment is a `Button` with `toolkit-segmented__item`,
  and the selected one adds `toolkit-segmented__item--on`. It is sized to its content (no flex-grow), 20px items and
  11px/500 text. There is also `ToolkitChrome.SetSegmentedSelection(VisualElement segmented, int selectedIndex)`.
  `CatalogSidebarElement` modes use it instead of `clip-editor__tab` (R11). This fixes Flipbooks | Images and
  Images | Recipes.
- **A104-D5 — Buttons.** The existing `.toolkit-primary-action` is re-coloured in `ToolkitComponents.uss`:
  - **Primary:** background `var(--unity-colors-default-text)`, text `var(--unity-colors-default-background)`, 28px,
    radius 5, 12px/600, hover lighter, disabled 40%. The ten `MakePrimaryAction` call sites change with no C# edit.
  - **Variants:** `toolkit-button--secondary` (transparent, 1px `--toolkit-divider` lightened border),
    `toolkit-button--ghost` (transparent, no border, label colour, hover `--toolkit-hover`) and
    `toolkit-button--destructive` (ghost with error-colour text).
  - **Builder:** `ToolkitChrome.StyleButton(Button button, ToolkitButtonVariant variant)`, with enum
    `ToolkitButtonVariant { Primary, Secondary, Ghost, Destructive }` in the same file.
  - **Disabled:** `.toolkit-button--*:disabled` is opacity 0.4 (R17).
- **A104-D6 — Flat list rows.** `ToolkitChrome.MakeListRowSlot` rows lose `toolkit-box`: a slot row becomes
  `toolkit-list-row` (22px, 12px inset, row gap 8, hover `--toolkit-hover`, selection `--toolkit-selection`).
  - **Row labels:** new `toolkit-list-row__title` (flex-grow, ellipsis) and `toolkit-list-row__meta` (11px muted,
    right-aligned, no grow).
  - **Catalog rows:** `ToolkitCatalogColumn.MakeRow` builds title + meta on **one** 22px line. Its bind sets the row's
    `tooltip` to the full title and info (R01/R03), and the list's `fixedItemHeight` becomes 22.
  - **Ragdoll:** `RagdollBodiesColumn`'s row uses `toolkit-list-row__title`, which fixes the half-height names without
    a Ragdoll edit beyond the class name (that one-line change is A106's; A104 only guarantees the classes).
- **A104-D7 — Card, badge, empty state, property row, status dot.** New builders in `ToolkitChrome`:
  - `MakeCard(string elementName, string title, out VisualElement body, out VisualElement headerActions)` —
    `toolkit-card`, `__header` (28px, 13px/600, hairline bottom), `__body` (8px/10px padding, column gap 4).
  - `MakeBadge(string text, ToolkitStatusTone tone)` — `toolkit-badge` plus `--ok/--warning/--error/--neutral`;
    18px pill, 11px.
  - `MakeEmptyState(string elementName, string title, string why, string actionText, Action onAction)` —
    `toolkit-empty`, centred column; `actionText` null means no button.
  - `MakePropertyRow(string labelText, VisualElement field, string tooltip)` — `toolkit-property-row`, 20px, label
    `__label` fixed 112px with ellipsis and the tooltip, field `__field` flex-grow (R08).

  `ToolkitStatusTone` already exists; if it lacks a neutral member, add `Neutral` last.
- **A104-D8 — Preview proxies stop rendering magenta (R19).** New
  `Editor/ClipEditor/Preview/PreviewSurfaceMaterialResolver.cs`:
  - static `Material CreateNeutralSurfaceMaterial()`;
  - tries `Universal Render Pipeline/Unlit`, then `Universal Render Pipeline/Lit`, then `Sprites/Default`;
  - sets `_BaseColor` (and `_Color`) to rgba(0.55,0.55,0.58,1), `HideAndDontSave`.

  It replaces `GetBuiltinExtraResource<Material>("Default-Diffuse.mat")` at `PreviewRigMirror.cs:89` and
  `ClipPreviewController.cs:335,609`. `Default-Diffuse` is the built-in Standard material, which URP draws magenta.
  **Materials the resolver creates are owned and destroyed by the object that created them** (a static cache would
  survive a domain reload as a leaked object). The mirror's existing `_ImageIndex` property block path is unchanged.
- **A104-D9 — Style conformance ratchet (`Conformance_J`).** In `EditorStyleConformanceTests`:
  - (a) `ToolkitComponents.uss` contains no `rgb(`, `rgba(` or `#` colour literal, and every `font-size` is
    11/12/13/16px;
  - (b) `ToolkitTokens.uss` defines every token name in D2;
  - (c) `ClipEditorWindow.uss`'s colour-literal count and non-scale font-size count are pinned as a shrink-only
    ratchet at the post-T9 values, recorded in §7.
- **A104-D10 — Window files granted to the lead** (A104 runs alone):
  - `ClipEditorWindow.uxml` (tab strip classes only);
  - `ClipEditorWindow.uss` (tab, bar, primary-action and `toolkit-box` row rules only, plus literal greys in the
    Shared and Toolbar sections → `var()`);
  - `ClipEditorWindow.cs` (stylesheet loading only).

  Every other stage-owned file in the lead contract stays the stage's.
- **A104-D11 — Scope.** The shared layer only. A tab's own layout bugs (Ragdoll's stretched picker, Materials'
  duplicated copy, Capture's two-row bar…) are A105–A107. Expect every tab to change visibly after A104; the §6
  captures record it.

### 1.1 Cross-tab findings this spec fixes (audit of `Library/UIAudit/01…15_*.png`, 2026-09-15)

| Id | Finding | Rules | Where seen | Fixed by |
|---|---|---|---|---|
| G1 | Tab strip clips every descender ("Rigs" reads "Rias"); bordered, underlined, stretched tabs | R01, SG-D1 | all 15 | D3 |
| G2 | In-pane modes reuse the tab class and read as a second, misaligned strip | R11 | Texture Packer, Flipbooks | D4 |
| G3 | Catalog rows are 100px boxed cards: a lighter title band over a darker, truncated path line | R09, R03, R01 | Texture Packer, Flipbooks, Clip Sets, Rigs, Materials, Events, Actor Profiles | D6 |
| G4 | Primary buttons are saturated blue fills; the style guide makes blue selection-only | SG-D3, R16 | 10 tabs | D5 |
| G5 | Proxy quads and ragdoll boxes render solid magenta | R19 | Clip Editor, Retarget, Actor Profiles, Ragdoll, Capture | D8 |
| G6 | List rows inside 22px slots clip their text (padded `toolkit-box` in a fixed-height item) | R01 | Ragdoll bodies, Clip Editor clip list | D6 |
| G7 | Disabled buttons barely differ from enabled | R17 | Ragdoll Delete, Clip Editor Delete, Flipbooks Save | D5 |
| G8 | 151 colour literals and 9/10/14px font sizes in the window sheet | R15, R02 | stylesheet | D1, D9, T9 |

## 2. Files (all under `Packages/com.dotsanimationtoolkit/` unless rooted)

**Lead (worktree):**
- `Editor/ClipEditor/Shared/ToolkitTokens.uss` + `.meta` — new (T1).
- `Editor/ClipEditor/Shared/ToolkitComponents.uss` + `.meta` — new (T2).
- `Editor/ClipEditor/Shared/ToolkitChrome.cs` — builders and `ToolkitButtonVariant` (T3).
- `Editor/ClipEditor/Preview/PreviewSurfaceMaterialResolver.cs` — new; `Tests/EditMode/PreviewSurfaceMaterialResolverTests.cs` — new (T4).
- `Tests/EditMode/EditorStyleConformanceTests.cs` — `Conformance_J` (T5).
- `Editor/ClipEditor/Preview/PreviewRigMirror.cs`, `Editor/ClipEditor/Preview/ClipPreviewController.cs` — material swap only (T6).
- `Editor/ClipEditor/Shared/ToolkitCatalogColumn.cs`, `Editor/ClipEditor/Shared/CatalogSidebarElement.cs` (T7).
- `Editor/ClipEditor/ClipEditorWindow.uxml`, `Editor/ClipEditor/ClipEditorWindow.cs` (T8a); `Editor/VatBaking/VatBakeWindow.cs` (T8b).
- `Editor/ClipEditor/ClipEditorWindow.uss` (T9).

**Stage only:**
- `CHANGELOG.md`, `package.json`, the conformance pin, `HANDOFF.md` §4, the roadmap and `AnimationToolkit.md`.
- `Library/UIAudit/` captures.

## 3. Read (line ranges; grep the member if it has moved)

- `ClipEditorWindow.uss`:
  - `16-96` — tokens;
  - `97-140` — Shared header;
  - `484-530` — primary action;
  - `616-720` — sidebar modes, tab strip, tabs;
  - grep `toolkit-box` for the row rules.
- `ClipEditorWindow.uxml` `1-25` (tab strip); `ClipEditorWindow.cs` `30-40` (`StyleSheetPath`) and grep
  `styleSheets.Add`; `VatBakeWindow.cs` `30-45`.
- `Shared/ToolkitChrome.cs` whole (176 lines); `Shared/CatalogSidebarElement.cs` whole (~100); `Shared/ToolkitCatalogColumn.cs` `50-130`, `180-260`.
- `Preview/PreviewRigMirror.cs` `70-135`; `Preview/ClipPreviewController.cs` `325-340`, `600-615`; `Preview/PreviewLineMaterial.cs` whole (the fallback-chain precedent).
- `Tests/EditMode/EditorStyleConformanceTests.cs` `1-140` (the `Conformance_I` pattern to copy).
- The style guide §2–§4.

## 4. Fixtures

- **F1 `PreviewSurfaceMaterialResolverTests.CreateNeutralSurfaceMaterial_UsesAPipelineShader_NotTheBuiltInStandardMaterial`.**
  - **Asserts:** the shader name is one of the three chain names, and the material is destroyed in `TearDown`.
  - **Revert-to-fail:** return `GetBuiltinExtraResource<Material>("Default-Diffuse.mat")`; its shader is `Standard`.
- **F2 `EditorStyleConformanceTests.Conformance_J_ToolkitComponentSheet_UsesTokensAndTheTypeScale`** (D9 a+b).
  - **Revert-to-fail:** add `font-size: 9px;` and `color: rgb(1, 2, 3);` to one component rule.
- **F3 `EditorStyleConformanceTests.Conformance_J_WindowSheet_LiteralRatchet`** (D9 c).
  - **Revert-to-fail:** add one literal grey rule to `ClipEditorWindow.uss`.
- **Unchanged, must stay green:** `ClipEditorLayoutTests`, `Conformance_I` (no inline visual styles in C#),
  `ClipPreviewCompositeTests`, `SocketPreviewParityTests`, `PackagingConformanceTests`.

## 5. Tasks — lead

- [x] **T0 — Ground.**
  - `git rev-parse --show-toplevel`; claim `a104`; grep every name in §1–§3.
  - Read the S0 probe verdict from the stage (in your prompt): does `var()` inside a custom property resolve?
  - Log drift in §7.

**Wave 1 — five workers, disjoint files.**
- [x] **T1 — `ToolkitTokens.uss`** `[parallel-safe]` (1 new file): a `:root` block with D2's names, per the probe
  verdict; a header comment naming the style guide.
- [x] **T2 — `ToolkitComponents.uss`** `[parallel-safe]` (1 new file):
  - `toolkit-tablist`, `__tab`, `__tab--active` (D3);
  - `toolkit-segmented`, `__item`, `__item--on` (D4);
  - `.toolkit-primary-action` and `toolkit-button--*` (D5);
  - `toolkit-list-row`, `__title`, `__meta` (D6);
  - `toolkit-card`, `__header`, `__body`; `toolkit-badge` and its tones; `toolkit-empty`; `toolkit-property-row`,
    `__label`, `__field` (D7).

  Tokens only, no literals. Selectors must beat `.unity-toolbar-toggle` and `.unity-button` (use the class plus the
  Unity class).
- [x] **T3 — `ToolkitChrome` builders** `[parallel-safe]` (1 file): D4, D5 and D7 signatures verbatim; one `<summary>`
  per file, so none on the new members; no inline visual styles (`Conformance_I`).
- [x] **T4 — `PreviewSurfaceMaterialResolver` + F1** `[parallel-safe]` (2 new files): D8.
- [x] **T5 — `Conformance_J` (F2, F3)** `[parallel-safe]` (1 file): D9. The window-sheet pins start at today's
  counts (151 literals; count the non-scale font sizes) and T9 lowers them.

**Gate W1:** `PreviewSurfaceMaterialResolverTests`, `EditorStyleConformanceTests`, `PackagingConformanceTests`. One
mutation commit with F1–F3's reverts; exactly those three fail.

**Wave 2 — four workers.**
- [x] **T6 — Material swap** `[parallel-safe]` (`PreviewRigMirror.cs`, `ClipPreviewController.cs`): D8. Every
  created material is destroyed where the mirror or markers are disposed.
- [x] **T7 — Catalog rows and sidebar modes** `[parallel-safe]` (`ToolkitCatalogColumn.cs`, `CatalogSidebarElement.cs`): D6, D4.
- [x] **T8a — Tab list + sheet loading** `[parallel-safe]` (`ClipEditorWindow.uxml`, `ClipEditorWindow.cs`):
  - UXML: strip → `toolkit-tablist`; every tab → `toolkit-tablist__tab` (keep `clip-editor__tab` until T9 removes its rules).
  - C#: load `ToolkitTokens.uss` then `ToolkitComponents.uss` after `StyleSheetPath`.
  - Active-tab code: add and remove `toolkit-tablist__tab--active` beside the existing class. Grep
    `TabActiveUssClassName` / `clip-editor__tab--active`.
- [x] **T8b — Standalone window** `[parallel-safe]` (`VatBakeWindow.cs`): load the same two sheets after the window sheet.

**Gate W2:** W1's fixtures + `ClipEditorLayoutTests`, `ClipPreviewCompositeTests`, `SocketPreviewParityTests`.

**Wave 3 — one worker, then close.**
- [x] **T9 — Window sheet cleanup** (`ClipEditorWindow.uss`):
  - delete the `.clip-editor__tab*` visual rules and the sidebar-mode rules D3/D4 replace;
  - delete the old blue `.toolkit-primary-action` colours;
  - delete the `toolkit-box__header` band as used by list rows (not by component boxes);
  - convert every literal grey in the Shared and Toolbar sections to a `var(--unity-colors-*)` or `--toolkit-*` token;
  - fix the 9/10/14px sizes to the scale.

  Then lower F3's pins to the new counts in the same commit.
- [x] **T10 — Close text.** §7 `### For integration`:
  - the CHANGELOG `## [0.56.0] — Style foundation` text;
  - allowlist names (expect none: `PreviewSurfaceMaterialResolver` is a Resolver);
  - the new classes and builders list, for A105–A107's briefs;
  - vault traps and a HANDOFF paragraph.

  Then `status a104 ready`.

**Gate W3:** every fixture above + `EditorStyleConformanceTests` whole.

## 6. Tasks — stage orchestrator

- [x] **S0 — Phase 0.**
  - **Preflight:** `ListAgents`; `doctor`.
  - **Baseline:** compile, EditMode 873, PlayMode 285; CHANGELOG top `## [0.55.0]`; registry sha256s.
  - **Before captures:** already in `Library/UIAudit/01…15_*.png` (2026-09-15, 3206×1655). Copy them to
    `Library/UIAudit/before/`.
  - **Probe** (execute_code, one call): does a custom property defined as `var(--unity-colors-window-background)` resolve?
    - **Setup:** a temporary `StyleSheet` is not creatable from a string, so write
      `Assets/Generated/DotsAnimationToolkit/A104Probe.uss` with `:root { --a104-probe: var(--unity-colors-window-background); }`
      and `.a104-probe { background-color: var(--a104-probe); }`, refresh, and add it to the Clip Editor root with a probe element.
    - **Read:** `resolvedStyle.backgroundColor` in a **second** call.
    - **Clean up:** delete the file and refresh.
  - Put the verdict in the lead's prompt and §7.
- [x] **S1 — Hand gates.** None expected.
- [x] **S2 — Merge** `a104`; compile gate; full suites (EditMode ≥ 873 + 3, PlayMode 285).
- [x] **S3 — After captures.**
  - Re-run the capture chain (the Editor must be focused; if it is not, ask the owner to click into Unity and wait).
    Save all 15 tabs to `Library/UIAudit/after-a104/` and restore the active tab.
  - Look at every capture. In §7, mark each G1–G8 row pass or fail per tab, citing the capture.
  - Add a new finding row for anything the shared change made worse, e.g. a tab whose layout depended on the old
    row height.
- [x] **S4 — Close.**
  - **Versions:** CHANGELOG `0.56.0`; `package.json` and pin `0.56.0`.
  - **Docs:** HANDOFF §4 paragraph; `AnimationToolkit.md` traps; the style guide §5 "implementation notes" gains the
    probe verdict.
  - **Records:** roadmap box ticked; A104 status line and boxes.
  - Commit; push.
- [ ] **S5 — ⏸ owner checkpoint.** A real stop while the owner is present; under the standing rule when away.
  - **Show:** four before/after pairs — Flipbooks (tabs, modes, rows), Ragdoll (rows, magenta), Health (primary button),
    Clip Sets (catalog rows).
  - **Q1.** Does the tab list read right at this window width, or should the tabs shrink/scroll when the window is narrow?
  - **Q2.** Is the neutral light primary button clear enough as "the main action"?
  - **Q3.** Anything in the shared layer to change before the per-tab passes (A105/A106) build on it?

## 7. Build log

*(S0 probe verdict, T0 grounding, gate verdicts, revert-to-fail, drift, For integration, S3 audit table, close.)*

### S0 — Phase 0 (stage, 2026-09-15, against `d009eecd`)

- **Preflight:** `ListAgents` shows one peer (`stitch-punk-ae`, idle, 5 days old); `doctor` reports git 2.43.0, long
  paths on, `brokerAlive` true, hooks installed, no stage blockers; `list --json` shows no worktrees and
  `busyWith` null; `git status` is clean.
- **Baseline:** compile clean with zero console errors. `DotsAnimationToolkit.Tests.EditMode` passed 873 of 873;
  `.PlayMode` passed 285 of 285. CHANGELOG top is `## [0.55.0]` and `package.json` is `0.55.0`.
- **Registry sha256s:** `DotsAnimationToolkitAnimEventKeyRegistry.asset` is
  `3bdb420d55b808ecfd9251ab144ac89645c4d6f903b4a8a3498a42aa76d14701`; `DotsAnimationToolkitTargetTagRegistry.asset`
  is `dbec3d5f6d31db02891682e7f88e6011f7317658f1d29753a0185ff2ebd1eb4f`.
- **Before captures:** the 15 `Library/UIAudit/01…15_*.png` files were copied to `Library/UIAudit/before/`.
- **Window-sheet baseline (for T5's F3 pins):** `ClipEditorWindow.uss` is 1,984 lines with **151** colour literals
  (`rgb(`, `rgba(` and `#hex`). Its font sizes are 9px ×2, 10px ×2, 11px ×2, 12px ×1 and 14px ×1, so there are
  **5** non-scale sizes.
- **Probe verdict: YES, `var()` inside a custom property resolves.** It was defined as
  `--a104-probe: var(--unity-colors-window-background)` and read through `background-color: var(--a104-probe)`. It
  resolved to `56,56,56,255` (`#383838`), identical to a control element that referenced
  `var(--unity-colors-window-background)` directly. D2's tokens can therefore alias the Unity variables.
  - **First attempt, inconclusive:** it read `0,0,0,0` with width NaN for both elements. The docked Clip Editor sits
    behind the Game View tab, so its `rootVisualElement.panel` was null and nothing was styled.
  - **Second attempt:** the probe ran in a temporary floating utility `EditorWindow`, which was closed afterwards.
    The probe elements and sheet were removed from the Clip Editor root, and `A104Probe.uss` and its `.meta` were
    deleted.
- **Drift found while grounding (for the lead's T0):**
  - **(1) Status tones:** `ToolkitStatusTone` is `{ Neutral, Warning, Error }`. It already has `Neutral`, but D7's
    badge wants an `--ok` tone and there is no Ok/Clean member.
  - **(2) Capture trap:** the Clip Editor is currently a background dock tab behind the Game View. S3's capture chain
    must bring it to the front (or capture while it is the visible view) and then restore the Game View.
  - **(3) Sheet loading:** `ClipEditorWindow.cs` has no `styleSheets.Add`. The window sheet loads through
    `ClipEditorWindow.uxml:2` (`<Style src="project://database/…ClipEditorWindow.uss?…guid=9f9579bb…"/>`), and
    `StyleSheetPath` is used only by `VatBakeWindow.cs:37`. T8a has two options:
    - add the two sheets in C# after the UXML clones (by path, GUID-free); or
    - add two `<Style>` lines in the UXML. That option needs the new sheets' `.meta` GUIDs from T1/T2.

    The lead picks one and logs it here.

### T0 — Grounding (lead, 2026-09-15, `spec/a104` from `eb6d8abd`)

- **Names confirmed by grep:** every §2/§3 file exists at its path. `Default-Diffuse` is still at `PreviewRigMirror.cs:89`
  and `ClipPreviewController.cs:335,609`; `TabActiveUssClassName` at `ClipEditorWindow.cs:55/1471` and
  `CatalogSidebarElement.cs:14/93`; `ToolkitCatalogColumn.MakeRow` at 191 with `fixedItemHeight = 64f` at 116.
- **Decisions and drift (11):**
  1. **Status tone:** `ToolkitStatusTone` gains `Ok` last, for D7's `--ok` badge. No caller switches over the enum.
  2. **Sheet loading, S0 drift (3):** the C# route, GUID-free. T3 adds `ToolkitChrome.AddToolkitStyleSheets(VisualElement root)`
     with two public path constants. `ClipEditorWindow` calls it right after `CloneTree`, and `VatBakeWindow` calls it after the window
     sheet, so T8a and T8b are one call each. Every T2 selector carries two classes, so it wins whatever the sheet order.
  3. **Window bar:** the Toolbar's `toolkit-asset-bar` class is shared by every tab's asset bar. D3's surface colour and
     6px/8px padding therefore go on a new class, `toolkit-tablist-bar`, which T8a adds in the UXML.
  4. **List row height:** `min-height` 22px rather than `height`. Five other `MakeListRowSlot` callers (EventKeyCatalogColumn,
     FlipbookFramesColumn, RagdollBodiesColumn, HealthFindingListElement and ImageCatalogColumn) still stack content.
     Their look is a finding for A105–A107 (D11).
  5. **Row selection:** those callers set `toolkit-box--selected`, which draws nothing once `toolkit-box` is gone. T2
     paints both `.toolkit-list-row--selected` and `.toolkit-list-row.toolkit-box--selected` with `--toolkit-selection`.
  6. **USS limits:** USS has no numeric font weights, so 500 → normal and 600 → bold. It has no `gap` and no
     `:first-child`, so tabs and segments are spaced with a 2px right margin.
  7. **Hover and borders:** USS cannot lighten a `var()`, so the primary hover is `opacity: 0.9` (shadcn `primary/90`). The
     secondary button's "lightened divider" border is `--toolkit-color-hairline`.
  8. **Status dot:** D7 names no signature for it, and `MakeSeverityDot` already exists, so no new builder.
  9. **Button variants:** `StyleButton(Primary)` adds the existing `toolkit-primary-action` class, so primary has one rule.
     Destructive adds both the ghost and destructive classes.
  10. **Tests:** no test pins the sidebar mode toggles, the catalog row classes or the tab classes. `ClipEditorLayoutTests`
      only checks that the tabs are `ToolbarToggle`s, and names and types stay.
  11. **`.meta` GUIDs:** the new sheets' `.meta` files are hand-written with GUIDs `ToolkitTokens.uss` `b11b87cc62b64271a239464bf21c7eb6`
      and `ToolkitComponents.uss` `c8a51b7876164e5bae099a516cf9dcb0`. The new `.cs.meta` files are left to the Editor, as in A103.

### Gates and revert-to-fail (lead)

- **W1 `9d761e3f` — pass, 17 of 17**, no compiler or Burst errors (`PreviewSurfaceMaterialResolverTests`,
  `EditorStyleConformanceTests`, `PackagingConformanceTests`).
- **Revert-to-fail — proven.** One mutation commit carried all three probes: the resolver returning the built-in
  `Default-Diffuse.mat`, a `font-size: 9px` / `rgb(1, 2, 3)` rule in `ToolkitComponents.uss`, and a literal grey rule in
  `ClipEditorWindow.uss`. Verdict `test-failures`, 14 passed and **exactly 3 failed** — F1 (shader read
  `Legacy Shaders/Diffuse`), F2 (`rgb(` in the component sheet) and F3 (152 literals against the pin of 151). Then
  `git reset --hard HEAD~1`, and all three sha256s match their pre-mutation values
  (`d481fb7f…` resolver, `df58a1aa…` components sheet, `609492c0…` window sheet).
- **W2 `74a149aa` — pass, 28 of 28**, adding `ClipEditorLayoutTests`, `ClipPreviewCompositeTests` and
  `SocketPreviewParityTests`. The tab-list classes did not disturb the layout fixture.
- **W3 `b4782de2` — pass, 30 of 30**, adding `ToolkitPaletteTests` because T9 edited the sheet that fixture parses.
  `Conformance_J` passes against the lowered pins, so the ratchet is honest at 139 and 0.
- **T9 window-sheet cleanup:** the superseded rules are gone (`.clip-editor__tab*` visuals and the strip, the old blue
  `.toolkit-primary-action` family, `.toolkit-sidebar__modes`, `.toolkit-box.toolkit-list-row`), `.clip-editor__tab--health`
  and the `.toolkit-box*` component rules stay. Four opaque row dividers became `var(--toolkit-divider)`; the
  `--toolkit-color-*` block is untouched, because `ToolkitPaletteTests` mirrors those literals in C#. Font sizes now sit
  on the scale (detail title 14 → 16px, two 10px and two 9px → 11px). **Counts: colour literals 151 → 139, non-scale
  font sizes 5 → 0**, and F3's pins were lowered to 139 and 0 in the same commit. Left as judgement calls, listed here
  rather than forced: `.toolkit-icon-button--playing`, `.toolkit-transport__status`, `.toolkit-box--active`'s blue and
  `.toolkit-chip__block`'s white alpha are state, accent or data colours, not greys.
- **Stage trap, not a spec failure:** the first W1 gate hung for sixteen minutes and two later attempts returned
  `refused: Unity is compiling`. The cause was stage-side — vault `.md.meta` files committed to trunk after this
  worktree forked, which the detached Editor regenerated as untracked files, so restoring trunk failed. The
  orchestrator cleared it (trunk revert `494fc5fd`). A compile refusal is a retry, never a verdict.

### For integration

**CHANGELOG text for `## [0.56.0] — Style foundation`:**

> **Added** — `Editor/ClipEditor/Shared/ToolkitTokens.uss` and `ToolkitComponents.uss`, the shared style layer every
> tab draws from, loaded after the window sheet by both `ClipEditorWindow` and `VatBakeWindow`. Tokens alias Unity's
> theme variables (`--unity-colors-*`), so the light skin follows. New `ToolkitChrome` builders: `MakeSegmentedControl`,
> `SetSegmentedSelection`, `StyleButton` with `ToolkitButtonVariant`, `MakeCard`, `MakeBadge`, `MakeEmptyState`,
> `MakePropertyRow` and `AddToolkitStyleSheets`. New `PreviewSurfaceMaterialResolver`.
> **Changed** — the window tab strip is a shadcn-style tab list (a field-coloured track, a raised active pill, no
> clipped descenders); in-pane modes are a segmented control instead of the tab class; the primary button is a neutral
> light fill rather than saturated blue, with secondary, ghost and destructive variants and a visible disabled state;
> catalog and slot rows are flat 22px lines with a right-aligned meta column and the full title in the tooltip;
> preview proxy quads and socket markers draw a neutral grey material instead of the built-in Standard material that
> URP renders magenta.
> **Tests** — `PreviewSurfaceMaterialResolverTests`, and `Conformance_J` guarding the component sheet's tokens and type
> scale plus a shrink-only literal ratchet on the window sheet.

**`Conformance_G` allowlist:** nothing needed. The one new static class is `PreviewSurfaceMaterialResolver`, and
`Resolver` is already an allowed suffix.

**Wiring:** none. A104 adds no tab, no enum member, no UXML toggle or pane and no panel, so there is no constructor,
`Bind` or `Dispose` call to add. The only window-file changes are the ones A104-D10 grants: tab-list classes in
`ClipEditorWindow.uxml`, `ToolkitChrome.AddToolkitStyleSheets(rootVisualElement)` right after `CloneTree` in
`ClipEditorWindow.cs` plus a second `EnableInClassList` for `toolkit-tablist__tab--active`, the same one-line call in
`VatBakeWindow.CreateGUI`, and the superseded rules removed from `ClipEditorWindow.uss`.

**The shared layer, for A105–A107 briefs.** Classes: `toolkit-tablist`, `__tab`, `__tab--active`, `toolkit-tablist-bar`;
`toolkit-segmented`, `__item`, `__item--on`; `toolkit-primary-action`, `toolkit-button--secondary`, `--ghost`,
`--destructive`; `toolkit-list-row`, `__title`, `__meta`, `--selected`; `toolkit-card`, `__header`, `__title`,
`__actions`, `__body`; `toolkit-badge` with `--ok/--warning/--error/--neutral`; `toolkit-empty`, `__title`, `__why`,
`__action`; `toolkit-property-row`, `__label`, `__field`. Builders: the `ToolkitChrome` members listed above.
Tokens: the 28 `--toolkit-*` names of A104-D2, in `ToolkitTokens.uss`.

**Traps for the vault note.**
- A `var()` inside a custom property **does** resolve in an editor panel, so tokens can alias `--unity-colors-*`; a
  detached element has no panel and no theme, so a detached test can never assert a colour.
- Component rules must carry the toolkit class **plus** the Unity class (`.toolkit-tablist__tab.unity-toolbar-toggle`),
  or Unity's own theme wins the tie and the toolbar height clips the descenders again.
- USS has no numeric font weights, no `gap` and no `:first-child`: 500 is `normal`, 600 is `bold`, and spacing is a
  right margin.
- `ClipPreviewController.cs` imports both `System` and `UnityEngine`, so an unqualified `Object.DestroyImmediate` is
  CS0104 there; it must be `UnityEngine.Object`.
- Materials from `PreviewSurfaceMaterialResolver` are owned by their creator. The mirror destroys its own in `Dispose`;
  the controller destroys the socket-marker material in `Dispose` only, never in `DisposeMirrors`, because markers are
  rebuilt while the controller lives.
- `MakeListRowSlot` rows no longer carry `toolkit-box`. Five callers still set `toolkit-box--selected`, so the shared
  sheet paints that class too; new code should use `toolkit-list-row--selected`.

**HANDOFF paragraph (draft).** A104 landed the style foundation at 0.56.0: two new stylesheets under
`Editor/ClipEditor/Shared/` hold the tokens and the component classes, loaded after the window sheet by the Clip Editor
and the VAT Bake window, so every tab changes at once. The tab strip is now a tab list that no longer clips its
descenders, in-pane modes are a segmented control, the primary button is a neutral light fill with secondary, ghost and
destructive variants, list rows are flat 22px lines, and preview proxies draw neutral grey instead of URP magenta.
`Conformance_J` keeps the component sheet on tokens and the type scale and ratchets the window sheet's literal count
downward. A104 is deliberately the shared layer only: per-tab layout bugs belong to A105–A107, and a tab that looks
worse under the new rows is a finding for them rather than a fix here.

### S3 — After captures and the audit (stage, 2026-09-15, merged `dbaf12c8`)

All 15 tabs were captured to `Library/UIAudit/after-a104/`, the Editor focused throughout, every file a distinct
MD5 (so no stale frame). The docked Clip Editor was a background tab behind the Game View, so it was brought to the
front for the run and the Game View restored after; the window's own active tab (Events) was restored too. The
captures are 3137×1588 against the before set's 3206×1655 — the window is slightly smaller now, so a pair is not
pixel-aligned.

| Id | Verdict | Evidence |
|---|---|---|
| G1 tab strip clips descenders | **pass, all 15** | "Rigs" reads as Rigs in every capture; Flipbooks, Retarget, Capture and Cutscenes keep their descenders; the active tab is a raised pill on a muted track, no dividers or underline |
| G2 in-pane modes read as a second strip | **pass** | `01_TexturePacker` Images \| Recipes and `02_Flipbooks` Flipbooks \| Images are a small segmented control, visibly not the tab list |
| G3 catalog rows are 100px boxed cards | **pass where the shared column is used; one fail** | Flat one-line rows in `03_ClipSets`, `04_Rigs` (left), `05_Materials`, `06_Events`, `10_ActorEditor`. **Fail:** `01_TexturePacker`'s image list still draws a boxed two-line row with a thumbnail — it is `ImageCatalogColumn`, not `ToolkitCatalogColumn`, so A104's row never reached it |
| G4 primary buttons are saturated blue | **pass** | Neutral light fills on Save (`02`), Open in Clip Editor (`03`), Use in Clip Editor (`04`), Create and assign (`05`), Bake (`09`), Capture (`13`), Snapshot (`14`), Scan project (`15`). Blue now appears only as row selection (`03`, `05`) |
| G5 proxies render magenta | **not clean — see the finding** | No magenta in the Clip Editor, Retarget, Actor Profiles, Ragdoll or VAT Bake viewports, but all of them were empty, so the replaced proxy material is **unproven by eye**. `12_CutsceneEditor` still shows magenta faces and two magenta quads |
| G6 list rows in 22px slots clip text | **pass** | `11_Ragdoll` reads Pelvis … RightLowerLeg in full; the Clip Editor's clip list rows are whole |
| G7 disabled buttons barely differ | **pass** | `02_Flipbooks` Bake (disabled) against Save is an obvious difference; `09_VatBake`'s enabled Bake is plainly brighter |
| G8 literals and off-scale font sizes | **pass** | Window sheet 151 → 139 colour literals and 5 → 0 off-scale sizes, both pinned by `Conformance_J` |

**New findings — for A105–A107, not fixed here (A104-D11).** None of these is a regression the shared layer caused
except F3, which the shared row height exposed.

| Id | Tab | Finding | Rules | Owner |
|---|---|---|---|---|
| F1 | Texture Packer | The image list keeps the boxed two-line row with a thumbnail; `ImageCatalogColumn` needs the shared row | R09, R01 | A105 |
| F2 | Flipbooks | The name column is so narrow that every name truncates ("EarA…", "FacialHai…") while the meta takes most of the row | R01 | A105 |
| F3 | Events | Key rows sit roughly four row-heights apart, so four keys fill the column; the shared row sets `min-height`, and this caller still stacks its content | R04, R07 | A105 |
| F4 | Clip Sets | The full asset path repeats on all eleven clip rows | R03 | A105 |
| F5 | Rigs | Target rows are still individually boxed with truncated node paths | SG-D6, R09 | A105 |
| F6 | Events, Actor Profiles, Cutscenes, Capture | Empty panes are a plain sentence ("Select an event on the left.", "No profile assigned.") rather than a designed empty state | R13 | A105/A106/A107 |
| F7 | VAT Bake, Capture, Health | The primary (and the Rebake/Locate pair inside Health's cards) stretch the full column width, so they read as bars rather than buttons | R10, R12 | A106 |
| F8 | Ragdoll | The body picker above the bodies list is a tall empty box | R10 | A106 |
| F9 | Cutscenes | The scene viewport renders magenta faces and two magenta quads. The viewport draws the **open scene** (`TestArea`) through a hidden camera, so the likely cause is scene materials rather than a toolkit proxy — A107 confirms the source before assuming A104 missed a case | R19 | A107 |

### S4 — Close (stage, 2026-09-15)

Version `0.56.0` in `CHANGELOG.md`, `package.json` and the `PackagingConformanceTests` pin. HANDOFF §4 carries the
A104 paragraph, the roadmap's Phase 6 A104 box is ticked, `AnimationToolkit.md` gains the A104 trap section, and the
style guide §5 records the probe verdict. Suites after the merge: **EditMode 876 of 876** (873 + F1–F3),
**PlayMode 285 of 285**, compile clean.

**One stage trap, recorded in `WorktreeToolkit.md` as trap 27 (`d63ceddd`).** Five vault `.md.meta` files committed
to trunk after the worktree forked deadlocked the W1 gate's return for sixteen minutes: the gate detaches the stage
to the fork point where those files do not exist, the Editor regenerates them untracked, and `git switch main` then
refuses to overwrite them. `GateBroker` retried 180+ times while the CLI only ever said `gating`. The metas were
deleted, the commit reverted for the run (`494fc5fd`) and re-landed at this close.
