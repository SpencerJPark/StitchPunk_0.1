---
title: Verify Living Humans
status: awaiting play-test
created: 2026-10-20
area: ai
---

# Verify: Living Humans

## Goal
Citizens and bandits read as different people: they talk with emotes over their heads, some drink and get drunk, drunks can brawl or pass out, and none of it gets in the way of gameplay. Scene: `Assets/Scenes/CampLoop.unity`. Re-enter Play mode so the subscene rebakes. Units still have no animations, so the emotes and the movement carry everything.

Already checked live on 2026-10-20 with `execute_code`: personalities differ per bandit; forced drunk units decay at the designed rate and walk slower; a 0.95 drunk passes out with a zzz; a tipsy, short-tempered bandit picked a brawl; emote quads render over heads (capture: `Library/LHCaptures/emotes_camp_close.png`). What is left is the owner's eye.

## Personalities
- [ ] Watch the camp for two minutes. The bandits don't all behave the same: some chat more, some grab rocks first in a fight.
- [ ] Fight the camp. A few bandits break off and run when nearly dead; most fight on.

## Conversations
- [ ] Two humans talking show emotes above their heads, taking turns (one speaks, the other reacts).
- [ ] Friendly pairs show hearts and laughs; a pair with a hothead sometimes ends in angry faces and walks off.

## Drinking
- [ ] Within a few minutes some humans walk to the Bar (the cube and cylinder north of the gramophone) and show a mug.
- [ ] Afterwards they show green bubbles now and then and weave while walking.
- [ ] To force it: in Play mode set a unit's `Intoxication.level` to 0.95 (or see the spec §6 W6). It stands still with a zzz for 15 s.

## Brawls (must never matter to gameplay)
- [ ] A drunk hothead punches a camp-mate. The victim never drops below about a third of its health and never dies.
- [ ] Guards and allies do not join a brawl, and nobody turns on the player because of one.

## Crowd scaling
- [ ] With many humans on screen, fewer emotes show at once and none appear far from the screen centre.

## Art swap
- [ ] Replace `Assets/Art/CrowdLife/EmoteAtlas.png` with your own art (same 4×4 layout, icon order of `EmoteIcon`). Nothing else needs changing.
