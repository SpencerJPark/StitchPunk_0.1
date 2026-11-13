# Verify: Smart Human AI (H0–H5)

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
- [ ] A rock that hits a citizen or zombie makes it react to the thrower. (Since H5 a rock that hits a fellow bandit hurts it; see § H5.)
- [ ] Player throws (aim + attack) still fly. They were silently broken before this pass because items lacked PlayerInteractable.

## H4: tactics (CampLoop, rebake first)
- [ ] Walk right up to a rock-holding bandit (under 3 m). It runs back about 5–6 m, then throws. Several throwers spread out instead of stacking.
- [ ] It backs off at most once every ~4 s. Chasing it doesn't make it run backwards forever.
- [ ] Hurt a bandit below ~35 % and stand back (over 3 m). It runs to a camp bandage by the supply crate and heals.
- [ ] Hurt a bandit below 30 % with two zombies on it and other bandits at the camp. It runs to them, then fights again alongside them.
- [ ] A cowardly hurt bandit still flees as before; it doesn't retreat.

## H5: guns, wind-up, line of fire (CampLoop, rebake first)
- [ ] Every throw and shot is preceded by a visible swell + white flash and a sound; side-stepping during it dodges. (H5-D3 was closed unseen: if a white flash doesn't read on the pale placeholder bodies, change the colour on the `Wind Up Presentation` object in CampLoop_Sub.)
- [ ] A bandit picks up the camp rifle over a rock; the starting gunman (Bandit 1) shoots from range and backs off when you close in.
- [ ] Stand so a bandit is between you and a shooter: a careful shooter side-steps, a careless one shoots its friend.
- [ ] A rock or bullet that hits a fellow bandit hurts it (no fight breaks out yet; that comes with DamageReaction).

## Readability refactor 2026-11-13 (BehaviorExecution shared command context)
- [ ] Bandits still patrol, sit, throw, shoot and heal as before.

## Known gaps
- No throw lead and no face-target command; since H5 throws and shots aim where you stood when the wind-up started (by design, so you can dodge).
- Bullets are instantiated and destroyed, not pooled.
- Zombies still think every frame (H6 sets their interval).
- `maxOccupants` isn't enforced, so more bandits than seats can crowd one spot.
