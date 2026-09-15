# A104 — Style foundation: the shared layer every tab draws with

> **Status:** 📝 specced 2026-09-15 against `18a4039a` (package `0.55.0`), takes `0.56.0`; not built. First of four
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

- [ ] **T0 — Ground.**
  - `git rev-parse --show-toplevel`; claim `a104`; grep every name in §1–§3.
  - Read the S0 probe verdict from the stage (in your prompt): does `var()` inside a custom property resolve?
  - Log drift in §7.

**Wave 1 — five workers, disjoint files.**
- [ ] **T1 — `ToolkitTokens.uss`** `[parallel-safe]` (1 new file): a `:root` block with D2's names, per the probe
  verdict; a header comment naming the style guide.
- [ ] **T2 — `ToolkitComponents.uss`** `[parallel-safe]` (1 new file):
  - `toolkit-tablist`, `__tab`, `__tab--active` (D3);
  - `toolkit-segmented`, `__item`, `__item--on` (D4);
  - `.toolkit-primary-action` and `toolkit-button--*` (D5);
  - `toolkit-list-row`, `__title`, `__meta` (D6);
  - `toolkit-card`, `__header`, `__body`; `toolkit-badge` and its tones; `toolkit-empty`; `toolkit-property-row`,
    `__label`, `__field` (D7).

  Tokens only, no literals. Selectors must beat `.unity-toolbar-toggle` and `.unity-button` (use the class plus the
  Unity class).
- [ ] **T3 — `ToolkitChrome` builders** `[parallel-safe]` (1 file): D4, D5 and D7 signatures verbatim; one `<summary>`
  per file, so none on the new members; no inline visual styles (`Conformance_I`).
- [ ] **T4 — `PreviewSurfaceMaterialResolver` + F1** `[parallel-safe]` (2 new files): D8.
- [ ] **T5 — `Conformance_J` (F2, F3)** `[parallel-safe]` (1 file): D9. The window-sheet pins start at today's
  counts (151 literals; count the non-scale font sizes) and T9 lowers them.

**Gate W1:** `PreviewSurfaceMaterialResolverTests`, `EditorStyleConformanceTests`, `PackagingConformanceTests`. One
mutation commit with F1–F3's reverts; exactly those three fail.

**Wave 2 — four workers.**
- [ ] **T6 — Material swap** `[parallel-safe]` (`PreviewRigMirror.cs`, `ClipPreviewController.cs`): D8. Every
  created material is destroyed where the mirror or markers are disposed.
- [ ] **T7 — Catalog rows and sidebar modes** `[parallel-safe]` (`ToolkitCatalogColumn.cs`, `CatalogSidebarElement.cs`): D6, D4.
- [ ] **T8a — Tab list + sheet loading** `[parallel-safe]` (`ClipEditorWindow.uxml`, `ClipEditorWindow.cs`):
  - UXML: strip → `toolkit-tablist`; every tab → `toolkit-tablist__tab` (keep `clip-editor__tab` until T9 removes its rules).
  - C#: load `ToolkitTokens.uss` then `ToolkitComponents.uss` after `StyleSheetPath`.
  - Active-tab code: add and remove `toolkit-tablist__tab--active` beside the existing class. Grep
    `TabActiveUssClassName` / `clip-editor__tab--active`.
- [ ] **T8b — Standalone window** `[parallel-safe]` (`VatBakeWindow.cs`): load the same two sheets after the window sheet.

**Gate W2:** W1's fixtures + `ClipEditorLayoutTests`, `ClipPreviewCompositeTests`, `SocketPreviewParityTests`.

**Wave 3 — one worker, then close.**
- [ ] **T9 — Window sheet cleanup** (`ClipEditorWindow.uss`):
  - delete the `.clip-editor__tab*` visual rules and the sidebar-mode rules D3/D4 replace;
  - delete the old blue `.toolkit-primary-action` colours;
  - delete the `toolkit-box__header` band as used by list rows (not by component boxes);
  - convert every literal grey in the Shared and Toolbar sections to a `var(--unity-colors-*)` or `--toolkit-*` token;
  - fix the 9/10/14px sizes to the scale.

  Then lower F3's pins to the new counts in the same commit.
- [ ] **T10 — Close text.** §7 `### For integration`:
  - the CHANGELOG `## [0.56.0] — Style foundation` text;
  - allowlist names (expect none: `PreviewSurfaceMaterialResolver` is a Resolver);
  - the new classes and builders list, for A105–A107's briefs;
  - vault traps and a HANDOFF paragraph.

  Then `status a104 ready`.

**Gate W3:** every fixture above + `EditorStyleConformanceTests` whole.

## 6. Tasks — stage orchestrator

- [ ] **S0 — Phase 0.**
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
- [ ] **S1 — Hand gates.** None expected.
- [ ] **S2 — Merge** `a104`; compile gate; full suites (EditMode ≥ 873 + 3, PlayMode 285).
- [ ] **S3 — After captures.**
  - Re-run the capture chain (the Editor must be focused; if it is not, ask the owner to click into Unity and wait).
    Save all 15 tabs to `Library/UIAudit/after-a104/` and restore the active tab.
  - Look at every capture. In §7, mark each G1–G8 row pass or fail per tab, citing the capture.
  - Add a new finding row for anything the shared change made worse, e.g. a tab whose layout depended on the old
    row height.
- [ ] **S4 — Close.**
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
