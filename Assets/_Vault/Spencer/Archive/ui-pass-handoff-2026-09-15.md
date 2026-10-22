# Editor UI pass — handoff, 2026-09-15

**Package:** `com.dotsanimationtoolkit` at **0.56.0**. **Trunk HEAD when this was written:** `3184a427`.
**Binding law for this work, in this order:**

1. `Assets/_Vault/Tasks/Claude/StyleGuideReferenceImage.png` — the owner's own capture of the rebuilt Ragdoll
   tab. **Look at it first.** It is the target composition and it shows what the rules do not state outright.
2. `Docs/AnimationToolkit/EditorStyleGuide.md` — approved 2026-09-15, rules R01–R22, decisions SG-D1–SG-D8.
3. This document, for what is already built, what is measured, and what is left.

The owner judges **by captures**, not by descriptions. Every claim below was verified from a real screenshot or a
live measurement, never from a worker's report alone.

## 1. What shipped

A104 (the shared style layer) was built through `/worktree-run`, merged at `dbaf12c8` and closed at `77b0354c`.
Everything after that is direct restyle work on trunk, driven by the owner reviewing captures live.

| Commit | What |
|---|---|
| `dbaf12c8` | A104 merged: tokens sheet, components sheet, `ToolkitChrome` builders, `PreviewSurfaceMaterialResolver`, `Conformance_J` |
| `77b0354c` | A104 closed at 0.56.0 (CHANGELOG, package.json, pin, HANDOFF §4, roadmap, vault traps, style guide §5) |
| `a3c55acd` | Owner's S5 answers: centred tab list, icon tabs when narrow, the first two-tone column |
| `d3f39da8` | The owner's reference capture, tracked in the repo |
| `d8965d80` | Flat rows in six lists, two-tone columns, coloured Health count, Ragdoll badges, real buttons, five empty states |
| `f4c726c0` | Images list matched to Flipbooks, raised middle column, Flipbooks detail-pane card |
| `305035f9` | Rigs chips, Materials card, Events footer |
| `0f5d3c6c` | Rigs alignment, Materials orphan label removed |
| `0d2d6b5f`, `3184a427` | The written record: what is done, and why Rigs needs a detail card |

**Verified at every step:** compile clean, **EditMode 876 of 876**, zero colour literals in `ToolkitComponents.uss`.
PlayMode was last run at the A104 close (285 of 285); nothing since touches runtime code.

## 2. The shared layer, as built

In `Packages/com.dotsanimationtoolkit/Editor/ClipEditor/Shared/`:

- **`ToolkitTokens.uss`** — every `--toolkit-*` token, each neutral aliasing a `var(--unity-colors-*)` variable.
- **`ToolkitComponents.uss`** — sections A–O: tab list, segmented control, button variants, list rows, card,
  badge, empty state, property row, list surface, compact tab list, row balance, column host, raised column.
- **`ToolkitChrome`** — `MakeSegmentedControl`, `SetSegmentedSelection`, `StyleButton` + `ToolkitButtonVariant`,
  `MakeCard`, `MakeBadge`, `MakeEmptyState`, `MakePropertyRow`, `MakeStatusRow`, `SetStatus`, `MakeListRowSlot`,
  `MakeSeverityDot`, `AddToolkitStyleSheets`.

