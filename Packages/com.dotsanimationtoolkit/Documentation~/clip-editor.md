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

- **The rig hierarchy is the bone picker.** Pick a `RigAsset` in the toolbar's **Rig** field — window state, written to no asset, and independent of the **Clip Set** field beside it: swapping either never changes the other — and its **Source Prefab**'s transforms appear as a tree in the left column. Select a bone there and the inspector offers **Add Bone Track** for exactly that bone — no typing, so the "name resolved to nothing and the bake froze it at rest" failure cannot happen. Bones the selected clip already animates are shown in bold. A rig with no Source Prefab set yet (or with nothing assigned at all) leaves the tree empty, but you can still type a bone name by hand, which is the only option a cutout set has. Use **New Rig** beside the field to build a rig from a prefab instead of assigning one field at a time.
- **Click the model to select it.** Clicking in the viewport picks the object under the cursor and selects it in the tree; selecting in the tree outlines it in the viewport. It picks the *child* under the cursor, not the prefab root, so a hand or a prop is one click away. Selection follows the pose as you scrub.
  - **Alt- or shift-click cycles** through everything stacked under the cursor, nearest first. Repeat it to walk backwards through overlapping parts; an ordinary click returns to the nearest.
  - **Bones are clickable even where there is no geometry.** Every joint of a `SkinnedMeshRenderer` gets an octahedral handle, linked to its parent so the skeleton reads as a skeleton, and the handle is the click target. Joints are offered *ahead* of the mesh around them — a bone sits inside the thing it deforms, so ordering strictly by depth would make it impossible to click. The mesh underneath is still reachable by cycling.
  - Dragging orbits, as before. A click only selects if the pointer did not travel, so an orbit never changes the selection.
- **The inspector is a component stack.** Select an object and it lists what that object carries on this clip, each in its own foldable block, with **Add Component** underneath.
  - **Every object has a Transform**, keyed or not. It heads the stack from the moment you select the object and has no **✕**, because there is no state in which an object is nowhere — posing is the main way both a cutout part and a skinned bone get animated. The header reads *not keyed* until the first key, which is what creates the track. A part is posed on a transform track and anything else on a bone track, and the stack picks which for you.
  - **The add-ons are Flipbook, Billboard and Socket**, and all three go on anything. Add a **Flipbook** to an ordinary plane in the hierarchy and the rig declares that plane a part — it is named after the node, and from then on the node's row *is* that part, so it does not also appear as a separate row in the rig-target list. If the rig already has a part with the node's name, that one is adopted rather than a second one made.
    - Poses you had already keyed on the node move onto its new transform track. If the part it adopted was already keyed they cannot be merged, so the old bone track is left alone and shown as a second block with a **✕** — better a stack with two transform blocks than a track animating the object where you cannot see it.
  - **Descriptions are on hover.** The Add Component list is names only; hovering one shows a card saying what it does. A kind that cannot go on this object stays listed, dimmed, with the reason on its card — the menu never silently omits what you came for. Hovering the selection heading says what kind of object it is.
  - **Removing takes the track with it.** An empty one goes without a prompt; one with keys asks first, and says how many. Undo brings it back.
  - **Several of a kind where that makes sense.** A part can carry as many flipbook tracks as it has independent feature sets, and as many sockets as it needs; Transform and Billboard are one to an object, because two of either is a validation error whichever wins the bake.
  - **The scope is on the block.** A component with a **rig-wide** badge — Socket and Billboard — is stored on the rig, so an edit there is seen by every clip in the set.
  - **Billboard is the root itself.** Add it to any object and that object faces the viewer, in every clip — the prefab root included, which turns the whole actor. Everything beneath it comes along for free, because everything beneath it is a transform child; a descendant only stops inheriting when it declares a root of its own. Its fields animate how much, and the first edit makes the track. Removing it stops the node billboarding and takes those keys with it. The row's right-click menu has the same pair.
