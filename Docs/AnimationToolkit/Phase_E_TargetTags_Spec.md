# Phase E — Target tags and shared clips (Amendment A51): technical spec

**Opened:** 2026-08-23.
**Spec basis:** `Phase_B_Architecture.md` §3.1 (RigAsset), §3.4 (identity scheme), §4.2 (blob structs), §4.3 (lookup contract), §5.2 (RigPartBinding).
**Status:** specified, not built. Blocks the New Rig wizard (§8).

**Product directive.** Clips must be shareable between clip sets. One clip set per character, but a
face animation authored once should play on every character that has a face. This eventually pairs
with animation layers — a shared blink on a face layer over per-character locomotion below it.

---

## 1. Why this cannot work today

`TransformTrack.targetId` is a `uint` — a rig target's stable id — and those ids are minted from
`Guid.NewGuid()`, folded. **Random, and unique per rig.**

So a face clip authored against Character A binds to A's random ids. Character B's rig has entirely
different ones. The clip resolves to nothing, and does so *silently*: an unresolved target simply
does not animate.

**Nothing else is in the way.** Two clip sets can already list the same `ClipAsset` — the set holds
references, and nothing marks a clip as owned. `ClipId` is stable and would be identical in both
registries, which is what you want. The entire blocker is target identity, and nothing else.

### What was tried before

The host project originally used an **enum** for target sharing. Stable against renames, fragile
against everything else: inserting a value shifts every ordinal after it, and §3.4 exists precisely
to replace that scheme. Going back to it is not an option; the failure mode it has is the one this
package was built to remove.

---

## 2. The decision: tags are roles, not identity

A tag registry, project-scoped, modelled on Unity's object tags: add, rename, remove, and reference
the existing ones. You map a rig's parts once, then tag them — some tags new, some reused from other
rigs. Clips bind to tags.

**This does not weaken §3.4, because a tag answers a different question than an id.**

| Question | Answered by | Scope |
|---|---|---|
| *Which slot is this?* | `RigTargetDefinition.stableId` — random, rename-proof | Unique within one rig |
| *What is this slot **for**?* | its tag | Shared across every rig in the project |

Keep both. A target keeps its stable id exactly as now; the tag says what that target represents. It
is only when a tag *replaces* the id that this becomes name-binding — the thing §3.4 forbids.

### The trap, and the precedent that already avoids it

**A clip must never store the tag's name.** Renaming a tag would silently repoint every clip using
it, which is the enum's failure with different packaging. A tag row therefore carries a **stable id
and a renameable label**, and clips bind to the id.

This package already solved exactly this, one level down. `AnimEventKeyRegistry` is a project-wide
vocabulary of `{authored number, renameable label}`, and its own remarks give the reasoning:

> *"Minting it from a hash of the name … would be wrong here … renaming an event would silently
> repoint every clip that used it. A typed number that a rename cannot touch is the safer of the
> two."*

`TargetTagRegistry` is that asset again, for targets instead of events. Follow its shape — including
its decision to stay **authoring-only and never baked**.

---

## 3. The runtime never sees any of this

**This is the finding that makes the change cheap, and it should be verified before anything else is
built.** Clip tracks do not reach the runtime as ids at all: the bake resolves every binding to a
**dense index** into `ClipRegistryBlob.sortedTargetIds`, and `RigPartBinding.targetIndex` is that
position. Whether a track bound by target id or by tag is a question the *bake* answers and the
runtime never asks.

Consequences:

- **No blob layout change**, so no `ContentHashGoldenTests` churn beyond intended re-bakes.
- **No runtime component change**, no shader contract change, no PlayMode archetype change.
- Phase E is an **authoring + bake** change. That is an unusually forgiving footprint for something
  this foundational, and it is the reason to do it now rather than after 1.0.

---

## 4. Data model

### 4.1 `TargetTagRegistry` (new, `Authoring/Assets/`)