**The two-tone recipe** (this is the owner's headline requirement — a tab that reads as one flat grey has failed):

- the column stays `--toolkit-window`;
- its **list body** takes `toolkit-list-surface` — the darker surface, a divider above, and −10px side margins so it
  reaches the pane edge;
- a column that only **hosts** other columns takes `toolkit-column--host` (no side padding), or the dark sits in a
  grey gutter from two stacked insets;
- the **header and search keep their inset** — the owner asked for that explicitly after the first attempt;
- a **middle** column between a catalog and a working pane takes `toolkit-column--raised`.

## 3. The ranked list — all seven closed 2026-09-15 (A105)

Commits `eb8869e3`, `2d841291`, `e46f7926`, `A105-4`. Compile clean and EditMode 876 of 876 at every gate.
After-captures in `Library/UIAudit/after/`; `now/` is what they replaced.

1. ~~Two-tone the six flat tabs.~~ Done. The Clip Editor's panes do not inset their children, so its lists needed a
   new `toolkit-list-surface--flush` (the tone without the −10px pull-back) rather than the plain class.
2. ~~Viewport empty states.~~ Done. `ViewportFrameElement.SetEmptyState` / `ShowEmptyState` is the one way now
   (R20), used by Retarget, Actor Profiles and Capture; the Clip Editor's viewport is uxml, so its overlay is
   inserted at index 1 of `viewport-frame` — above the image, under the rail, picking ignored.
3. ~~Inspectors become cards.~~ Done for the Clip Inspector and the Actor Inspector. `BuildAnimationBlock` is
   deliberately **not** converted — left whole rather than half-done; it is the obvious next piece.
4. ~~Stats.~~ Flat cards, aligned rows, and the em dash explains itself in a tooltip where it is read.
5. ~~Rigs target rows.~~ SG-D6's Target card, built. See §5 — the row constraint is unchanged, the chips simply
   left the row.
6. ~~Cutscenes magenta.~~ **Proven, and it is not the toolkit's.** The viewport renders the open scene faithfully;
   the scene is broken. `Assets/Materials/UnitsLegacy/Faceware.mat` names shader guid
   `e5e6305bdbeddc649b56d342dc41c652`, which no `.meta` in the project claims — the shader asset is gone, so Unity
   substitutes `Hidden/InternalErrorShader`. Two `Quad` renderers in `TestArea` also carry a null material. R19
   governs materials the toolkit creates; substituting one into the owner's scene would be lying about it. **Owner
   call: which shader those faces should use.**
7. ~~Retarget's roster chips.~~ Real badges now, toned ok/warning/error. The five coverage blocks went with them:
   they encoded the ratio the count already states (R03), and the badge's tone carries the at-a-glance signal.

Still open from A104's own audit: Texture Packer's Recipes mode (SG-D8: never require a recipe) and Actor Profiles'
hover play (SG-D7).

## 3b. The strict R01–R22 audit (2026-09-15, all fifteen tabs)

SG-D5 asks every tab to be captured and audited, so all fifteen were — `Library/UIAudit/audit/`. Four read-only
verifiers checked the rules that can be read out of code; the rest were judged from the captures.

**Fixed in this pass**

- **R08, three tabs.** VAT Bake ("Settings"/"Output" were bare headings over ragged fields), Ragdoll ("Rig settings"
  likewise — and its own reference image shows it as a card), and Flipbooks, whose import row read
  `Filter [Bilinear] Wrap [Clamp] Mips ☐ Linear ☐` so that which word labelled which control was genuinely
  ambiguous. All three are cards over `MakePropertyRow`'s single 112px label column now.
- **R01, everywhere at once.** A 112px label column ellipsizes a long label ("Default Linear Da…", "Full Precision
  (RG…"), and R01 only permits an ellipsis that a tooltip can recover. Rather than patch three call sites,
  `MakePropertyRow` now falls back to the label's own words when a caller passes no tooltip — every property row in
  every tab, including ones not written yet.
- **R15, four literals.** `rgb(56,56,56)` → `var(--toolkit-window)` ×2, `rgb(88,88,88)` and `rgb(103,103,103)` →
  the Unity button variables. `Conformance_J`'s pin moved 139 → 135, which is the shrink-only direction it allows.
  The remaining literals have no exact token — a near-match would silently change a colour, so they stay.
- **A latent break in the shared layer.** `ToolkitComponents.uss` painted badges and warning/error text from
  `--toolkit-color-warning/-error/-clean` but declared none of them; they lived only in `ClipEditorWindow.uss`,
  while `AddToolkitStyleSheets` loads just the tokens and components sheets. Nothing was visibly wrong — both
  windows load the window sheet first — but the shared layer did not stand alone, so the next window that forgot
  it would have lost every badge tone with no error. Declared in `ToolkitTokens.uss`, with
  `ToolkitPaletteTests.SharedTokenSheet_StatusHues_MatchToolkitPalette` pinning the two copies against the C#
  palette (proven to fail: drifting one channel reports "warning green channel drifted, expected 170, was 171").

**Found and deliberately NOT changed — these are yours to call**

- **Texture Packer breaks R12 and R16.** Two primary actions on one tab: "Bake" in the asset bar *and* a second
  "Bake" inside the Pack Output node. The node's header is also solid violet, which is neither a status hue nor a
  documented data colour — SG-D3 says colour otherwise means status. Both sit inside the node-graph view, which is
  its own spec's territory, and SG-D8 ("the tab opens on a working unsaved setup, Pack Output centred, a recipe
  never required") is still open — the tab still shows "No recipe" with the node parked bottom-right. One spec,
  not a restyle.
- **R04 off-scale spacing.** `padding: 3px` on the segmented track, `8px 10px` card bodies, assorted 6px margins in
  the window sheet. All are on approved-looking surfaces; moving them onto 0/2/4/8/12/16/24 would nudge visuals you
  have already signed off, so it wants your eye rather than my judgement.
- **R10 flexGrow hits are mostly false positives.** A verifier flagged seven controls with `flexGrow = 1f`; on
  inspection all but the known ragdoll-picker case are in horizontal rows, where filling the remaining width is
  correct. R10's real target is a control stretching *vertically* in a column. Not changed.

## 4. The loop that worked

1. Read the reference image and the guide. Pick one defect you can see in a capture.
2. Spawn `worker` agents — **parallel, one or two files each, files disjoint**. Paste the exact line numbers, the
   builder signatures and the constraints into the brief. Workers never call Unity MCP.
3. **Wait for every worker in the wave** before compiling: gating a half-written file wastes a cycle and blocks the
   test runner.
4. `refresh_unity` (compile, force) → `editor/state` → `read_console` → full `DotsAnimationToolkit.Tests.EditMode`
   (~10s, 876 expected).
5. **Capture and look.** Switch tabs, 45 `RepaintImmediately`, `GrabPixels` into a **linear** RenderTexture, flip,
   save, restore the owner's tab. Check `EditorApplication.isFocused` first — an unfocused grab returns a stale
   frame and must never be saved under a capture name.
6. Judge hard against the reference. Commit with a message that says what was wrong and what changed.

**When a capture is ambiguous, measure instead of squinting:** walk the hierarchy in `execute_code` and print
`worldBound`, resolved padding/margins and `resolvedStyle.backgroundColor`. That is what found the double inset.

## 5. Facts that cost real time — do not re-derive

- **A Rigs target row is 330px** at the owner's window width. A checkbox, a readable node name and two chips do not
  fit. Three width passes each traded one defect for another (overflow + scrollbar → collapsed name → clipped chip
  text) and were reverted. Build SG-D6's **Target card** instead.
- **Two stacked insets** put the darker list in a grey gutter: the catalog column pads, and the sidebar hosting it
  pads again. `toolkit-column--host` fixes the outer one; the list's −10px margins cancel the inner one.
- **Default `flex-shrink: 1` makes a detail pane overlap itself** when its column runs short. Fixed-content siblings
  beside a `flexGrow` region take `style.flexShrink = 0f`.
- **A dropdown with `flexGrow = 1` in a column becomes a tall empty box** — that was the Ragdoll body picker.
- **`Conformance_I`** fails on any inline visual style written from C# (colour, border, radius, opacity, font);
  layout writes are fine. Its allowlist is **shrink-only**: when a file stops having inline styles, its entry must be
  removed or the fixture fails.
- **`Conformance_J`** fails on any colour literal or off-scale font size in `ToolkitComponents.uss`, and ratchets
  `ClipEditorWindow.uss` down (139 literals, 0 off-scale sizes). Never raise a pin to make a change fit.
- **`ToolkitPaletteTests`** mirrors the `--toolkit-color-*` block against C#; those literals stay literal.
- **Rich text, not `style.color`**, is how a status colour goes into a label — `HealthPanel.BuildSeverityToggleText`
  is the precedent, and it is why the Health tab count colours only the number, not the word.
- **A test that pins markup will fail a restyle.** `RigsPanelTests` located rows by `toolkit-box__header`; the
  locator moved to `toolkit-list-row` and its assertions were left untouched. Change the locator, never the
  assertion, and say so in the commit.

## 6. Standing owner directions from this session

- Follow the style guide **religiously**; use the reference image as the target.
- **Do not remove features.** Restyle only. If a change would drop information, keep it (a tooltip is acceptable,
  losing it is not).
- **Judge by captures.** Do not report a visual result that has not been photographed.
- Do not check in constantly while working — work through the list and show results.
- Settled looks: centred tab list; icons with tooltips when the window is narrow; dark list columns edge to edge
  with the header and search keeping their inset; a differently toned middle column; the Health issue count in a
  status colour.
