# Sharing clips between rigs

**A clip knows nothing about a rig, and a rig knows nothing about a clip.** They are independent
assets. The one place they are paired is an **Actor**, which states a rig and the clip sets played
on it; which part a track drives is worked out at bake, against that actor's rig, and never against
anything stored on a clip or a set.

That inversion is what makes the rest of this page possible:

- **One rig takes several clip sets.** A loadout — `Locomotion` plus `Reactions` plus a
  character-specific `Idles` — is per-actor, so two characters on one rig can carry different
  motion without duplicating the rig.
- **One clip set plays on any rig whose tags partially align.** Only the aligning tracks animate;
  the rest are skipped with a warning, never an error.

**Target tags** are the vocabulary that alignment is expressed in: a blink, an ear twitch, a
react-to-hit pose is rarely something only one character does — it's a role every character with a
face plays the same way.

## Why a track's own target id can't travel

A rig target's stable id is random and minted per rig (`RigTargetDefinition.stableId`), on purpose —
it's what makes renaming, reordering, or moving a target safe. That safety is also exactly why a
track bound by target id can't travel: Character A's `EyeL` and Character B's `EyeL` have completely
different ids, so a track built against A's id resolves to nothing on B.

The bake says so rather than staying silent. An id the actor's rig doesn't declare is **rule T6**: a
warning naming the clip, the track, the id and the rig, and the track is skipped. It is a warning
rather than an error because "wrong id" is not a fact anything can establish — a clip records no rig
to be wrong against, only a rig it does or doesn't line up with.

## Every authored track binds by tag

The tag is a keyed track's identity (amendment A56): creating a Transform or Flipbook track tags
its part when the part has no tag yet — reusing the registry tag named like the part, or minting
one — so every row is born tag-bound. A part that's genuinely character-specific simply keeps an
auto-minted tag no other rig carries, which travels nowhere and costs nothing.

Target-binding (the `tagId == 0` sentinel) still exists in the data model and at bake — a track
from an older asset, or one built in code, resolves by its stored target id, and an id the actor's
rig doesn't declare is a T6 warning-and-skip. The editor just no longer creates that state: such a
row reads `(assign tag)` in the timeline until you give it one.

The track keeps living under the object it was added to either way. The tag only decides which id
the bake resolves against — never where the track appears in the Clip Editor's hierarchy.

## Authoring a shared clip

1. **Map the rig, then tag the parts.** Select a claimed part in the Clip Editor and click the tag
   button at the right edge of its selection heading in the inspector (or use the `RigAsset`
   inspector's Target Tags section). It opens a searchable picker over the project's tag vocabulary —
   filter to find an existing tag, or type a name and choose **Create tag "…"** if this is the first
   rig to need it. Reuse an existing tag whenever the part plays the same role on another rig; that's
   what makes tracks travel later.
2. **Author the clip normally.** Keying, event markers, and everything else on the timeline are
   unaffected by whether a track binds by tag or by target. The toolbar's **Rig** and **Clip Set**
   pickers are independent: swapping the set never swaps the rig, and swapping the rig never empties
   the clip list. The rig is window state — stored on no asset, never baked, and making no claim
   about where the set can play. Point the same set at a second rig to see, right there, which of
   its tracks line up and which the badge reports as skipped.
3. **Move a binding from the timeline when it's wrong.** A row's header reads `tag → part` and both
   halves are pickers: the tag half moves the row's keys to another tag (merging into that tag's
   existing row if one exists), the part half moves the tag onto another part of the open rig —
   a rig edit every clip set sharing the rig sees.
4. **Add the set to an actor.** An `Actor` component names a **Rig** and a list of **Clip Sets**. The
   same `ClipSetAsset` can appear on any number of actors on any number of rigs, with nothing extra
   to configure.

Selection only, never typing: a tag's name is typed in exactly one place — the picker's inline
**Create tag "…"** row, or the registry's own list via the **Edit…** button beside the picker's
search field or **Project Settings → DOTS Animation Toolkit → Target Tags** — and every other
surface only ever picks from what's already there. That's deliberate. A wrong pick from a visible
list is a mistake you can see; a typo that silently resolves to nothing is not.

## Binding several sets to one actor

The sets an actor names are merged into one registry:

- The clip list is the **union** of every set's clips, deduplicated by asset and sorted by clip id.
  A clip registered by two of the bound sets contributes one baked entry and a warning (V11).
- **Two distinct clips sharing an id anywhere in the union is an error** (V05), because one of them
  would be unreachable. This is the one new failure mode a multi-set loadout introduces, and it can
  only happen when two independently authored sets meet on one actor.
- **List order does not matter.** The set list is sorted by set id before anything reads it, so
  dragging the same two sets in the other order produces the same blob and the same dedup key.
- A **starting layer** may name any clip in the union.

## What "shareable" actually promises

- **A rig missing the tag is not an error.** A blink clip tagging `EyeL`/`EyeR` plays on a character
  and is simply skipped, with a warning, on a barrel that has no eyes. One "reactions" clip can cover
  a roster of rigs that genuinely differ in what parts they have. The warning names the clip, the
  track, the tag, and the rig — actionable without opening anything — and it surfaces in the Clip
  Editor's validation badge, not only the bake console, because that's where you're already looking
  while authoring.
- **A rig missing the target id is not an error either** (T6, above). The id-bound half of the same
  leniency, for the same reason: a set applied to a second rig legitimately carries tracks only its
  home rig declares.
- **A tag the *registry* no longer has is an error, not a warning.** That's a different fact from a
  rig lacking the part: a rig without `EyeL` is an ordinary roster fact, but a track pointing at a
  deleted tag is a broken reference no rig can satisfy. The two are reported differently so the
  second never hides inside the noise of the first.
- **A clip shared by more than one clip set that still binds by target id gets a warning.** That's
  the "my shared clip does nothing on the second character" mistake turned into a message at
  authoring time instead of a silent no-op discovered later.

## VAT is the exception

A VAT texture encodes one skinned mesh's vertex motion. It **cannot retarget**, and no rule can make
it — so a set carrying baked VAT textures is pinned in practice to the rig they were baked from.

- The VAT bake panel has its own **Rig** field (seeded from the Clip Editor's, if one is open), and
  stamps the rig it sampled into `VatTextureSetAsset.sourceRigKey`.
- Binding that set to a **different** rig is an error, not a skip. A wrong-mesh VAT is never wanted,
  and the failure it prevents — another character's motion on this character's body — is invisible
  to tests and obvious to a player.
- An actor addresses exactly **one** VAT texture set, so two bound sets each supplying one is also an
  error.
- Transform, sprite and billboard content in the same set still shares normally.

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

- **Runtime set switching.** The registry is one immutable blob per (rig, set-list) bind. Changing an
  actor's loadout while it is alive is not supported today; author the loadout you want as a prefab
  variant.
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
answers; the runtime never asks.

`ClipRegistryBlob.setKey` holds the **bind key**: the rig's stable id XOR-folded with every bound
set's. It is a dedup and diagnostic identity — nothing at run time looks a clip up by it — and it is
what makes many actors on the same rig with the same loadout share one blob.
