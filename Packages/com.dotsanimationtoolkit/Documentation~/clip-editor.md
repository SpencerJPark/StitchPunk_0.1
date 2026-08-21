# The Clip Editor

**Window ▸ DOTS Animation Toolkit ▸ Clip Editor**

One window for authoring animation against a rig, whatever technique that rig
uses. Cutout parts driven by transform tracks, flipbook indices, and bone tracks
destined for a VAT bake are all edited here, on one timeline, scrubbed together —
that composition is the thing the toolkit exists for.

This page is the reference for the window itself. For the workflow around it,
see [`cutout-characters.md`](cutout-characters.md) or
[`rigged-characters.md`](rigged-characters.md).

---

## What the window gives you

- **The rig hierarchy is the bone picker.** Assign your rigged prefab to the toolbar's **Rig** field and its transforms appear as a tree in the left column. Select a bone there and the inspector offers **Add Bone Track** for exactly that bone — no typing, so the "name resolved to nothing and the bake froze it at rest" failure cannot happen. Bones the selected clip already animates are shown in bold. With no rig assigned you can still type a name, which is the only option a cutout set has.
- **Click the model to select it.** Clicking in the viewport picks the object under the cursor and selects it in the tree; selecting in the tree outlines it in the viewport. It picks the *child* under the cursor, not the prefab root, so a hand or a prop is one click away. Selection follows the pose as you scrub.
  - **Alt- or shift-click cycles** through everything stacked under the cursor, nearest first. Repeat it to walk backwards through overlapping parts; an ordinary click returns to the nearest.
  - **Bones are clickable even where there is no geometry.** Every joint of a `SkinnedMeshRenderer` gets an octahedral handle, linked to its parent so the skeleton reads as a skeleton, and the handle is the click target. Joints are offered *ahead* of the mesh around them — a bone sits inside the thing it deforms, so ordering strictly by depth would make it impossible to click. The mesh underneath is still reachable by cycling.
  - Dragging orbits, as before. A click only selects if the pointer did not travel, so an orbit never changes the selection.