**Superseded by Amendment A52** (`Phase_B_Architecture.md`): this was never built as a
`[CreateAssetMenu]` project asset. It is `ProjectSettings/`-scoped and provider-owned, exactly like
`AnimEventKeyRegistry`, and never assigned by hand anywhere — see the amendment for what actually
shipped and why. Rows of:

```
stableId : uint     // minted once, never derived from the name
name     : string   // freely renameable; display only
```

Authoring-only, never baked, never read at runtime — same contract as `AnimEventKeyRegistry`, and
for the same reason: a project must be able to rename, reorder, or delete the registry without
invalidating a single baked clip.

### 4.2 `RigTargetDefinition` gains a tag

```
public uint tagId;   // 0 = untagged
```

This is the "map the rig, then tag the parts" step. Untagged is a legal, ordinary state: a
one-off part on one character needs no role.

### 4.2.1 A tag name is typed in exactly one place (owner directive, 2026-08-23)

**Assigning a tag is selection, never typing.** A name is typed when the tag is *defined*, in the
registry, and nowhere else for the rest of its life. Every other surface — the rig inspector's tag
column, the Clip Editor's track binding — offers the existing tags and nothing but.

This is not convenience, it is the whole safety argument for §6.1's lenient T2. A warning-and-skip
rule combined with free-text entry is how a roster ends up half-animated with nobody noticing;
combined with pure selection, a wrong tag is a wrong *pick* from a visible list, which is a mistake
you can see.

**The control is a searchable dropdown**, because a project with a hundred tags is unusable as a
flat menu:

- A filter field at the top, narrowing a list as you type. Typing filters; it never creates a
  binding by itself.
- **Built in UI Toolkit, reusing `ClipComponentPicker`'s shape** — that class is already an overlay
  panel with rows, hover states and `Open`/`Close`, and generalising it costs far less than a second
  popup mechanism. **`AdvancedDropdown` is not available**: it lives in `UnityEditor.IMGUI.Controls`
  and `Conformance_E` bans IMGUI APIs in editor sources. Do not reach for it.
- **The last row is "Edit tags…"**, opening the registry editor directly. Discovering mid-tagging
  that a role has no tag yet must not mean abandoning the rig and hunting for an asset.

**Optionally, a filter matching nothing may offer "Create tag *<text>*".** This is still one typing
surface — you are *defining* the tag, not re-typing an existing one — and it is the natural moment to
do it. If built, it must guard against near-duplicates case-insensitively, or `Jaw` and `jaw` become
two roles that look identical in every list and match nothing in common.

### 4.2.2 Editing the registry

Two entry points onto the same asset, because the two situations are different:

- **The `TargetTagRegistry` inspector** — the considered pass: rename, reorder, prune.
- **A small editor window from the dropdown's "Edit tags…"** — the in-flow add, one tag, back to
  work. UI Toolkit, like everything else in this package's editor code.

A rename is safe by construction (§2): clips bind to the tag's stable id, so the label can change
freely. **A delete is not** — it produces T3 errors on every clip that used it, which is correct and
loud, but the registry editor should say how many bindings a tag has before removing it, rather than
letting a person discover the count from the console afterwards.

### 4.2.3 Numbers are an implementation detail and must never surface (owner directive, 2026-08-24)

**The owner will only ever reference tags and event names by *name* — in the editor and in
downstream game code. A number must never be something they type, read, or compare.**

This is not in tension with §2's stable ids; it is a statement about *interface* versus *storage*.
The id exists so a rename cannot repoint a clip. It is not something a person should ever meet.

| Layer | What it uses | Why |
|---|---|---|
| Stored in assets | the **id** | A rename must not repoint a clip (§2). Non-negotiable. |
| Every editor surface | the **name** | Pickers, rig rows, timeline lanes, inspector fields, validation messages. |
| Downstream game code | a **generated constant** | `AnimEvents.Footstep`, `TargetTags.Jaw`. |

