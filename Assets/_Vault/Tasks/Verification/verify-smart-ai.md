# Verify: Smart Human AI (H0–H3)

Scene: `Assets/Scenes/CampLoop.unity`. Re-enter Play mode so the subscene rebakes. Units still have no animations (no profile), so watch movement and the rocks.

## H1: camp life (watch from outside ~15 m)
- [ ] Bandits walk between the 5 camp waypoints instead of standing still.
- [ ] Within a minute or two, some sit at the campfire seats, visit the supply crate, or gather at the gramophone.

## H2: reactions + alarm
- [ ] Walk toward a bandit who is mid-walk or sitting. It turns on you within about a second, without finishing the walk first.
- [ ] When one bandit engages you, the others within ~15 m join, even ones who couldn't see you yet.
- [ ] Run past the leash: bandits still give up and walk home.

## H3: rocks
- [ ] An unarmed bandit in a fight picks up a nearby camp rock (within ~6 m) instead of walking straight at you.
- [ ] Holding a rock at 3–12 m, it throws. The rock flies at you and hits for 20 (more than a punch).
- [ ] Thrown rocks land and can be picked up again by bandits or you.
- [ ] A rock that hits a fellow bandit does nothing; one that hits a citizen or zombie makes it react to the thrower.
- [ ] Player throws (aim + attack) still fly. They were silently broken before this pass because items lacked PlayerInteractable.

## Known gaps
- No throw lead and no face-target command, so throws aim at where you were.
- Zombies still think every frame (H6 sets their interval).
- `maxOccupants` isn't enforced, so more bandits than seats can crowd one spot.
