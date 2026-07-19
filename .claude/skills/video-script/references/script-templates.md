# Script Templates

Two skeletons — long-form (YouTube devlog) and short-form (Reels/TikTok/Shorts) — plus the shot-line format and a worked example. These are shapes, not straitjackets: reorder segments when the material demands it, but keep the mixed-audience arc (casual hook → visible result → technical depth → back to the game).

## Shot-line format

Every piece of footage is called out inline where the narration needs it:

```
[VO — Play mode, DOTSTestScene: citizen at low health breaks off a fight and runs]
[VO — Editor UI, Entities Hierarchy: filter to CitizenBrain, show the entity count]
[VO — Code, FleeAwarenessSystem.cs: the (1−health)×(1−bravery) > 0.35 check, highlighted]
[VO — Art, Assets/_Vault/<path>: character design sheet still]
[FACECAM]
```

Source is always one of: **Play mode**, **Editor UI**, **Code**, **Art**. After the source, say exactly what to capture — the scene + behavior to trigger, the window + what's visible, the file + region, the asset path. A shot Spencer has to figure out is a shot that doesn't get recorded.

At the end of every script, aggregate all shots into a **Shot list** checklist so footage can be captured in one Editor session without re-reading the script.

---

## Long-form template (8–15 min devlog)

```markdown
---
tags: [video, devlog, draft]
related: "[[Memories/Marketing/Strategy]]"
source: <system name or commit range>
---

# <Title — player-facing, curiosity-driven. "My NPCs learned to run for their lives" not "Implementing FleeAwarenessSystem">

**Format:** long-form, target <N> min · **Fulfils:** <Content_Recording entry, or "standalone">

## Cold open (0:00–0:20)
[VO — Play mode, <scene>: the single most striking moment of the feature working]
> One or two lines. The player-facing result, stated plainly. No intro, no "hey guys".

## Hook & context (0:20–1:30)
[FACECAM]
- Who I am / what Stitch Punk is (one beat, for new viewers)
- What this video covers and why I built it
- The honest version: what was broken or missing before

## The result (1:30–3:30)
[VO — Play mode / Editor UI shots]
> Show it working before explaining how. Narration describes what the viewer
> is seeing, with the concrete specifics (real numbers, real behaviors).

## How it works (3:30–8:00) — the technical segment
[VO — Code / Editor UI shots, interleaved]
> The DOTS depth for the dev audience. Explain accessibly — "components of
> data instead of objects with methods" — then earn the jargon. One idea per
> shot. Architecture diagrams can be a vault doc still (Art source).
> Mark deeper dives with (cut if long).

## What went wrong (8:00–9:30)
[FACECAM]
- The honest segment — Strategy.md tone lives here. A real bug, dead end, or trade-off
- What it taught / how it got fixed

## Wrap & what's next (9:30–end)
[FACECAM, cut to Play mode for the last line]
- Connect the feature back to the game loop
- What's coming next (pull from vault Next steps)
- Single CTA, low-key

## Shot list
- [ ] <every shot, aggregated, in capture-friendly order: all Play mode together, all Editor together, etc.>

## Shorts cuts (if requested)
- <segment timestamp> → <platform> — <hook line for the clip>
```

## Short-form template (per clip, under 60–90s, vertical)

```markdown
## Clip <n> — <working title>

**Hook (0–3s)** — on-screen text + first VO line. Must work with sound off.
> Text overlay: "<7 words max, curiosity gap>"
[VO — <source>: <the most visual moment first, not context>]
> "<first spoken line — drops the viewer mid-action>"

**Body (3s–40s)**
[VO — <source>]
> 2–4 short narration lines. One idea only. For technical clips: one concept,
> one payoff ("every one of these 100 citizens is deciding for itself").

**Payoff (last 5–10s)**
[VO — Play mode: the result]
> Land the moment the hook promised. End on motion, not a summary.

**Caption:** <1–2 sentences, honest voice> #indiedev #gamedev #unitydots
```

Batch note: one file, one `## Clip n` section each, shared Shot list at the end. Each clip = one idea. If a clip needs two ideas, it's two clips.

---

## Worked example — one long-form segment

Feature: the flee/bravery system. Note the concrete specifics pulled from code — this is the research bar to hit.

```markdown
## How it works (4:00–7:30)

[VO — Code, FleeAwarenessSystem.cs: the flee option emission]
> So how does a citizen "decide" to run? There's no state machine. Every
> citizen has a buffer of scored options — attack, flee, wander — and every
> frame the highest score wins.

[VO — Editor UI, Inspector on FleeAction.asset: the consideration curves]
> Flee's score comes from two curves: health and a personality stat I'm
> calling bravery. Low health, cowardly citizen — this curve spikes.

[VO — Code, the break-off check, highlighted]
> And there's one special case. Below 30% health, if one-minus-health times
> one-minus-bravery clears 0.35, flee jumps to priority four — which is the
> only thing allowed to break off a fight mid-swing.

[VO — Play mode, DOTSTestScene: brave citizen fights to the death; coward at
 the same health runs. Side-by-side if possible, sequential is fine]
> Same health. Different bravery. Two different citizens.

(cut if long)
[VO — Editor UI, Entities Hierarchy while Play mode runs]
> All of this is Burst-compiled jobs over component data — this decision
> costs about the same for 100 citizens as it does for one.
```

What makes this work: real file names, the real 30% / 0.35 numbers, jargon earned after plain-language setup, footage that all exists, and the emotional beat ("Two different citizens") given to the gameplay shot, not the code shot.
