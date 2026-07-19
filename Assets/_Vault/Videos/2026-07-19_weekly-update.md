---
tags: [video, devlog, draft]
related: "[[Memories/Marketing/Strategy]]"
source: commits d86b402..2827675 (2026-07-05 → 2026-07-15)
---

# Every citizen gets their own look — and stops costing me frames when you look away

**Format:** long-form, target 8–12 min · **Fulfils:** standalone weekly update (not one of the numbered devlog slots)

Three stories this fortnight: the character recolor pipeline (one grayscale texture set → endless character variants), the corpse ragdoll rework (real 3D flight), and camera-visibility culling (off-screen characters stop doing presentation work but keep living their lives).

---

## Cold open (0:00–0:20)

[VO — Play mode, DOTSTestScene: kill a unit with a launch-force attack; corpse arcs through the air, limbs flailing, lands and settles]

> This corpse is flying through real 3D space, every limb is a little pendulum, and the moment it lands off screen — my game stops spending a single frame rendering it. Let me show you two weeks of work.

## Hook & context (0:20–1:20)

[FACECAM]
- Quick reset for new viewers: solo dev, Stitch Punk, 2.5D necromancy RTS on Unity DOTS
- This update is three things: characters got a full recolor pipeline, death got proper physics, and rendering got a lot cheaper
- The honest thread connecting them: I want *hundreds* of characters on screen who each look different — that forces you to solve art variety AND performance at the same time

---

## Story 1 — One texture, every character (1:20–4:30)

### The result first

[VO — Editor UI, Inspector on a UnitPartSO + a ColorPaletteSO asset: flip through palette entries, show the `alternative` color fields]

> Every body part in the game — hair, skin, clothes — is now a grayscale mask that gets colored at runtime from palette assets. Same texture, different palette, different citizen. And every palette entry can carry an *alternative* color — that's how zombification works: same sprite, the skin entry just swaps to its rotten variant.

### How it works

[VO — Code, PackedChannelRecolor.hlsl: the header comment block explaining R/G/B zones]

> The trick is channel packing. One texture, four channels doing four jobs: red is the base fill, green is a detail layer composited on top — think a bloody mouth — blue is a third layer, and alpha is just alpha. Each layer gets its own color input, and the color's *alpha* is that layer's blend strength — so optional details fade in per character without extra texture memory.

[VO — Editor UI, Shader Graph with the Packed Channel Recolor node wired in]

> These are custom Shader Graph nodes written in HLSL — Unity 6.5's reflection API lets a single HLSL file show up as a first-class graph node.

[VO — Code, PackedChannelSwitch.hlsl: the "Built for hair-under-hats" comment]

> My favorite dumb problem this week: hair poking through hats. The fix is a two-variant packed sprite — the red/green channel pair is the normal hair, blue/alpha is the *same hair reshaped to hug the head*. Put a hat on, and equipment code flips one per-instance float, and the silhouette swaps. Same texture slice, same color.

(cut if long)
[VO — Editor UI, a per-instance material property (Hybrid Per Instance _BaseColor) on two units with different tints in Play mode]

> All the colors are per-instance GPU properties, so a hundred differently-colored citizens still batch together.

### The tool that feeds it

[VO — Editor UI, Window ▸ Stitch Punk ▸ Texture Channel Packer: drag two grayscale textures in, wire channels into the Pack Output node, press Bake]

> Packing channels by hand in an image editor gets old immediately, so I built a node-graph editor tool for it. Drag grayscale textures onto the canvas, each exposes its channels as ports, wire them into the output, bake. It overwrites the PNG in place so the texture keeps its GUID and every material reference survives. Recipes save the whole graph — repaint a source, repack in one click.

[FACECAM]
- Why build a tool instead of just packing in Photoshop/Krita: iteration count — every part × every variant
- This is the unglamorous 50% of solo dev: tools nobody sees that make the visible stuff possible

---

## Story 2 — Corpses that fly (4:30–6:00)

[VO — Play mode, DOTSTestScene: several kills in a row, different launch directions; include one corpse landing on a ledge or prop if available]

> Death got a rework. Ragdolls used to be a 2D effect — now a corpse gets a real 3D launch velocity, raycasts against the actual collision world for ground height — so ledges and props work — and bounces off walls.

[VO — Code, Ragdoll2DSystem.cs: the ①FLIGHT ②FLAIL ③SETTLE summary comment at the top]

> The whole thing is three phases in one Burst job: flight integrates the launch, flail treats every joint as a one-segment pendulum swinging in the character's plane — gravity plus the pseudo-forces from the body's own motion — and settle eases each limb into an authored landing pose, so corpses land looking *composed*, not like spilled spaghetti. While it's airborne, the per-attack spin can wind extra turns and it settles to the nearest full turn.

(cut if long)
[VO — Play mode, slow-mo or repeated kill: focus on limbs during flight]

> Once every angle goes quiet the corpse sleeps — all the dynamics skip and it's basically free.

---

## Story 3 — Stop rendering what nobody sees (6:00–9:00)

### The result

