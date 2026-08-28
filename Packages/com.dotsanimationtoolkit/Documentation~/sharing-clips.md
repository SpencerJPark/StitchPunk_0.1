# Sharing clips between rigs

A clip set is scoped to one rig, so a face-animation clip set is normally scoped to one character.
But a blink, an ear twitch, or a react-to-hit pose is rarely something only one character does —
it's a role every character with a face plays the same way. **Target tags** let a clip authored once
play on every rig that tags a part the same way, without duplicating the clip per character.

## Why a track's own target id can't do this

A rig target's stable id is random and minted per rig (`RigTargetDefinition.stableId`), on purpose —
it's what makes renaming, reordering, or moving a target safe. That safety is also exactly why a
track bound by target id can never travel: Character A's `EyeL` and Character B's `EyeL` have
completely different ids, so a track built against A's id resolves to nothing on B. Nothing errors —
the part simply doesn't animate, silently.

## The two ways to bind a track

A Transform or Flipbook track binds to **one object or the other**, never both:

| | Target-bound (the default) | Tag-bound |
|---|---|---|
| What it stores | The object's own stable id | A project-wide tag's stable id |
| Travels to another rig? | No | Yes, if that rig tags a part the same way |
| Use for | A part with no role to name — a character-specific prop, a one-off accessory | A part every rig of this kind has, under a name shared across the project — `Jaw`, `EyeL`, `WeaponHand` |

The track keeps living under the object it was added to either way. Choosing a tag only changes
which id the bake resolves against — never where the track appears in the Clip Editor's hierarchy.

## Authoring a shared clip

1. **Map the rig, then tag the parts.** On the rig's hierarchy row in the Clip Editor (or in the
   `RigAsset` inspector's Target Tags section), click the row's tag button. It opens a searchable
   picker over the project's tag vocabulary — filter to find an existing tag, or type a name and
   choose **Create tag "…"** if this is the first rig to need it. Reuse an existing tag whenever the
   part plays the same role on another rig; that's what makes tracks travel later.
2. **Bind a track by tag.** A Transform or Flipbook track's header shows **Target-bound** by default;
   click it to open the same picker and choose a tag instead. The track's label updates to show which
   tag it now travels under.
3. **Author the clip normally.** Keying, event markers, and everything else on the timeline are
   unaffected by whether a track binds by tag or by target.
4. **Reference the clip from another rig's clip set.** `ClipId` is already stable and shared — a
   `ClipAsset` can sit in more than one `ClipSetAsset`'s roster with nothing extra to configure. The
   tag-bound tracks resolve against whichever rig the clip set names; anything target-bound does not
   travel (see the T4 warning below).

Selection only, never typing: a tag's name is typed in exactly one place — the picker's inline
**Create tag "…"** row, or the registry's own inspector via the picker's **Edit tags…** row — and
every other surface only ever picks from what's already there. That's deliberate. A wrong pick from
a visible list is a mistake you can see; a typo that silently resolves to nothing is not.

## What "shareable" actually promises

- **A rig missing the tag is not an error.** A blink clip tagging `EyeL`/`EyeR` plays on a character
  and is simply skipped, with a warning, on a barrel that has no eyes. One "reactions" clip can cover
  a roster of rigs that genuinely differ in what parts they have. The warning names the clip, the
  track, the tag, and the rig — actionable without opening anything — and it surfaces in the Clip
  Editor's validation badge, not only the bake console, because that's where you're already looking
  while authoring.
- **A tag the *registry* no longer has is an error, not a warning.** That's a different fact from a
  rig lacking the part: a rig without `EyeL` is an ordinary roster fact, but a track pointing at a
  deleted tag is a broken reference no rig can satisfy. The two are reported differently so the
  second never hides inside the noise of the first.
- **A clip shared by more than one clip set that still binds by target id gets a warning.** That's
  the "my shared clip does nothing on the second character" mistake turned into a message at
  authoring time instead of a silent no-op discovered later.

## Renaming and deleting tags

Renaming a tag is always safe — every binding stores the tag's id, never its name, so a rename can
never repoint a clip. Deleting one is not: the registry inspector says how many bindings a tag has
before you remove it, rather than letting you discover the count from the console afterward. A
deleted tag's dangling bindings show as errors (see above) and render as `(unresolved 0x1A2B3C4D)`
wherever a name would otherwise appear — the one place a raw number is ever shown, because it's the
only thing left that makes the broken binding findable.

## Generated constants for game code

Downstream code should never carry a hardcoded tag id or type a string at runtime — Burst jobs can't
compare managed strings anyway. The tag registry's inspector has a **Generate Target Tag Constants**
button that writes a `public const uint` file, so game code reads `TargetTags.Jaw` instead of a magic
number. Regenerate after a rename; the constant's own name changes with it, so any code still
referencing the old name fails to compile — loud and located, rather than a silent repoint at run
time. Event names get the same treatment via **Generate Event Name Constants**; see
[`animation-events.md`](animation-events.md).

## What this doesn't solve

- **Layer conventions.** A shared face clip playing on "layer 1" needs every rig to agree what layer
  1 means, and a `RigAsset`'s layer identity is still its list position. Tagging layers the way
  targets are tagged is an obvious follow-on and is out of scope today.
- **Per-character variation.** A shared clip has no per-character offset without additive layers.
  `TrackBlendOp.Additive` exists; whether a shared clip composes correctly over a character-specific
  base underneath it is untested.

## Under the hood

None of this reaches the runtime as an id. The bake (`ClipRegistryBuilder`) resolves every binding —
by target id or by tag — to the same dense index into the clip registry's sorted target list before
anything is written to the blob. Whether a track was bound by tag or by target is a question the bake
answers; the runtime never asks, which is why this feature carries no blob layout change, no new
runtime component, and no shader contract change.
