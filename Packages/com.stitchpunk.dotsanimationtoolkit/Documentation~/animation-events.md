# Animation events

An event is a marker on a clip's timeline that gameplay can react to: play a
footstep sound, spawn a projectile, apply damage, start an invulnerability
window. You author them on the **Events** lane of the Clip Editor; systems read
them off the actor entity.

Events are deliberately **not** callbacks. Nothing on the animation side calls
into gameplay — the toolkit publishes what happened onto the entity, and your
systems query it in their own group at their own point in the frame. That is
what keeps event handling Burst-compatible and jobifiable.

## The two channels

One authored marker feeds two channels, and they answer different questions.
Picking the wrong one is the most common mistake with this feature, so start
here:

| | **Pulse** | **Window** |
|---|---|---|
| Reads from | `AnimEventOutput` buffer | `AnimEventMask` component |
| Answers | "it just happened" | "it is happening *now*" |
| Lives for | One frame | As long as you authored |
| Carries payload | Yes — `intParam`, `floatParam` | No — just the key |
| Use for | Sounds, spawning a projectile, VFX one-shots, camera shake | Damage/hit frames, invulnerability, parry windows, "is committed" |

The rule of thumb: **if the reacting system runs on the frame the thing happens,
use the pulse. If it has to be able to ask on any frame, use the window.**

A footstep sound is a pulse — it fires once, on the frame the foot lands, and it
needs to know *which* sound (that is the payload). A sword's damage frames are a
window — the collision system has no idea when the swing started and just needs
to ask "is this sword live right now?".

A marker with `windowSeconds = 0` is pulse-only. That is the default, so every
clip authored before windows existed behaves exactly as it did.

## Authoring

On the **Events** lane, double-click to place a marker. Event keys draw larger
than pose keys and in amber, and a translucent bar behind a marker shows how long
its window runs — so a hit frame's duration lines up visibly against the poses
around it.

Selecting a marker gives you:

- **Event** — which event this is. A dropdown of names when the clip set has an
  `AnimEventKeyRegistry`, a raw number when it does not.
- **Window (frames)** — how long the window stays open. **0 makes it
  pulse-only.**
- **Int Param** / **Float Param** — payload delivered on the pulse. Not carried
  by the window.

### Windows are authored in frames and stored in seconds

The field edits in frames because that is how animation is timed, but the value
stored on the marker is seconds. This is on purpose: a literal frame count would
make a 6-frame damage window last 100 ms at 60 fps and 200 ms at 30 fps, so the
same authored attack would connect on a fast machine and miss on a slow one. The
reference rate used for display lives on the registry asset and defaults to 60.
Changing it re-labels existing windows without changing how long any of them
lasts.

## Naming your events

Event keys are `uint`s. Keys 0–15 are reserved by the package (`ClipFinished`,
`ClipResolveFailed`); your events start at **16**.

Create an **Anim Event Key Registry** (`Create ▸ DOTS Animation Toolkit ▸ Anim
Event Key Registry`) and assign it to your clip set to pick events by name in the
Clip Editor instead of by number. Each entry has a name, its key, an optional
default window, and a description shown as the tooltip.

The registry is **authoring-only — it is never baked and never read at runtime.**
The key/bit relationship is arithmetic, so nothing at runtime needs a table to
interpret a marker. That means renaming an event, reordering the list, or
deleting the asset entirely cannot invalidate a single baked clip. It is a label
on a number that already means what it means.

### The 64-key maskable range

`AnimEventMask` is a 64-bit field and the mapping is **bit `n` is key `16 + n`**:

| Keys | Can hold a window? |
|---|---|
| 0–15 | Reserved by the package |
| **16–79** | **Yes** — each owns one mask bit |
| 80+ | No — pulse-only |

Keys above 79 are still perfectly legal, they just cannot hold a window. So a
project with more than 64 distinct events puts the ones that need a duration in
the low range and its one-shots above it, rather than running out of events at
64. Validation rule **V20** warns if you author a window on a key that has no
bit, since that is the one combination that silently does nothing.

## Reading events from a system

### Windows

```csharp
[UpdateInGroup(typeof(MyCombatSystemGroup))]
[BurstCompile]
public partial struct SwordDamageSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ApplySwordDamageJob damageJob = new ApplySwordDamageJob();
        state.Dependency = damageJob.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct ApplySwordDamageJob : IJobEntity
{
    // Not declared WithPresent, so this runs only for actors that currently hold
    // a window — every idle actor's chunk is skipped without a branch.
    private void Execute(in AnimEventMask eventMask, in SwordHitbox hitbox)
    {
        if (!AnimEventMaskKeys.IsOpen(eventMask, GameEventKeys.ApplyDamage))
        {
            return;
        }
        // ... the sword is live this frame.
    }
}
```

Fold several keys together once and test them in one go with
`AnimEventMaskKeys.IsAnyOpen(mask, bits)`, where `bits` is the OR of
`AnimEventMaskKeys.BitOf(key)`.

### Pulses

```csharp
private void Execute(
    in DynamicBuffer<AnimEventOutput> animEvents,
    in FootstepSounds sounds)
{
    for (int eventIndex = 0; eventIndex < animEvents.Length; eventIndex++)
    {
        AnimEventOutput animEvent = animEvents[eventIndex];
        if (animEvent.eventKey != GameEventKeys.Footstep)
        {
            continue;
        }
        // animEvent.intParam / .floatParam carry the payload;
        // .layerIndex and .clip say where it came from.
    }
}
```

Gate on the `AnimEventsPending` enableable the same way, so actors that emitted
nothing cost nothing.

## Timing and ordering

Both channels are produced inside `AnimationToolkitLogicSystemGroup`:
`EventEmissionSystem` writes the pulses, then `EventWindowSystem` rebuilds the
mask.

**Neither is gated on `AnimVisible`.** Events are gameplay, not presentation — an
actor swinging behind the camera lands its hits on schedule.

A consumer ordered **after** `AnimationToolkitSystemGroup` sees this frame's
events. One ordered earlier sees the previous frame's — a documented one-frame
latency, not a bug. Order your combat group after the toolkit group if you need
same-frame reactions.

### Interrupts close windows

The mask is rebuilt from scratch every frame from each layer's current playback
time; nothing is accumulated or counted down. So an interrupt closes its windows
with no cancel path anywhere: a `Play` command swaps the layer's clip, the next
rebuild reads the new clip's markers, and the interrupted swing's damage window
is simply never set again.

**A unit staggered mid-swing therefore deals no damage.** If you want a committed
attack to land regardless, don't reach for the window — make the attack
uninterruptible in your own state machine, which is where that decision belongs.

The same property is why scrubbing in the editor, reverse playback, and PingPong
reflection all report windows correctly: none of them can desynchronise a value
that is never stored.

## Validation rules

| Rule | Severity | Fires when |
|---|---|---|
| V09 | Error | An event uses a key below 16 (reserved by the package) |
| V19 | Error | A marker's window is negative |
| V20 | Warning | A window is authored on a key outside 16–79, where no bit exists to observe it |

## Gotchas

- **A marker at normalized time 0 on a looping clip**: its *pulse* does not fire
  at play start (only on each wrap), but its *window* is open at play start,
  because the playhead is on the marker. This is intentional and is the one place
  the two channels disagree.
- **A window longer than the clip** is not an error. On a loop it is the ordinary
  way to say "open for the whole loop".
- **A window on a Once clip that overruns the end** stays open while the layer is
  parked at the end, and closes when the layer goes inactive.
- **Changing `EventMarker.windowSeconds` changes the blob**, so subscenes need a
  re-bake like any other clip edit.
