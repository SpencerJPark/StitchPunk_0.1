---
tags: [design, ai, reference, needs-based, utility-ai]
source: "Game Maker's Toolkit — How The Sims Think (Mark Brown)"
created: 2026-05-23
related: "[[Systems_AI]]"
---

# Design Reference: How The Sims Think

Core lesson: needs-based AI lets characters feel alive in ANY house, situation, or expansion pack — without hand-scripting a single specific rule. The smarts live in the scoring system, not in the agent.

---

## 1. Motives (Needs)

The Sims 1 has eight motives: **Hunger, Hygiene, Fun, Energy, Bladder, Social, Comfort, Room (tidiness)**.
All run from −100 to +100. Their sum is the Sim's overall happiness.

- Decay at slightly different rates, tuned to match a believable human schedule (8 hrs sleep, 3 meals/day).
- Some rates are contextual: Bladder decays faster while eating.
- Maxis carefully tuned these rates — this is a major design lever.

**Stitch Punk equivalent:** `Motivation` buffer with `decayRate` per entry; 22 `MotivationType` values. Same model.

---

## 2. Advertisements (The Key Insight)

> "It's actually the other way around. Instead, all of the objects in the Sim's house contain this data, and will broadcast what they can offer."

Objects advertise what they can offer:
- Bed: "+10 Energy if you sleep on me"
- Toilet: "+20 Bladder if used, +5 Room if cleaned"
- Other Sims: "+Social"

The Sim doesn't know what objects do. **Objects tell the Sim.** This means:
- New objects slot in without changing the Sim's code
- Hundreds of expansion pack objects work automatically
- Object state can change what it advertises (broken fridge only advertises "fix me")

**Stitch Punk equivalent:** `InteractionBlob.restorationAmount` + `satisfiedMotivation` — world objects already advertise. `advertisedDelta` now flows from blob → `ActionOption` → scorer.

---

## 3. Scoring Pipeline

```
For each object in range:
  raw_score = object's advertised reward
  weighted_score = apply_curve(raw_score, sim's current motive level)
  distance_penalty = attenuate by proximity

Pick randomly from top-scoring interactions.
```

The random pick is **intentional and critical** — it prevents robotic predictability and lets the player feel needed.

**Stitch Punk equivalent:** `MotivationScoringSystem` → `ActionSelectionSystem` (top-3 random pick). Same architecture.

---

## 4. Motive Curves (Tuning Intelligence)

Curves are the **biggest tuning lever**. They define urgency vs. satisfaction level.

**Physiological needs (Hunger, Bladder, Energy):**
- Curve drops close to zero when motive is full.
- Spikes dramatically when depleted.
- Hunger when starving outscores everything else → biological priorities win.

**Social/cognitive needs (Fun, Social, Comfort):**
- Increase as the Sim becomes MORE happy (Maslovian).
- You can never have too much fun — the curve stays useful even at high motive levels.
- These only matter once base needs are met.

> "We can never have too much fun." — the curve for Fun never reaches zero.

**Design implication for Stitch Punk:** Configure `AIScoringCurveSO` with steep drop-offs for survival needs (Safety, SelfPreservation) and gentler increasing curves for social/entertainment needs. The shape of the curve IS the personality of the need.

---

## 5. Personality

### Sims 1 approach: score multipliers
Each Sim has meter values for Niceness, Neatness, Playfulness, etc.
A pinball machine and a bookcase both advertise "+Fun" — but a playful Sim multiplies the pinball score, a serious Sim multiplies the bookcase score.
Result: same world, different behavior.

### Sims 3 approach: trait-as-motive
Swap personality meters for **traits** (Neat, Neurotic, Heavy Sleeper, etc.). 5 slots, 60+ traits = ~5 million possible Sims.

**Traits add extra motives to the pile:**
- A Couch Potato needs TV time just like they need sleep.
- Evil Sims get a motive for doing evil actions — the same scoring system handles it.
- Objects advertise to trait-specific motives: "write trolling comment" is advertised only to Evil Sims.

> "Sims are encouraged to enact their unique personalities autonomously — while also juggling their standard, everyday needs."

**Design implication for Stitch Punk:** This is Phase 2 of our personality system. `Personality.socialAffinity` etc. are the Sims 1 multiplier approach. The Sims 3 approach — adding trait-specific motives to the buffer — is more powerful but needs more content. Consider adding `MotivationType.Bookworm`, `MotivationType.Lazy`, etc. as actual decaying needs that unlock specific interactions.

---

## 6. Context Motives (Situational Needs)

Locations and situations **temporarily inject motives**:
- Enter a gym → gain motive "be in gym" → gym equipment advertises to it → leave gym → motive removed.
- Guests at a party → gain "act socially acceptable" motive.
- Restaurant at lunch → lot temporarily gives "eat outside" motive to nearby Sims.
  - Can narrow to Sims with "culinary" trait; discourage "frugal" Sims.
