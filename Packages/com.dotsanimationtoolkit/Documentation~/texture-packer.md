# The Texture Packer

**Window ▸ DOTS Animation Toolkit ▸ DOTS Animator** — the Texture Packer tab, first in the row.

Packs greyscale images into the red, green, blue and alpha channels of one
texture — the shape a mask map, a channel-packed material, or any other
multi-signal texture needs. Drag sources in from the sidebar or the Project
window, wire each one into a channel of the output, and bake over the output
in place.

---

## What the tab gives you

- **The sidebar has two modes, Images and Recipes,** switched by a pair of
  toggles where a pane title would sit. **Images** lists every `Texture2D`
  found under your project's Assets folder — nothing under a package is
  offered — each row a boxed thumbnail 48 pixels square, the texture's name,
  its dimensions and folder underneath, and a search field above the list to
  narrow it. Drag a row, or a shift-selected run of rows, out onto the canvas
  exactly as you would drag files out of the Project window — both land on
  the same drop handling, so neither path is the "real" one. Double-clicking
  a row instead adds that texture at the centre of whatever the canvas
  currently has in view. A row already present on the canvas shows a
  checkmark rather than being added again, and the eye toggle in the header
  hides rows already on the canvas from the list, for when the sidebar is
  more list than you want to scroll. Refresh rescans the project for new or
  renamed textures.
- **Recipes is the saved-setup catalog**, the same boxed-row shape as
  Images. **New** asks for a name and a folder before creating anything —
  cancel and nothing is written — and remembers the folder you chose for
  next time. Clicking a row loads that recipe's graph onto the canvas;
  right-click offers Rename and Delete. Double-clicking a recipe asset in
  the Project window opens the tab with that recipe already loaded.
- **Three things can happen when you drop a texture, depending on where it
  lands.** Dropped on open canvas space, it becomes a new, unwired source
  node. Dropped on a channel row of the Pack Output node, the texture is
  added as a source *and* its red channel is wired straight into that row,
  replacing whatever wire was already there — a fast path for the common
  case of one greyscale mask per channel; only the first texture in a
  multi-drag is wired this way, the rest land unwired beside it. Dropped
  directly on an existing source node, it replaces that node's texture in
  place — the node keeps its ports, so every wire survives, and its title,
  thumbnail and size label update to match; a source already used elsewhere
  on the canvas refuses the drop rather than create a second copy the recipe
  could not tell apart.
- **A source's thumbnail carries four small R G B A chips underneath it.**
  Click one to preview that single channel of the source as greyscale;
  click it again to go back to the texture as imported. None of this
  touches what gets baked — it is a way of checking what a channel actually
  holds before you commit it to a wire.
- **A Presets ▾ menu sits beside the output's size field**, offering Match
  Largest Source alongside the common square sizes from 256 up to 4096. Pick
  Match Largest Source and the field is set to whatever the largest source
  currently on the canvas measures; pick a number and the field is set to
  that square size. Either way the size field itself stays the one place
  that decides the output's dimensions — the menu only ever writes into it.
- **Each channel on the output is either wired or flat.** A wired channel
  offers an invert toggle, for a mask that reads backwards from what you
  need. An unwired channel offers a flat-value slider instead, so a channel
  nothing feeds still bakes a deliberate constant rather than an accidental
  zero.
- **A recipe is written only by pressing Save in the Recipes column** —
  nothing else ever writes one. Baking does not touch the loaded recipe,
  loading one does not rewrite it, and renaming or deleting leaves its
  graph alone. Because of that, the graph header shows the recipe's name
  with a trailing dot the moment the canvas stops matching what was last
  loaded or saved, so it is always visible whether a Save would change
  anything. Switching to another recipe, starting a new one, or clearing the
  canvas while that dot is showing asks first, with the option to discard or
  go back — the tab never saves behind your back, so it also never throws
  work away behind your back.
- **Baking overwrites the output file in place rather than replacing it**,
  which preserves the file's identity: any material already pointing at it
  keeps pointing at it, no reassignment needed. Import settings are stamped
  onto the file only the first time it is created — a repack of an existing
  output never undoes import configuration you set by hand afterward.
- **Reading a source's pixels favours the exact bytes over the imported
  texture.** PNG and JPEG sources are read directly from disk and decoded
  byte-for-byte, so how the file is imported — compression, sRGB, read/write
  flags — has no bearing on what gets packed. Any other format falls back to
  a linear blit through the texture importer instead, which does pass
  through import settings, and logs a warning so the difference is never
  silent.
- **The alpha channel is only written when it has something to say** — when
  it is wired to a source, or its flat value is set to anything other than
  fully opaque. An output nobody asked to have transparency does not get an
  alpha channel it doesn't need.

---
