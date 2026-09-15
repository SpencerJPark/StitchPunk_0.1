# DOTS Animator — Editor Style Guide

> **Status:** approved by the owner 2026-09-15 ("I love the style guide, very much approve"); written the same day from the owner's direction ("clean and professional like shadcn/ui mixed with Blender,
> leaning shadcn, following the Unity editor's colours"). Binding for every tab of the Clip Editor window and every
> panel under `Packages/com.dotsanimationtoolkit/Editor/`. Rendered reference with a rebuilt Ragdoll tab:
> [`EditorStyleGuide.html`](EditorStyleGuide.html).
> **Supersedes:** A72 §2's "the top tab strip stays as it is" (owner, 2026-09-15: it clips its text; replaced by the
> tab list below) and A72 D8's boxing rule where it conflicts with R09. Everything else in A72/A101 stands.

## 1. Owner decisions (2026-09-15 — do not re-ask)

| Id | Decision |
|---|---|
| SG-D1 | **Tab strip → shadcn tab list.** Muted rounded track, the active tab a raised filled pill, no dividers, no underline. In-pane modes (Flipbooks / Images) become a smaller segmented control and never share the tab list's class. |
| SG-D2 | **Hybrid surfaces.** Columns sit flush (Blender), separated by 1px dividers. Related fields inside a detail panel group into cards (1px hairline, 6px radius). Selection lists are flat rows. |
| SG-D3 | **Neutral colour, Unity blue for selection.** Greys come from the Unity editor theme variables. The primary button is a light neutral fill. Unity's highlight blue is used only for selection and focus. Colour otherwise means status. |
| SG-D4 | **Mixed density.** Blender-dense data (22px list rows, 20px fields); shadcn-roomy headers, primary actions and empty states. One spacing scale. |
| SG-D5 | **Judge hard.** Every tab is captured and audited against R01–R22; assume the work can be improved. Clear, good UI is what sells the product. |
| SG-D6 | **Rigs target rows: chips + detail card.** Flat 22px rows (checkbox, node name, full path in tooltip); Kind and Tag as small chips that open their pickers, shown only on ticked rows; the selected target's fields in a Target card. |
| SG-D7 | **Actor Profiles animation rows: hover play, detail for the rest.** Name plus one play icon on hover and while playing; stop lives in the preview transport; speed moves to the selected animation's card in the inspector. |
| SG-D8 | **Texture Packer never requires a recipe.** "Recipes are just saved node set ups for texture packing, but shouldn't be required." The tab opens on a working unsaved setup (Pack Output centred) with Bake available; Save as recipe is optional. |

## 2. Tokens

Neutrals are Unity's built-in USS variables (measured on the Pro skin, 2026-09-15, via `customStyle` inside the Clip
Editor's panel). Using the variables, not the values, is what makes the light skin work.

| Role | USS | Pro-skin value |
|---|---|---|
| Surface (lists, bars, status rows) | `var(--unity-colors-default-background)` | `#282828` |
| Window (columns) | `var(--unity-colors-window-background)` | `#383838` |
| Raised (active tab pill, card header) | `var(--unity-colors-inspector_titlebar-background)` | `#3E3E3E` |
| Divider | `var(--unity-colors-default-border)` | `#232323` |
| Field | `var(--unity-colors-input_field-background)` / `-border` | `#2A2A2A` / `#212121` |
| Button (secondary hover base) | `var(--unity-colors-button-background)` / `-hover` | `#585858` / `#676767` |
| Text | `var(--unity-colors-default-text)` | `#D2D2D2` |
| Label text | `var(--unity-colors-label-text)` | `#C4C4C4` |
| Selection | `var(--unity-colors-highlight-background)` | `#2C5D87` |
| Hover overlay | `var(--unity-colors-highlight-background-hover)` | white 6% |
| Primary fill / primary text | `var(--unity-colors-default-text)` / `var(--unity-colors-default-background)` | inverts with the skin |

Toolkit-owned tokens (`--toolkit-*` in `ClipEditorWindow.uss`):

| Token | Value |
|---|---|
| `--toolkit-color-muted` | `rgb(143, 143, 143)` — meta and hint text |
| `--toolkit-color-hairline` | `rgba(255, 255, 255, 0.08)` — card borders |
| `--toolkit-color-focus` | `rgb(77, 158, 242)` — 1px focus border |
| Status | existing `--toolkit-color-error / -warning / -clean / -playing` |
| Data | existing event and lane palettes, unchanged |
| Type | 11px meta · 12px body · 13px/600 pane title · 16px/600 detail title |
| Space | 2 · 4 · 8 · 12 · 16 · 24 px |
| Inset | 12px column inset |
| Heights | 20 field · 22 list row · 24 control/tab pill · 28 primary/card header · 32 pane header · 36 asset bar |
| Radius | 4 field · 5 button · 6 card/rail/segmented · 7 tab track · pill badge |