- Medieval Sims: work-related motives during the shift, removed during breaks.

This is pure `contextMultiplier` or direct motive injection — **no rule-writing required**.

**Stitch Punk equivalent:** `MotivationChangeRequest` buffer + `contextMultiplier` field. When a unit enters a zone (factory floor, bar, etc.), inject a temporary motive via `MotivationChangeRequest`. Currently unused but fully scaffolded.

**Concrete ideas for Stitch Punk:**
- Enter the factory → gain `MotivationType.Work` motive → workbench/stations advertise to it
- Near a fire/explosion → gain `MotivationType.Safety` spike → flee behavior fires naturally
- Night shift hours → boost `NightOwl` contextMultiplier → units prefer rest during day

---

## 7. Conversation Rules (Production Rules)

Beyond motives, conversations need authored specificity:
- Input + conditions + output (production rules, ranked by specificity)
- "Tell joke" → if GoodHumour trait → laugh; if relationship is sour → insulted; if repeated 5x → bored.
- Rules can't clash; most specific rule wins.
- Designers wrote thousands of these rules for hundreds of topics.

**Stitch Punk implication:** Our dialogue/narrative system is separate from the AI system. The `TalkActionSystem` handles duration and social score, but the actual dialogue content is authored in `DialogueSequenceSO`. This is correct separation — motives drive WHEN to talk, authored rules drive WHAT is said.

---

## 8. Town-Level Simulation (Nested Needs)

The neighbourhood ALSO has motives:
- Maintain 50/50 gender balance (weights new Sim gender)
- 80% employment rate (forces hirings/firings)
- Restaurant wants ~8 diners at lunch → temporarily gives "eat outside" motive to nearby Sims

Background Sims run at "low detail":
- Each day: score big life changes (job, love, marriage) weighted by traits + relationships
- Designers made **time-of-day charts** for what a Sim's motives probably look like
- When a background Sim becomes foreground (enters player's view), snap motives to the chart

> "When a background Sim is promoted into being a foreground Sim, the system checks the time of day and snaps all their motives to the chart."

**Stitch Punk implication:** Not immediately relevant, but if we ever add a living city simulation (other factions going about their lives), this pattern applies perfectly. World-level needs driving population behavior without simulating every individual.

---

## 9. Storytelling Philosophy

> "Knowing when to hold back. Knowing what NOT to simulate."

**The urinal rule:** Maxis modelled correct bathroom etiquette (maintain one-urinal buffer) — but removed it before launch. Random toilet choice led to funnier, more memorable moments. Rule was *too* correct.

**Ambiguity is a feature:**
- Simlish (fake language) makes conversations ambiguous → players project their own meaning → game becomes personal.
- "If we used actual language, the game would flatten and shrink, and everyone would be having the same experience."
- Ambiguity also hides AI mistakes — players fill in the gaps charitably.

**"Yes, and" principle (from improv):**
- Sims use autonomy to support the player's story, not contradict it.
- Will relieve bladder autonomously; will NOT autonomously quit a job or romance a random Sim.
- If the player directs a Sim to flirt with men, the game infers a preference and maintains it going forward.
- Never let autonomous behavior undo a player-driven narrative beat.

**Stitch Punk implication:** Minions should never autonomously do something that contradicts the player's last order. An `ActionInterruptRequest` from the player is final — the AI waits for a new opportunity, doesn't immediately re-engage. The "yes, and" principle supports the necro-engineer fantasy of commanding obedient constructs.

---

## 10. Quick Reference: Sims AI Layers

| Layer | Mechanism | Stitch Punk Equivalent |
|---|---|---|
| Needs | Motive buffer, decays over time | `Motivation` buffer + `MotivationDecaySystem` |
| Advertisements | Objects broadcast need → delta | `InteractionBlob.restorationAmount` + `advertisedDelta` |
| Scoring | Urgency curve × distance × personality | `MotivationScoringSystem` (need-delta formula) |
| Curves | Per-motive, designer-tuned | `AIScoringCurveSO` per `MotivationType` |
| Personality (S1) | Score multipliers per trait | `Personality.socialAffinity/wanderlust/gluttony` + `contextMultiplier` |
| Personality (S3) | Traits as extra motives | `MotivationType.Bookworm` etc. → future expansion |
| Context motives | Temporary motive injection by location | `MotivationChangeRequest` buffer (scaffolded) |
| Randomness | Pick from top N, not always best | Top-3 random pick in `ActionSelectionSystem` |
| Authored rules | Production rules for conversation specifics | `DialogueSequenceSO` + narrative events |
| Storytelling | "Yes, and" — support player narrative | `ActionInterruptRequest` is final; no autonomous story negation |