- **Keying.** Select a part and its transform is always on screen, updating as you scrub. The field border says which of three things you are looking at: a stored key, a sampled in-between value, or a change you have made but not keyed. **Auto Key** writes edits straight into a key at the playhead; with it off, press **Key** to keep one.
- **Gizmos.** W/E/R give move, rotate and scale handles on the selected part in the viewport. They write the same values the numeric fields do — one code path — and commit a key on release when Auto Key is on. Rotate is a single Z ring and scale is XY only, because that is exactly what a cutout part's data has.
- **Copy and paste keys across objects.** **Ctrl+C** takes the selected keys, **Ctrl+V** drops them at the playhead on whatever object is selected in the hierarchy, and **Ctrl+D** does both at once. The group lands with its internal spacing intact, so the playhead is where the *earliest* copied key goes and the rest keep their distance behind it.
  - **The destination gets whatever components the keys need.** Paste a flipbook onto a part that has none and it gets one; paste onto a plain hierarchy node and the rig declares it a part first. A track created this way inherits the source track's settings — a flipbook's mode and base frame, a transform's blend op — because a sprite key means nothing without them. A track that was already there keeps its own.
  - **A pose moves between a part and a bone.** They store rotation differently, so the paste converts; the destination decides which form the keys land in.
  - **With nothing selected the keys go back where they came from**, which is what duplicating at the playhead means. Copy from one object with several selected and its animation goes onto all of them; copy from several and they pair up with the selection in order.
  - Both halves say what they did, including how many keys were dropped because a component could not be made.
