# Verify: Unit Inspector

Spec: [[UnitInspector_System]]. Built 2026-11-06. The data side is checked headless (reader and history system ran against a live bandit; ring tests pass and fail when the capacity is changed). Nothing visual has been looked at.

## Play passes (keep the Game view focused)
- [ ] Press backquote: the debug panel opens on the right with a "Unit inspector" toggle (off). Tick it: an inspector window opens under the panel saying "Click a unit to inspect it."
- [ ] Left-click a bandit: the window fills (Unit, Plan, Why, Needs, History). The click lands on the unit even though the sprite is a billboard; if it misses, say which unit type (spike UN-S2).
- [ ] Overlay (spike UN-S1): a green line from the unit through its waypoints with a cross on the current waypoint and a bigger one at the goal; white ring = sight range, magenta ring = stopping distance; a fading trail behind it. Lines are drawn on top of the world; judge whether they sit in the right place in both the Game and Scene view (GL matrices are the first suspect if they are misplaced).
- [ ] Start a fight with the watched bandit: the History list gains rows ("Idle -> Attack [WinnerChanged]", "[Fled]" when it runs, "[PlayerOrder]" for a player command), newest first.
- [ ] Why table: shows the scored options with ">" on the winner. It is scored only when the unit thinks, so it can say "scored 1.4s ago"; that is expected.
- [ ] Needs: hunger/energy/fun/social/comfort values move over a minute.
- [ ] Watch a unit walk into a wall or a crowd: the nav line turns red when stuck.
- [ ] Click a citizen far outside the loaded tiles (abstract): it shows state Abstract and a blue line to its route end, no live path.
- [ ] Click empty ground: the watched unit clears. Untick the toggle: window and overlay vanish and no history buffer stays on the unit.
- [ ] With the toggle off, confirm nothing changes in frame time (no overlay, no window).

## Known limits (by design, v1)
No behaviour command-sequence view, no camera follow, no multi-unit list, no Editor window. Attack and flee range rings are not drawn (only sight range and stopping distance). The picking click also reaches the game (UN-Q1 default).