Unity's `--unity-metrics-*` variables did not resolve in the probe; sizes are the toolkit's own.

## 3. Components (one `toolkit-*` class family each, built through `ToolkitChrome`)

| Component | Use | Never |
|---|---|---|
| Tab list | Window navigation, top only | Inside a pane |
| Segmented control | A mode inside a pane header, sized to content | Full width, or the tab list's class |
| Button: primary | One per tab, light fill, icon + word | Two on one tab |
| Button: secondary | Outline, icon + word | Filled grey |
| Button: ghost | Toolbars and rails; icon only with tooltip | As the main action |
| Button: destructive | Red ghost, behind a confirmation naming the asset | Filled red |
| Property grid | Fixed label column, fields on one x, joined X/Y/Z vectors | A label pushing its field |
| List row | Flat, 22px, name left, muted mono meta right, selection fill | Individually boxed |
| Card | Groups fields in a detail panel, 28px collapsible header | Nested in a card |
| Badge | State in form (outline pill), status hue or neutral | As a button |
| Empty state | Title, one line of why, the action that fills it | A blank box |
| Status row | Column footer, tone dot + one line | A floating label between controls |
| Viewport rail | Top-left, 24px icon column on a translucent card | Different per viewport |

## 4. Rules

A screen passes only when it breaks none. Cite rules by id in specs, logs and capture audits.

**Text**
- **R01 — No clipped text.** Fit, ellipsis with a tooltip, or wrap in its own container. A control is at least its
  line height plus 4px vertical padding.
- **R02 — Four text styles only.** 11 meta, 12 body, 13/600 pane title, 16/600 detail title.
- **R03 — Say it once.** A value in a row's meta is not repeated in its title or header.

**Space and alignment**
- **R04 — Spacing from the scale** (0/2/4/8/12/16/24), set with gaps on flex containers.
- **R05 — One inset per column** (12px) for headers, hints, fields and rows.
- **R06 — Headers share a row:** 32px, same baseline across columns.
- **R07 — A control row shares one height (24px) and one gap (4px);** groups split by a 1px divider.
- **R08 — Property grids align:** one label column per inspector; long labels shorten with a tooltip.

**Structure**
- **R09 — Flush columns, cards for groups, no nesting.** Rows boxed only when each is a rich object (a layer with its
  animations, a cast member).
- **R10 — Only the content grows** (list body, viewport, timeline, inspector scroll). Headers, dropdowns, buttons and
  fields stay at token height.
- **R11 — Navigation and modes look different** (tab list vs segmented control).
- **R12 — One primary action per tab,** top right of the asset bar or working pane.
- **R13 — Empty is designed** (title, why, action).
- **R14 — Status lives in the footer.**

**Colour and state**
- **R15 — Neutrals from `--unity-colors-*`;** no literal greys in the stylesheet.
- **R16 — Blue means selected or focused;** status hues mean status; data colours stay in data.
- **R17 — States are visible:** hover +4–6%, disabled 40% with no hover, a toggle that is on is filled.
- **R18 — Contrast:** body ≥ 4.5:1, muted ≥ 3:1 on their surface.

**Viewports and proof**
- **R19 — No magenta.** A failed preview material falls back to a neutral grey outline.
- **R20 — One rail style** on every viewport.
- **R21 — Icons match their neighbours:** 16px glyphs in 24px buttons, one stroke weight.
- **R22 — Every visual change ships with captures:** before and after of each affected tab in the docked window at
  its real size, audited against R01–R21 in the spec's log.

## 5. Implementation notes

- UI Toolkit resolves `var(--unity-colors-*)` inside editor panels (verified); a detached element outside a panel has
  no theme, so a detached-panel test cannot check colours.
- `Conformance_I` already keeps visual styles out of C#. R15 extends it to the stylesheet: a literal `rgb(` grey in
  `ClipEditorWindow.uss` outside the data and status palettes is a violation.
- Capture recipe and focus trap: `Assets/_Vault/Memories/Code/AnimationToolkit.md`, "A session CAN see the editor UI".
  Captures need the Editor focused; an unfocused `GrabPixels` returns a stale frame.
