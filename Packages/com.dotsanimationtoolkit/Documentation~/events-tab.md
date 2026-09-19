# The Events Tab

**Window ▸ DOTS Animation Toolkit ▸ DOTS Animator ▸ Events** — between Materials
and Clip Editor in the tab strip. Three columns: the project's event registry on the
left, the selected event's fields in the middle, and where that event is used
on the right.

---

## Keys

The left column is the project event registry, shown as a searchable catalog.
Each row's first line is the event name; the second reads either
`16 · maskable` or `80 · pulse-only`. Keys 16 through 79 are
maskable — there are 64 of them, each owns a bit in `AnimEventMask`, and each
can hold a window. Keys 80 and up are pulse-only: they still carry a payload
through `AnimEventOutput`, but they never claim a mask bit and can't be
tested with `AnimEventMaskKeys.IsOpen`.

A budget line under the search field reads "n of 64 maskable keys used ·
m pulse-only" and turns amber once the maskable range is full.

- **New** mints the lowest free maskable key. Once all 64 maskable keys are
  taken, it mints a pulse-only key instead — you never get a hard stop.
- **Refresh** rescans clips, cutscenes and profiles for key usage.
- Right-click a row for:
  - **Rename** — inline, in place.
  - **Delete** — confirms with a count of where the key is used; a key that
    is actually in use asks a second time before it goes.
  - **Generate Constants** — rewrites the event constants file from the
    current registry.
  - **Merge into…** — moves every marker using this key onto another key and
    removes this one. This acts on the project registry, not one clip.

## Event fields

The middle column describes whichever key is selected:

- **Name** and **Description**.
- **Default window frames** — the window length new markers for this key
  start with.
- The payload schema: **Int param label**, **Int value names** (comma
  separated), **Float param label**, **Float unit**.
- A **marker preview** showing how a marker carrying this key's payload
  fields will actually look on a track.
- A **preview clip**, played while you scrub the field values.

## Used by

The right column shows every place the selected key is actually used, as
three boxed groups: **Clips**, **Cutscenes** and **Profiles**, each labelled
with a count.

Each group lists one row per asset, two lines each:

- The asset's name.
- Where the key fires on it — `@0.35, 0.60` (normalized time) for a clip,
  seconds for a cutscene, `Layer ▸ animation` for a profile's ragdoll
  events.

Click a row to ping the asset in the Project window. Its **Open** button (the
link icon) jumps straight to the key: the clip opens in the Clip Editor, the
cutscene opens in the Cutscene tab, the profile opens in the Actor Editor.

This column refreshes when you select a different key, and again shortly
after clips, cutscenes or profiles change elsewhere in the toolkit.

## Reading events in your systems

The Events tab only edits data — it never delivers events to gameplay code.
Delivery happens through the per-actor `AnimEventOutput` buffer and the
`AnimEventBufferApi` helper; see `animation-events.md` for the full read
pattern, including how to scan a buffer for every occurrence of one key in a
frame.
