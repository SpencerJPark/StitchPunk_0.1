# The Retarget tab

The Retarget tab in the Clip Editor exists for one question sharing a clip across rigs always
raises sooner or later: for *this* clip on *this* rig, which tracks actually land, which ones are
quietly skipped, and which ones are broken outright? Before this tab the only answer was the
validation badge's message list — actionable, but one clip's worth of tracks at a time, and never
laid out against the rig they're being checked on.

## Header

**Clip Set** and **Rig** are the same shared selection every other tab in the window uses — picking
either here changes it everywhere, exactly like Materials or Sprite Sheets. **Clip** is local to
this tab: it lists the clips in the selected set, and choosing one doesn't touch the shared
selection.

## The track table

One row per track in the clip, whichever kind it is — Transform, Sprite, Bone, Billboard — each
resolved against the picked rig:

- **✓ Bound** shows the target it lands on (`Torso`, `Head`, `Hand_L`).
- **● Skipped** is a warning: the tag is real, but no part on this rig wears it, so the track poses
  nothing here. Untagged tracks show Skipped the same way when the rig lacks the id they store.
- **✗ Dangling** is an error on every rig: the tag itself no longer exists in the project's tag
  list.

Bone tracks bind by matching the track's stored bone name against the rig's source prefab
hierarchy, not by tag, so a Bone row reads Bound or Skipped depending on whether that name is
found. Billboard tracks bind by the rig's billboard root and never show a remap option, since
there's no tag to move.

## Remapping

Each Skipped or Dangling row carries a remap menu, listing the rig's tagged parts by their tag
name. Choosing one rewrites that row's tag in this clip only, and it's undoable like any other
edit. If the tag you land on is already carried by another track in the same clip, the two tracks'
keys merge into the one row — the same rule the timeline's own tag picker uses, so nothing about
merging is special to this tab.

For a fix that should apply everywhere a tag is used, the same menu's **Remap in every clip…**
item runs the project-wide tag replace: every clip track, cutscene part track, and rig target
still carrying the old tag moves to the new one in a single undoable step.

## Preview

The panel poses the selected clip on the selected rig, live. A Skipped track poses nothing — that
gap is the point: seeing the hole where a track should move is faster than inferring it from a row
of text. Drag to orbit the camera; double-click, or the **Reset View** control, returns to the
default framing.

## Roster strip

Below the table, a strip lists every rig in the project (the clip set's own rig first) with a
bound/total count and a small coverage bar for the clip currently open. Clicking a rig in the
strip switches the shared Rig selection to it, which re-scores the table and re-poses the preview
against the new rig — the fastest way to sweep one clip across a whole roster and see where it
comes up short.

## What this tab doesn't do

- It doesn't edit rigs. Adding a missing part or tag to a rig happens in the Rigs tab; this tab
  only tells you the gap exists.
- It doesn't guess a match by name similarity. A Skipped or Dangling row stays that way until you
  pick a real tag from the menu — nothing here infers that `Hand_L` probably means `HandLeft`.
- It doesn't remap VAT bone names. VAT is baked per source rig and can't retarget at all (see
  "VAT is the exception" in [`sharing-clips.md`](sharing-clips.md)), so a VAT-baked set isn't
  something this tab can fix by remapping.