- **The dopesheet.** Expand a track to see its channels as separate rows. Drag keys horizontally to retime; drag across empty space to box-select; a key dragged past a neighbour reorders rather than stopping. Empty space means anywhere in the key area, including the striped rows under the last track — a clip with three rows still gives you the whole pane to start a band in. Selecting a key shows its object's component stack, then the key's own values. Its easing is a curve: pick a preset — Linear (the default), Hold, Ease In, Ease Out, Ease In Out, Smooth, Snap — and drag the curve's handles to shape it from there, which turns the preset into a custom Bézier. Every shape is plotted through the same function the runtime evaluates, so what the curve shows is what plays.
- **A row is its tag, and the header is where bindings are made.** A transform or flipbook track's header reads `tag → rig part` and both halves are pickers. Click the **tag** to move the row — its keys — to another tag; if a row for that tag already exists the keys merge into it. Click the **part** to choose which part of the open rig wears the tag — that edits the rig, so the previous wearer is untagged and every clip set sharing the rig follows the keys to the new part; the picker's hover card says exactly what a pick displaces before you commit. Click the row's empty background to select every key on the track. Creating a track on an untagged part tags it automatically (a tag named after the part is reused or minted), so every keyed row is born with the tag that names it; a row from an older asset that predates this reads `(assign tag)` until you give it one.
- **Retagging a part brings its animation with it.** Change a part's **Tag** in the inspector's selection heading and every row keyed against its old tag moves onto the new one — across every clip in the open clip set, so a part animated in four clips does not have one follow and three left behind. Where a clip already has a row on the new tag the two merge (the arriving key wins a same-time collision; a flipbook row with different frame settings stays put rather than being retuned), and the whole thing is one undo step. Clip sets other than the open one are not rewritten. This is the opposite of the timeline row's **part** half, which places an existing row's tag on a part and leaves its keys alone — there the row is what you are moving, here the part is.
- **The name column is yours to size.** Drag the strip between the row names and the keys to widen or narrow it; where you leave it is remembered. A name too wide for the column wraps rather than being cut off — the tag keeps the first line and `→ part` reads as its own line beneath it, and the row grows to two lanes so its keys stay beside it.
- **Focus.** Selecting a part filters the timeline to that part's tracks — the way to read a busy clip. The status line names what is shown and how many rows are hidden; deselect to bring them back. Event rows always stay visible, because they belong to the clip rather than to any one part.
- **Several parts at once.** Ctrl- or shift-click in the hierarchy to select more. The timeline shows all of their tracks together, and the inspector gives each part its own stack — its own live transform, its own flipbook indices — so there is never a question of whose numbers are whose. One stack is marked **(active)**: the one the viewport gizmo and outline are on, which can only be in one place.
- **Flipbook indices step on their keys.** A frame index does not interpolate: the key at or before the playhead is what you see, and it holds until the next key's own time. Scrub through a key and the number changes exactly there, not halfway to the next one.
- **Parts start where the prefab puts them.** Pick a rig in the toolbar's **Rig** field, and set that rig's **Source Prefab**, and each rig target picks up the position, rotation and scale of the prefab transform with the same name, measured relative to the prefab root. That is the part's rest pose, and a clip animates *from* it — position and rotation add to it, scale multiplies it — which is exactly how the runtime composes, so what you preview is what plays. A target with no matching transform in the prefab falls back to the origin at unit scale.
- **The space is centred on 0,0,0, and squares are one unit.** Two grids: a backdrop in the XY plane to measure a flat rig against, and a floor in the XZ plane so a 3D prop or vehicle has something to stand on. Each square is one world unit — the height of Unity's default cube — so on a character running about two units tall a square reads directly as half its height. The origin is drawn as three short axis stubs, X red, Y green, Z blue. The rig is spawned there, so a character authored standing on the floor reads as standing on the floor.
- **The camera frames the rig, not the origin.** It opens aimed at the middle of the rig and far enough back to hold all of it, sized from the rig's own bounds. Placement and framing are separate: the rig sits at 0,0,0, but aiming there would look at the ground between a character's feet. Framing happens once when the rig or the loaded prefab changes — after that the camera is yours. Double-click the viewport to reframe.
- **Retiming.** Change `duration` and every key moves with it — times are normalized, so a re-time never moves a key relative to the clip.
- **Events.** Place `EventMarker`s on the timeline — footfalls, hit frames, VFX triggers — by double-clicking an existing lane or pressing **Add Event**, which opens a searchable event picker and places the chosen (or newly created) event at the playhead, selected (works even on a clip with no events yet). The button lives in the status row above the key area, immediately before **Auto Key** — beside Snap and the scale pivot rather than in the transport bar, because it answers what the next edit does, not when it happens. Right-click a lane header for Add marker at playhead, Select all markers, Change event…, and Delete lane. They draw as an amber pin, not a bigger diamond, so an event reads as a different kind of thing from a pose key rather than just a bigger one. Several markers can share one time — a hit frame that fires a sound, damage and a camera shake at once is exactly that — and they draw as a small vertical stack rather than one hiding the rest; a click cycles through the stack, and the inspector always shows whichever one is currently selected. These surface at runtime in the actor's `AnimEventOutput` buffer — see [`animation-events.md`](animation-events.md) for the full authoring and runtime model.
- **Layer and blend defaults.** `defaultBlendIn`/`defaultBlendOut` set how the clip crossfades in and out.
- **Sockets.** Added to the bone or part they follow, then tracked and previewed here — see [Sockets](#sockets) below.
- **Validation.** The toolbar badge runs the same `ClipValidation` the bake runs, so an error you see here is the error a bake would throw, with the same rule code. The badge shows the counts; **click it** to list the findings over a corner of the preview, and click again to put them away. It starts away — a clip set part-way through being built is meant to be invalid, and its errors have no business taking the space you are posing in. Each finding is a button that selects the asset it is about.
- **Bake without leaving.** The toolbar's **VAT Bake** toggle covers the editor with the bake panel — the same one the standalone window shows — and uncovers it again. Nothing is torn down: the playhead, the selection and every split boundary are where you left them, so authoring a clip, baking it and looking at the result is one window rather than three. The clip set you have open is offered to the panel's Clip Set field when it is empty, and left alone once you have chosen one there.
- **Ragdoll.** The toolbar's **Ragdoll** toggle drops the previewed rig under its own physics — ground contact, self-collision — to see whether a pose reads on impact. Turning it off restores the pose exactly. See [Ragdoll](#ragdoll) below.
- **Scrubbing the composite.** The preview poses through `ClipSampler` — the runtime's own functions — so what you scrub is what plays.

---

## Sockets

A socket is an attachment point: a pose the rig exposes so a game can hang something off it — a weapon in a hand, an effect at a fingertip. They live on the **rig**, so every clip in the set sees the same ones, and they are placed in the Clip Editor where you can scrub the animation while you tune them.

### Placing one

**A socket is a component of the thing it follows.** Select the hand — the bone, the part, or any node of the prefab — and add **Socket** from its **Add Component** menu. The source is fixed by where you added it, so there is no binding to type and no way to end up following something you did not mean to.

Its component says what it follows, the playback layer for a bone socket, and the offset. **Move in View** puts the viewport gizmo on its marker; **W** and **E** then move and rotate it, and the result is stored as an offset in the followed part's space, so it stays put as the rig moves. Clicking the socket's marker — or its preview attachment — in the viewport selects the source and puts the gizmo back on that socket.

Because a socket belongs to the rig rather than to the clip, its component carries a **rig-wide** badge: moving it while looking at one animation moves it in all of them.

### Finding them all

With nothing selected, the inspector lists every socket on the rig, labelled with what it follows:

```
RightHand Socket  →  RightHand
Blade Tip         →  Bone_Weapon_02   (unresolved)
```

That `(unresolved)` mark is the point. A binding that matches nothing does not error — it resolves to the actor's origin, and you find out in play mode with a sword lying at the character's feet. A resolvable socket offers **Select Source**, which jumps to the object carrying it; an unresolved one has no object to live on, so this list is where you rebind or delete it.

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

## Ragdoll

A ragdoll body is a box collider welded to a node — an authored part or an imported skinned bone — with mass, damping and a joint limit measured against its nearest ragdolled ancestor. Like a socket, it lives on the **rig**, so it is placed once and every clip in the set sees it.

### Placing a body

Select a node — a rig target, a bare grouping transform, or a skinned bone — and add **Ragdoll** from its **Add Component** menu. A freshly added body sizes its box from the node's own renderer bounds when it has one, and keeps a unit box otherwise. **Move in View** puts the box handles up in the viewport, live whether or not **Rig Edit** is on — placing a box is rig structure, but it is not a hierarchy edit, the same as a socket.

In the viewport, every body in the rig draws as a wireframe box, the selected one highlighted. Its handles: a centre dot that moves it (drag freely; the box tracks the cursor across the screen plane), six face handles that resize it (drag one face alone, or hold **Shift** to grow both sides at once and keep the centre fixed), and a rotation ring about the body's own local axis in Planar2D, or three rings in Spatial3D.

`Space` — Planar2D or Spatial3D — is shown on every body's block but belongs to the rig, not to the body: half an articulated ragdoll flat and half free in space is not a supported configuration, so it is edited once and every body obeys it together.

### Dropping it

Turning the **Ragdoll** toggle on captures whatever pose is on screen, resolves the rig's bodies against the current preview, and starts simulating — the same fixed-step solver the runtime uses, so a drop here previews the drop a game would show. The playhead freezes while it runs: a ragdoll has no timeline, so scrubbing (or pressing Play) turns the toggle back off first, exactly as if you had clicked it yourself. Turning it off restores the captured pose exactly.

A rig with no ragdoll bodies, or whose bodies resolve against nothing in the current preview, refuses to engage — the status line above the viewport says why rather than dropping a rig with nothing to fall.

**A skinned bone ragdolls here even though it never will at run time.** A VAT actor's skeleton exists only as texels; there is no bone entity for the runtime to move, so a bone-addressed body plays its baked clip in the game regardless of `RagdollActor`. It authors cleanly and previews fully anyway — this is where you judge whether its box and limits are right, on real transforms, even though the payoff is editor-only for that body.

Ground contact and self-collision are always on; the floor sits at y = 0. Project-wide, editor-only test scenery — boxes and ramps to drop the rig onto — lives in **Project Settings**, never on the rig asset, so a shipped rig never carries a test prop.

---

## Editing the rig itself

The Clip Editor animates a rig. It does not restructure one — parenting, adding parts and moving meshes happen in Unity's prefab mode, which already handles them correctly. What the window provides is a short path there and an honest account of what changed when you come back.

### Starting a rig from scratch

**New Rig**, the toggle beside the toolbar's **Rig** field, covers the editor with a panel that builds a `RigAsset` from a prefab instead of assembling one field at a time — and uncovers it again, the same way **VAT Bake** does. Nothing is torn down either way, so a prefab you have already scanned and the nodes you have already ticked survive a trip back to the editor and a return:

1. Assign the **Source Prefab** you want the rig built from.
2. The panel scans it for every renderer-bearing node and lists them as candidates, ticked or not — a disabled renderer or an inactive helper object starts unticked, everything else starts ticked, and nothing is ticked for you that you cannot untick.
3. **Create Rig** asks where to save the asset, creates one rig target per ticked node, and mints every target a fresh, unique stable id. A rig built this way passes validation immediately — it never carries the "every target id is still 0" state a rig built by hand and saved too early can.
4. With the panel's toggle checked, the new rig is loaded into the toolbar's **Rig** field on the way out — the same undoable write a manual pick makes.

Target tags — the vocabulary that lets one target be shared between rigs — are a separate step, added after a rig exists, not part of this flow.

### Getting into prefab mode

- **Edit Prefab** in the toolbar opens the loaded prefab. It is enabled only while the toolbar's **Rig** field holds a rig whose **Source Prefab** is set; with either one missing it says so on hover rather than failing after the press.
- **Right-click any row** for *Open Prefab Here* (opens with that object selected and framed), *Ping in Project*, and *Select in Scene*.
- **Double-click a row** does the same as *Open Prefab Here*.

It is a mode switch, not a window arrangement. The Clip Editor docks beside the Scene view, so entering prefab mode brings the Scene view and Hierarchy forward and the Clip Editor steps behind on its own; leaving prefab mode brings it back with the playhead and selection where you left them. You never have to move a window.

> If your Clip Editor is currently floating, the first **Edit Prefab** docks it for you, carrying its clip set, playhead and selection across. That happens once.

Sharing a tab group is what makes the switch free, and it is also why the Clip Editor's top bar is not on screen while you are in the stage. The Scene view carries a **Clip Editor** overlay for that: one button back to the timeline, one straight to the VAT bake tab, both leaving the prefab stage open behind them. Dismiss it from the Scene view's overlay menu if you would rather use the tab.

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
