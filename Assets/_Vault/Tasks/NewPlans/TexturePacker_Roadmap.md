# Texture Packer tab — the channel packer moves into the toolkit, with an image sidebar

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.28.0`.
> **Spec:** [`Amendment_A81_TexturePacker_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A81_TexturePacker_Spec.md).
> **Session prompt:** [`Amendment_A81_TexturePacker_Prompt.md`](../../../../Docs/AnimationToolkit/Amendment_A81_TexturePacker_Prompt.md).
> **Executor:** one Editor-connected orchestrator running the gate; thirteen `worker` subagents in
> **one wave**, every task ≤ 2 files with named line ranges, gated once. The orchestrator writes the
> shared value types first so the wave codes against files on disk.
> **Predecessors:** [`RigsTab_Roadmap.md`](RigsTab_Roadmap.md) (the catalog column being mirrored),
> [`SharedAssetSelection_Roadmap.md`](SharedAssetSelection_Roadmap.md) (the one-wave cut).

## What it delivers

- **The Texture Channel Packer leaves `Assets/_Scripts/Editor/TexturePacker/`** and becomes
  `Packages/com.dotsanimationtoolkit/Editor/TexturePacker/` — the **first tab** of the DOTS Animator
  (`ClipEditorTab.TexturePacker = 0`). The game folder is deleted; no recipe asset existed to migrate.
- **A sidebar in the Clip Sets / Rigs shape**, segmented `Images | Recipes`. Images: every
  `Assets/`-rooted `Texture2D` as a boxed row with a 48px thumbnail, name, size and folder; search;
  drag one or many onto the canvas (an editor drag, so the graph's existing Project-window drop code
  accepts both); double-click adds at the visible centre; ✓ on rows already on the canvas and an eye
  toggle to hide them. Recipes: the A77 catalog (New / Refresh / search / right-click Rename, Delete),
  click to load — replacing the toolbar's recipe object field.
- **Three graph extras:** dropping an image on an R/G/B/A row of the Pack Output node adds and wires
  it; a Presets ▾ menu beside the size field; R G B A chips under each source thumbnail that isolate
  that channel.
- **The packing logic becomes testable:** `TexturePackMath` (pure) gets the two fixtures the tool
  never had; `TexturePackBaker` (instance, owns the decode cache) and the recipe translation on the
  graph view keep the panel small.

## Owner calls recorded in the spec (§2) — do not re-ask

First tab; boxed list rows, not a grid; segmented sidebar with a recipe catalog; double-click add,
channel-row drop auto-wire (R channel), presets + chips; **not** this round: standalone window,
auto-repack on source change, rig-scoped filter, `Packages/` textures. Two ⚠ interpretations for the
checkpoint: the sidebar switch borrows the top-tab look (D6); New creates the recipe instantly in
the remembered folder with a default name, like Clip Sets (D13).

## A finding the build must act on first

The game-side tool's verification checklist
(`Assets/_Vault/Tasks/Verification/verify-texturechannelpacker.md`) has every box unticked since
2026-07-09 and no `TexturePackRecipeSO` asset exists in the project — it may never have been run.
T0 drives it once before anything moves, so the port starts from known behaviour.

## Open after the build

⏸ **T18 owner checkpoint** — what to open and press, and three questions: the sidebar switch style,
instant-New vs prompt-for-name, and the 280px sidebar start width.