- **Keying.** Select a part and its transform is always on screen, updating as you scrub. The field border says which of three things you are looking at: a stored key, a sampled in-between value, or a change you have made but not keyed. **Auto Key** writes edits straight into a key at the playhead; with it off, press **Key** to keep one.
- **Gizmos.** W/E/R give move, rotate and scale handles on the selected part in the viewport. They write the same values the numeric fields do — one code path — and commit a key on release when Auto Key is on. Rotate is a single Z ring and scale is XY only, because that is exactly what a cutout part's data has.
- **The dopesheet.** Expand a track to see its channels as separate rows. Drag keys horizontally to retime; drag across empty space to box-select; a key dragged past a neighbour reorders rather than stopping. Selecting a key exposes its easing as a curve: pick a preset — Linear (the default), Hold, Ease In, Ease Out, Ease In Out, Smooth, Snap — and drag the curve's handles to shape it from there, which turns the preset into a custom Bézier. Every shape is plotted through the same function the runtime evaluates, so what the curve shows is what plays.
- **Focus.** Selecting a part filters the timeline to that part's tracks — the way to read a busy clip. The status line names what is shown and how many rows are hidden; deselect to bring them back. Event rows always stay visible, because they belong to the clip rather than to any one part.
- **Several parts at once.** Ctrl- or shift-click in the hierarchy to select more. The timeline shows all of their tracks together, and the inspector gives each part its own labelled block — its own live transform, its own flipbook indices — so there is never a question of whose numbers are whose. One block is marked **(active)**: the one the viewport gizmo and outline are on, which can only be in one place.
- **Flipbook indices step on their keys.** A frame index does not interpolate: the key at or before the playhead is what you see, and it holds until the next key's own time. Scrub through a key and the number changes exactly there, not halfway to the next one.
- **Parts start where the prefab puts them.** Assign the prefab in the toolbar's **Rig** field and each rig target picks up the position, rotation and scale of the prefab transform with the same name, measured relative to the prefab root. That is the part's rest pose, and a clip animates *from* it — position and rotation add to it, scale multiplies it — which is exactly how the runtime composes, so what you preview is what plays. A target with no matching transform in the prefab falls back to the origin at unit scale.
- **The space is centred on 0,0,0, and squares are one unit.** Two grids: a backdrop in the XY plane to measure a flat rig against, and a floor in the XZ plane so a 3D prop or vehicle has something to stand on. Each square is one world unit — the height of Unity's default cube — so on a character running about two units tall a square reads directly as half its height. The origin is drawn as three short axis stubs, X red, Y green, Z blue. The rig is spawned there, so a character authored standing on the floor reads as standing on the floor.
- **The camera frames the rig, not the origin.** It opens aimed at the middle of the rig and far enough back to hold all of it, sized from the rig's own bounds. Placement and framing are separate: the rig sits at 0,0,0, but aiming there would look at the ground between a character's feet. Framing happens once when the rig or the loaded prefab changes — after that the camera is yours. Double-click the viewport to reframe.
- **Retiming.** Change `duration` and every key moves with it — times are normalized, so a re-time never moves a key relative to the clip.
- **Events.** Place `EventMarker`s on the timeline — footfalls, hit frames, VFX triggers. These surface at runtime in the actor's `AnimEventOutput` buffer.
- **Layer and blend defaults.** `defaultBlendIn`/`defaultBlendOut` set how the clip crossfades in and out.
- **Sockets.** Placed, tracked and previewed here — see [Sockets](#sockets) below.
- **Validation.** The toolbar badge runs the same `ClipValidation` the bake runs, so an error you see here is the error a bake would throw, with the same rule code.
- **Scrubbing the composite.** The preview poses through `ClipSampler` — the runtime's own functions — so what you scrub is what plays.

---

## Sockets

A socket is an attachment point: a pose the rig exposes so a game can hang something off it — a weapon in a hand, an effect at a fingertip. They live on the **rig**, so every clip in the set sees the same ones, and they are placed in the Clip Editor where you can scrub the animation while you tune them.

### Placing one

**+ Socket** in the hierarchy header creates a socket bound to whatever is selected — pick the hand first and it comes out already following the hand. Socket rows are listed after the rig's parts and labelled with what they follow:

```
RightHand Socket  →  RightHand
Blade Tip         →  Bone_Weapon_02   (unresolved)
```

That `(unresolved)` mark is the point. A binding that matches nothing does not error — it resolves to the actor's origin, and you find out in play mode with a sword lying at the character's feet.

Select a socket to get its inspector: what it follows (a **dropdown**, not a text field — typing is how you get an unresolved binding), the playback layer for a bone socket, and the offset. **W** and **E** give you move and rotate gizmos in the viewport; the result is stored as an offset in the followed part's space, so it stays put as the rig moves.

### Two kinds, and why the difference matters

| Mode | Follows | Motion comes from |
|---|---|---|
| **Rig Target** | A part this rig declares | The part's own transform, resolved live every frame — **nothing to bake** |
| **Bone** | A bone of the imported skeleton | The **VAT bake**, which samples the bone and stores its motion |

That asymmetry is the thing to remember. A bone socket follows a bone that exists at run time only as texels in a texture, so its motion has to be captured at bake time. Until it has been, it resolves to the actor's origin — and the socket inspector says so rather than leaving you to discover it. Re-run **Window ▸ DOTS Animation Toolkit ▸ VAT Bake**, and check the Console for unresolved bone names while you're there.

Both kinds are drawn in the preview and both track as you scrub.

### Seeing it work

Give a socket a **Preview Attachment** — any prefab — and the preview hangs it off the socket. Now "does the sword sit in the hand through the whole swing" is a question you answer by dragging the playhead rather than by entering play mode. Click the attachment to select its socket.

This is an authoring aid and nothing more: it is editor-only, it cannot pull the prefab into a player build, and nothing reads it at run time. What a game actually attaches is the game's decision, made with `SocketAttachmentAuthoring` on a real entity.

---

## Editing the rig itself

The Clip Editor animates a rig. It does not restructure one — parenting, adding parts and moving meshes happen in Unity's prefab mode, which already handles them correctly. What the window provides is a short path there and an honest account of what changed when you come back.

### Getting into prefab mode

- **Edit Prefab** in the hierarchy header opens the loaded prefab.
- **Right-click any row** for *Open Prefab Here* (opens with that object selected and framed), *Ping in Project*, and *Select in Scene*.
- **Double-click a row** does the same as *Open Prefab Here*.

It is a mode switch, not a window arrangement. The Clip Editor docks beside the Scene view, so entering prefab mode brings the Scene view and Hierarchy forward and the Clip Editor steps behind on its own; leaving prefab mode brings it back with the playhead and selection where you left them. You never have to move a window.

> If your Clip Editor is currently floating, the first **Edit Prefab** docks it for you, carrying its clip set, playhead and selection across. That happens once.

### Coming back

Saving or closing the stage reloads the preview, rebuilds the tree, and puts the playhead and selection back where they were — selection by name, since the tree's ids are indices into a hierarchy your edit just changed.

Then it tells you what no longer binds. **It will not silently drop a track.**

> **What a restructure can and cannot break.** Transform and sprite tracks bind to a rig target's **stable id**, minted once and never derived from a name. Rename a part, reparent it, move it across the hierarchy — none of that touches what those tracks point at. That is what the ids are for.
>
> Three bindings *are* name-based and do break:
>
> | Binding | What breaking costs |
> |---|---|
> | `BoneTrack.boneName` | The track stays authored but bakes nothing |
> | A socket with `mode = Bone` | The attachment bakes at the origin |
> | A rig target's `displayName` | Tracks still play; the preview has no rest pose for that part |

The reconciliation panel lists each one with a dropdown of names that exist, a **Remap** button, and **Delete** for the two track-like kinds. Deleting is confirmed and tells you how many keys go with it. A rig target cannot be deleted here — it carries the id every track binds to, so renaming is the fix and deleting is a rig-asset decision. **Dismiss** hides the panel without changing anything; the next save reports the same findings again.

### Rig Edit mode

If you want to nudge the base setup without leaving the window, toggle **Rig Edit** in the toolbar. It is a mode, not a modifier, and it is impossible to be in it by accident: the toggle is tinted, the viewport gets an orange border, and a banner across the top says what a drag will do. **Auto Key** greys out, and keying is refused outright rather than merely discouraged.

In Rig Edit:

- **Gizmo drags write the prefab's base pose** on release. No keyframes are created.
- **Drag a hierarchy row onto another** to reparent it in the prefab. World position is preserved — you are changing what drives the part, not where it sits.

Both go through Unity's prefab APIs. With a prefab stage open for that asset, edits land in the stage: undoable, visible, saved when you save the stage. With no stage open, the asset is written immediately via `LoadPrefabContents`/`SaveAsPrefabAsset`, which **cannot be undone** — there is no open instance for the undo system to restore. Open the prefab first if you want an undo stack.

---