**Generated constants are how code gets names without paying for strings.** `ClipSetAsset` already
does exactly this — its inspector's *Generate Clip Id Constants* writes a C# file of `public const`
values so game code says `Clips.Walk` rather than a magic number. The tag registry and the event-name
registry must each ship the same generator.

**Why not store or compare the string itself at run time:** event consumers live in Burst jobs, and
Burst cannot compare managed strings. A `uint` compare against a generated constant is the only form
that is both name-shaped in source and legal inside a job. The string never reaches the runtime at
all.

**Renaming a tag deliberately breaks compilation, and that is the desired behaviour.** The generated
constant is renamed with it, so code referencing the old name fails at compile time — loud, located,
and fixed in seconds. Compare that to the alternative this whole design exists to prevent: a silent
repoint that animates the wrong part and is noticed weeks later.

**The single permitted exception is an unresolvable id**, where no name exists to show — a dangling
reference after a tag was deleted (rule T3). Printing `(unresolved 0x1A2B3C4D)` there is correct,
because the number is the only information that survives and it is what makes the dangling row
findable. Everywhere a name *can* be resolved, the name is what is shown.

### 4.3 A track binds by tag **or** by target

A track carries a target id (as now) *or* a tag id. Both resolve to the same dense index at bake, so
there is one mechanism downstream and two ways to express it upstream.

**Why both, rather than migrating everything to tags:** a character-specific track has no role to
name, and forcing one would mean inventing a junk tag per part per character — which makes the
registry useless as a vocabulary. Sharing is opt-in, and the validation rules below make the
non-shareable case visible rather than silent.

---

## 5. Bake resolution

`ClipRegistryBuilder` resolves each track's binding to a dense target index:

1. Track bound by **target id** → today's path, unchanged.
2. Track bound by **tag id** → find the rig target whose `tagId` matches, then take its dense index.
3. Neither resolves → report and skip the track, exactly as an unresolved socket bone name is
   reported today. **Never silently inert.**

Determinism is unaffected: resolution happens before the sort that produces `sortedTargetIds`, and
the sort key is unchanged.

---

## 6. Validation rules

New rows in §3.5's list, as ordinary sequential `ValidationCode` entries (see Phase D's §4 note —
`V-*` labels are drafting shorthand, not codes).

| Rule | Check | Severity |
|---|---|---|
| **T1** | A tag appears at most once per rig. Two targets sharing a tag makes a tag-bound track ambiguous. | Error |
| **T2** | A track bound to a tag no target in this rig carries. **Skipped, not failed** — see §6.1. | Warning |
| **T3** | A tag id that no longer exists in the registry (deleted tag). | Error |
| **T4** | A clip referenced by **more than one clip set** that still binds by target id — it will not travel. | Warning |
| **T5** | Registry tag ids unique and non-zero. | Error |

T4 is the rule that earns this feature: it turns "my shared clip does nothing on the second
character" from a silent mystery into a message at authoring time.

### 6.1 T2 is lenient, and what that costs (owner decision, 2026-08-23)

**A tag-bound track whose tag is absent from this rig is skipped with a warning, not an error.** One
"reactions" clip can then cover a roster whose rigs genuinely differ — a blink tagging `EyeL`/`EyeR`
plays on a character and is simply ignored by a barrel, without either the clip or the barrel being
wrong.

**T3 stays an error, and the distinction is load-bearing.** A tag *missing from a rig* is a rig that
does not have that part — an ordinary, expected fact about a roster. A tag *missing from the
registry* is a dangling reference to something deleted, which is a broken clip whatever rig it meets.
They must not be reported the same way, or the second hides inside the noise of the first.

**The cost, stated plainly: a mistyped or mis-picked tag now degrades quietly.** Under the strict
reading a wrong tag stopped the bake; under this one it produces a warning and an unanimated part,
which is the exact silent-failure shape this whole phase exists to remove. Three mitigations, all
required rather than optional:

- **The warning names all four things** — clip, track, tag name, and rig — so it is actionable
  without opening anything. A warning that says only "unresolved tag" would be worse than useless
  here.
- **It surfaces in the Clip Editor's validation badge**, not just the bake console. The badge is
  where a person is already looking while authoring, and T2 will be the most common finding this
  feature produces.
- **The tag field is a dropdown from the registry, never free text.** Free text would make typos the
  normal case, and a lenient rule plus free entry is how a roster ends up half-animated with nobody
  noticing.

---

## 7. What this does not solve

Both are real and neither blocks Phase E, but they will be the next questions asked.

- **Layer conventions.** A shared face clip playing on "layer 1" requires every rig to agree what
  layer 1 means, and `LayerDefinition`'s identity is still its **list position** (index = priority,
  §3.1). Shared clips imply a shared layer vocabulary. Tagging layers is the obvious follow-on and is
  deliberately out of scope here.
- **Per-character variation.** A shared clip cannot carry a per-character offset without additive
  layers. `TrackBlendOp.Additive` exists; whether a shared clip composes correctly over a
  character-specific base is untested.

---

## 8. Relationship to the New Rig wizard

**The wizard must come after this, not before.** Its job is minting rig assets and their targets, so
building it first bakes in "always mint fresh, always unique" — the precise assumption that makes
sharing impossible. Built after, the wizard's flow becomes: pick a prefab → scan the hierarchy →
create targets → **assign tags from the registry**, reusing existing ones where the part has a role
that already exists.

The wizard also owns the fix for the `RigAsset`-versus-prefab confusion (Phase D10 Task E relabelled
the toolbar field as a stopgap): the rig asset should carry its own source prefab, in a
`#if UNITY_EDITOR` field so it cannot drag prefabs, meshes and materials into a player build — the
guard `SocketDefinition.previewAttachment` already uses.

---

## 9. Build phases

| # | Phase | Deliverable | Depends on |
|---|---|---|---|
| **E0** | Verify §3 | Confirm from `ClipRegistryBuilder` and `RigPartBinding` that binding really is resolved to a dense index at bake and that no blob field carries a target id into the runtime. **If this is wrong, the whole footprint argument collapses and the plan returns to the owner.** | — |
| **E1** | Registry | `TargetTagRegistry` + inspector (add / rename / remove), modelled on `AnimEventKeyRegistry`. Rename must not touch any clip; delete must report how many bindings it will break (§4.2.2). | E0 |
| **E1.5** | The picker | Generalise `ClipComponentPicker` into a reusable searchable overlay, plus the small "Edit tags…" window (§4.2.1, §4.2.2). **UI Toolkit only — no `AdvancedDropdown`.** Landing this before E2 is what keeps the tag field from ever being free text, even briefly. | E1 |
| **E2** | Rig tagging | `RigTargetDefinition.tagId`, rig-inspector tag column using the E1.5 picker, rules T1/T5. | E1.5 |
| **E3** | Track binding | Tracks bind by tag or target id; Clip Editor surfaces which, using the same picker; rules T2/T3/T4. | E2 |
| **E4** | Bake resolution | `ClipRegistryBuilder` resolves both, reports unresolved. Determinism tests extended. | E3 |
| **E5** | Docs + close | A guide for sharing clips, CHANGELOG, Amendment A51 appended to `Phase_B_Architecture.md`, vault note. | E4 |

Then, and only then, the New Rig wizard (§8).

---

## 10. Owner decisions

| # | Question | Decision | Consequence |
|---|---|---|---|
| **Q1** | Should a shared clip be able to bind a tag that only *some* rigs carry? | **Yes — skip with a warning** (2026-08-23). One clip covers a roster of differing rigs; each animates whatever it has. | T2 is a Warning, and the three mitigations in §6.1 become requirements rather than polish. T4 carries more weight, since T2 no longer stops anything. |
