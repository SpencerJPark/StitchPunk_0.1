# Verify — clip editor timeline (transport, zoom/pan, selection, grab/scale)

Everything below compiles clean and the 494-test EditMode suite is green, but none of it
has been seen working. It is all editor interaction, which I cannot observe — these are the
checks only you can run.

Open **Window ▸ DOTS Animation Toolkit ▸ Clip Editor**, assign a clip set, select a clip.

---

## 1. The two bugs you reported

Both had a different cause than my first attempt assumed, so these are the important ones.

- [ ] **Clip length can be set.** Type a new number into `Length` in the transport bar and press
      Enter. It should take, the frame count readout should follow, and the ruler should redraw.
      *Was broken because:* the transport bar is bound once, while no clip is selected, so its
      Length and FPS fields were disabled at zero and selecting a clip never re-enabled them.
- [ ] **Typing a decimal works.** Type `0.5` — you should be able to type all three characters.
      *Was broken because:* the field reported a value per keystroke, so `0` was clamped up to the
      minimum duration and written back over the rest of the number. Both timing fields now commit
      on blur/Enter only.
- [ ] **Clicking a key shows that key in the inspector**, and it *stays* shown after you release
      the mouse. Click several different keys in turn — the panel should follow each one.
      *Was broken because of two independent things:* the hierarchy tree re-notified its own
      selection during a repaint and the handler took it for a click, and `SortTrackKeys` cleared
      the selection on every pointer-up including the zero-distance one that is a plain click.
- [ ] **Clicking a key on a bone track** still moves the tree selection and the viewport outline
      onto that bone — the fix must not have cost that.

## 2. Transport bar

- [ ] Play / pause, step frame (`<` `>`), jump to start / end all work.
- [ ] Frame and seconds readouts agree, and typing into either moves the playhead.
- [ ] `FPS` change keeps keys where they are on the clip and only changes the frame count.
- [ ] Keys off the new frame grid are marked, and **Quantize Keys** moves them onto it.
- [ ] Loop toggle and speed multiplier behave.
- [ ] `Space`, arrows, `Shift`+arrows, `Home`, `End` work — and do **nothing** while the caret is
      inside any text or numeric field.

## 3. Zoom and pan

- [ ] Zoom slider works and reads back the current zoom.
- [ ] `Ctrl`+scroll over the timeline zooms **toward the cursor**, not the centre.
- [ ] Middle-mouse drag pans, including past the clip start and past its end.
- [ ] Outside the clip is shaded darker, with a warm boundary line at frame 0 and at the end.
- [ ] Keys outside the clip still draw, select and drag.
- [ ] `F` frames the selection, `Shift+F` frames the whole clip, and the two buttons do the same.
- [ ] Zoom and pan survive closing and reopening the window.
- [ ] **No label ghosting.** Zoom in and out repeatedly — second labels on the ruler should not
      accumulate or mis-colour. (Fixed once already; worth re-checking.)

## 4. Selection

- [ ] Click selects one key; shift-click adds; box-select (drag on empty lane space) works, and
      across tracks.
- [ ] `A` selects every key in the clip. `Alt+A` clears the selection.
- [ ] Clicking a **track header name** selects that track's keys; shift-click adds them.

## 5. Grab and scale — the new modal operators

Select a few keys first. These are keyboard-initiated: press the key, then move the mouse
*without holding a button*.

- [ ] `G` starts a move. The readout appears at the right of the transport bar in amber and says
      how many frames. Moving the mouse moves the keys.
- [ ] `S` starts a scale. Readout shows the factor and which pivot it is using.
- [ ] **Pivot dropdown** (transport bar) switches between Playhead / Selection Center / Selection
      Start, defaults to Playhead, and persists across sessions.
- [ ] **Typing a number** during a gesture takes over from the mouse — `G` `1` `0` `Enter` should
      move the selection exactly 10 frames. Backspace edits the number.
- [ ] `Escape` cancels: keys return to where they were, **and no undo entry is left behind**
      (press Ctrl+Z afterwards — it should undo whatever you did *before* the cancelled gesture).
- [ ] `Enter` or a left click confirms. Right click also cancels.
- [ ] A confirmed gesture is **one** Ctrl+Z, not dozens.
- [ ] **Snapping** is on by default; holding `Ctrl` during the gesture disables it live.
- [ ] **Scaling below zero mirrors** the selection about the pivot rather than collapsing it. The
      readout says "(mirrored)". The keys should still be selected afterwards — this is the case
      the sort-remap work exists for, so it is the one most worth a hard look.
- [ ] Interpolation modes survive a scale (a Bezier key should still ease the same way — handles
      are stored in segment space and are meant to stretch with the segment).
- [ ] While a gesture is running, `Space` does **not** start playback and `Backspace` does **not**
      delete keys.

---

## If something is wrong

The useful thing to tell me is *what you did and what happened*, not a diagnosis — twice now the
obvious-looking cause has been the wrong one, and both real causes were somewhere that looked
unrelated to the broken behaviour.

Detail on the design decisions and the two bugs is in
`Docs/AnimationToolkit/Phase_B_Architecture.md`, amendment **A48**.