[VO — Play mode, DOTSTestScene with the Entities Hierarchy open: pan the camera away from a group of units, show their CameraVisible checkbox flipping off; pan back, they're mid-walk-cycle]

> New system: every character rig now knows whether it's on camera. Off screen, all the presentation work stops — animation sampling, pose writes, billboarding, texture-index pushes. Pan back, and they're mid-stride, exactly where they should be. No T-pose, no snap.

### How it works

[VO — Code, CameraVisibilityComponents.cs: the CameraVisible tag + the "HARD RULE" comment]

> The mechanism is one empty component — an enableable tag called CameraVisible. In DOTS, enableable components let whole chunks of entities get skipped by a query without moving any memory around. Every presentation job just adds a filter on this tag and the culling is free.

[VO — Code, CameraVisibilitySystem.cs: the ENABLE_PADDING / DISABLE_PADDING consts and the hysteresis comment]

> Two details that matter. First, hysteresis: you become visible five units outside the screen radius, but only go *in*visible ten units out. That dead band stops units at the screen edge from flickering on and off every frame.

[VO — Editor UI, inspect the CameraView singleton while zooming the camera out; viewRadius grows]

> Second, the view radius isn't a magic number — it's computed every frame by firing rays through the four corners of the viewport at the ground plane. Zoom out, radius grows, more units stay animated. Clamped, so a near-horizontal camera can't declare the entire map visible.

[VO — Code or Editor UI: AnimationLayer time still advancing on an off-screen unit's root entity]

> And one deliberate non-optimization: the animation *clock* keeps ticking off screen — only the sampling stops. That's why units re-enter view mid-cycle instead of frozen in the pose they left with.

[FACECAM — the honest segment]
- The rule I wrote in the project docs in all caps: this tag is presentation-only. AI, movement, combat NEVER filter on it — the world must keep running off screen, otherwise behavior becomes camera-dependent and you get unfixable heisenbugs
- The bug that almost shipped: on a unit's spawn frame, its body-part list still points at the *prefab's* parts. Write visibility through those stale references and you permanently corrupt the prefab — every future spawn comes out broken. The job now refuses to touch anything tagged as a prefab
- Bonus from the same system: spawners now re-roll spawn positions until they land off screen — units stop popping into existence in front of you

---

## Wrap & what's next (9:00–end)

[FACECAM]
- The theme: everything this fortnight serves the same goal — huge crowds of characters that each look distinct and cost almost nothing
- Next up: units are getting a sound system (footsteps, combat, reactions), then daily schedules and waypoints — citizens with actual routines
- Low-key CTA: if DOTS internals are your thing, subscribe — this whole project is basically a DOTS field journal

[VO — Play mode: last shot, a crowd of citizens going about their business, camera slowly pans away]

> The citizens keep living their lives whether you're watching or not. Now, literally.

---

## Shot list

Capture order groups by source so it's one Editor session. **Note:** the camera-visibility verify pass (`Tasks/Verification/verify-camera-visibility.md`) hasn't been run yet — do at least the compile + tag-plumbing checks before recording Story 3 footage.

**Play mode — DOTSTestScene**
- [ ] Kill with launch-force attack: corpse arcs, flails, lands, settles (cold open + Story 2 — record 5–6 kills, varied directions)
- [ ] Corpse landing on a ledge/prop (if a ledge exists in the test scene)
- [ ] Two units with different per-instance tints side by side
- [ ] Camera pans away from a unit group / back again — units resume mid-walk-cycle (Story 3 result)
- [ ] Closing shot: crowd going about their business, slow pan away

**Editor UI**
- [ ] Inspector: a `UnitPartSO` + a `ColorPaletteSO` with `alternative` color entries visible
- [ ] Shader Graph: the Packed Channel Recolor node wired into the character shader
- [ ] Texture Channel Packer (Window ▸ Stitch Punk ▸ Texture Channel Packer): drag sources, wire, Bake
- [ ] Entities Hierarchy during Play mode: `CameraVisible` flipping on a rig root + its parts as the camera moves
- [ ] `CameraView` singleton inspector while zooming out — viewRadius growing
- [ ] Off-screen unit's root entity: `AnimationLayer` time still advancing

**Code / IDE**
- [ ] `PackedChannelRecolor.hlsl` — the header comment (R/G/B zones)
- [ ] `PackedChannelSwitch.hlsl` — the "Built for hair-under-hats" comment
- [ ] `Ragdoll2DSystem.cs` — the ①②③ FLIGHT/FLAIL/SETTLE summary comment
- [ ] `CameraVisibilityComponents.cs` — the tag + HARD RULE comment
- [ ] `CameraVisibilitySystem.cs` — the ENABLE_PADDING/DISABLE_PADDING consts, and the prefab-guard block

**Art / vault**
- [ ] (optional) Character design sheet still from the vault, if one shows palette variants — check `_Vault/Spencer/Art_Assets.md` for the current asset locations

---

## Shorts cuts

| Segment | Clip hook | Platform notes |
|---|---|---|
| Cold open + Story 2 kills | "I spent a week making corpses fly correctly" | Most visual clip — lead with a launch, no context needed. TikTok/Shorts |
| Story 1 recolor result | "Every character in my game is ONE grayscale texture" | Before/after palette swap is the money shot. Twitter/X + Shorts |
| Hair-under-hats | "The dumbest problem in gamedev: hair clipping through hats" | Relatable dev-problem hook; show the B/A channel swap. TikTok |
| Story 3 result | "My NPCs stop rendering when you look away (but keep living)" | Entities Hierarchy checkbox flip is oddly satisfying. Shorts, #unitydots |
