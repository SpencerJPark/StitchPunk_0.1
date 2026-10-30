---
title: Verify — Trade (weekly orders by carrier pigeon)
status: active
created: 2026-10-30
area: code
---

## Goal

Play one in-game week of trade in `CampLoop`: a pigeon brings a letter, you accept an order, make the units, hand them over at the client's marker, get paid, and see a missed deadline cost reputation. Spec and as-built notes (every place the build departs from the plan): [`Trade_System.md`](Trade_System.md).

What was checked when it was built:
- **Compile / lint:** the offline `devloop gate game` is clean on all 11 game assemblies.
- **Tests:** EditMode `TradeMathTests` plus the group-order and placement conformance tests; PlayMode `TradeRollTests`, `TradeDeliveryTests`, `TradeOrderResponseTests`, `TradeOrderExpiryTests`, `DeliveredUnitDespawnTests` and `TradeSaveTests` (14 in all). Each rule was shown failing with its fix removed.
- **Driven in Play mode through `execute_code`:** week roll and same-seed determinism, the pigeon flying in and landing on the arm target, the letter opening, accept / decline, hand-over payout and reputation, an overdue order failing, a real save and load through a JSON file.
- **Not run:** anything that needs eyes or a real input device. Every box below is unverified.

## Before you start

- [ ] Open **`Assets/Scenes/CampLoop.unity`** and enter Play. (After running tests the Editor is left on a temporary `InitTestScene…`; reload `CampLoop` first.)
- [ ] The debug panel (`DebugTradeMenu`, left side of the screen) shows the week, the seed and the offers, and has **Skip day**, **Skip to next week** and **Re-roll week with seed**. Use it to move time along: a real day is 24 minutes.

## Play checks

### The clock
- [ ] The HUD shows `Week 1 · Mon 06:00` under the resource row and the hour ticks over about once a real minute.
- [ ] Opening the pause menu stops it; closing resumes it.

### The pigeon and the letter
- [ ] A pigeon (a grey ball for now) flies in from off screen to the player and lands on the arm. Where it lands is the `PigeonArmTarget` child of `PlayerUnit.prefab`: move it if it looks wrong.
- [ ] About a second after it lands the letter opens by itself and the world holds still. The mouse cursor appears and **both buttons on a card (Accept, Decline) and Close are clickable**.
- [ ] Keyboard / gamepad: the first Accept is highlighted and Submit presses it; **E** (gamepad North) closes the letter. **Esc opens the pause menu over the letter** (the letter hides underneath); Esc again returns to it.
- [ ] Closing the letter lets time run again and the pigeon flies away and disappears.
- [ ] The HUD **Letter** button reopens the letter later; on foot **L** (gamepad right-stick press) does too. It blinks red only while a pigeon is waiting.
- [ ] Driving the caravan, or mounted at the workstation, when the pigeon arrives: the letter still opens and closing it puts you back where you were.

### Accepting and finding the client
- [ ] Accepting a card turns it into "Accepted — due Thu" (declined / failed cards grey out). The corner list gains a line for it.
- [ ] A yellow pole with text stands at that client's drop-off: client name, `0/2 Brute`, "by Thu". The six drop-offs are at the east, west, south-east and three houses, 30–40 m from the start: **are they in sensible places?** (They are plain objects in the `CampLoop_Sub` subscene named `DropOff_*`; move them, orders go by id.)

### Making and handing over units
- [ ] Summon (or revive) units of the ordered type; bring them near the marker. The marker count climbs (`1/2`, then "2/2 Brute ready, press E"). A unit below the order's health requirement does not count.
- [ ] Press **E at the marker**: the units leave your squad, walk to a spot near it and stand, **Currency goes up by the amount on the card** (the Bellfounders also add Scrap and Electricity), and a toast says `Delivered — +N Currency`. Which gets the press when the caravan is parked beside the marker: the caravan or the marker?
- [ ] With too few units in range the toast says `Need 1 more Brute`, and with a wounded one `(at least 75% health)`; nothing is taken and nothing is paid.
- [ ] Walk or drive 60 m or more away until the delivered units are off screen, then come back: they are gone. (They stay while you can see them.)
- [ ] The marker and the corner line disappear once the order is delivered.

### Deadlines and reputation
- [ ] Accept an order, then **Skip day** past its due day: it turns to Failed, a toast says `Order failed — <client>: -15 reputation`, and the pole goes away.
- [ ] The letter footer lists each recurring client's tier. Deliver for a client a few times: offers get bigger and pay better (the Army's Runners need **Trusted**; use **Re-roll week with seed** to compare the same seed at different reputations).

### Save and load
- [ ] Mid-week, with one order accepted and one unanswered, save from the pause menu, quit, load. The HUD shows the same day and time, the accepted order and its marker are back, reputation is unchanged, and the pigeon returns with the unanswered offer.

## Open decisions for you

- **Marker placement and look:** the drop-offs have no art, only the pole that appears when an order is accepted.
- **Letter art:** the Rive version replaces the placeholder `TradeLetterUI`; `letterSeal` on the clients is unused until then.
- **A "Hand over 2 Brutes" prompt** was left out (no prompt UI exists); the marker text carries it.
- **Gamepad:** Close and Accept work with the pad, and the HUD letter opens with the right-stick press, but nothing here has been tried on a real pad.
