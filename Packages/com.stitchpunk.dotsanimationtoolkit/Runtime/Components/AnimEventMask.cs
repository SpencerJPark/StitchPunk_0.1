// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Entities;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// The set of event windows currently open on this actor, one bit per event key
    /// (architecture section 5.5, amendment A45). Recomputed from scratch every frame by
    /// <c>EventWindowSystem</c>; never accumulated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is the "is it happening now" channel; <see cref="AnimEventOutput"/> is the
    /// "it just happened" channel.</strong> A marker feeds both, and the two answer different
    /// questions. A footstep sound needs to fire exactly once, on the frame the foot lands, and it
    /// needs to know <em>which</em> sound — that is a pulse with a payload, so it reads the buffer.
    /// A damage window needs to be testable on any frame inside it by a collision system that has
    /// no idea when it opened — that is a state, so it reads this mask. Authoring is identical:
    /// one marker, and a <c>windowSeconds</c> of 0 makes it pulse-only.
    /// </para>
    /// <para>
    /// <strong>Stateless by construction.</strong> The mask is a pure function of each layer's
    /// current playback time and its clip's markers, so it is rebuilt every frame rather than being
    /// opened and counted down. That is what makes an interrupt close its windows for free: a Play
    /// command replaces the layer's clip, the next rebuild reads the new clip's markers, and the old
    /// bits are simply not set again. A countdown would have had to be hunted down and cancelled on
    /// every command path, and the one path that got missed would leave a damage window open on an
    /// animation that stopped playing. It also means scrubbing, reverse playback and time-warping
    /// all stay correct with no extra code, because none of them can desynchronise a value that is
    /// never stored.
    /// </para>
    /// <para>
    /// <strong>Enableable, and disabled whenever no window is open</strong> — which is the common
    /// case for most actors on most frames. A consumer written as a normal
    /// <c>IJobEntity</c> over <see cref="AnimEventMask"/> therefore skips whole chunks of idle
    /// actors without writing a single branch.
    /// </para>
    /// </remarks>
    public struct AnimEventMask : IComponentData, IEnableableComponent
    {
        /// <summary>
        /// One bit per maskable event key: bit <c>n</c> is key <c>AnimEventMaskKeys.FirstMaskKey + n</c>.
        /// Test it with <see cref="AnimEventMaskKeys.IsOpen"/> rather than shifting by hand.
        /// </summary>
        public ulong bits;
    }

    /// <summary>
    /// The mapping between an event key and its bit in <see cref="AnimEventMask"/>
    /// (architecture section 5.5, amendment A45).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The mapping is arithmetic, not a lookup table.</strong> Bit <c>n</c> is key
    /// <c>16 + n</c>, so keys 16–79 are maskable and the conversion is a subtract. The alternative
    /// — letting a project assign arbitrary keys to arbitrary bits through a baked table — would
    /// have put a blob dereference and a search in front of every window test, and would have made
    /// the bit a project-global allocation that any two packages could collide on. Here a key
    /// <em>is</em> its bit, so nothing has to agree about anything.
    /// </para>
    /// <para>
    /// <strong>Keys above <see cref="LastMaskKey"/> remain legal.</strong> They simply have no bit,
    /// which makes them pulse-only: they still emit into <see cref="AnimEventOutput"/> with their
    /// payload, they just cannot hold a window open. A project with more than 64 distinct events
    /// puts the ones that need a duration in the low range and the one-shots above it, rather than
    /// running out of events at 64. Validation rule V20 warns when a marker authors a window on a
    /// key that has no bit, since that is the one combination that silently does nothing.
    /// </para>
    /// </remarks>
    [BurstCompile]
    public static class AnimEventMaskKeys
    {
        /// <summary>The lowest key that owns a mask bit; identical to <see cref="ReservedEventKeys.FirstUserKey"/>.</summary>
        public const uint FirstMaskKey = (uint)ReservedEventKeys.FirstUserKey;

        /// <summary>How many keys own a bit — the width of <see cref="AnimEventMask.bits"/>.</summary>
        public const int MaskKeyCount = 64;

        /// <summary>The highest key that owns a mask bit. Keys above this are pulse-only.</summary>
        public const uint LastMaskKey = FirstMaskKey + MaskKeyCount - 1;

        /// <summary>Whether <paramref name="eventKey"/> owns a bit in <see cref="AnimEventMask"/>.</summary>
        /// <param name="eventKey">The key to test.</param>
        /// <returns>True when the key is in the maskable range 16–79.</returns>
        [BurstCompile]
        public static bool IsMaskable(uint eventKey)
        {
            return eventKey >= FirstMaskKey && eventKey <= LastMaskKey;
        }

        /// <summary>
        /// The single-bit value <paramref name="eventKey"/> occupies, or 0 when it owns no bit.
        /// </summary>
        /// <param name="eventKey">The key to convert.</param>
        /// <returns>A mask with exactly one bit set, or 0 for a pulse-only key.</returns>
        [BurstCompile]
        public static ulong BitOf(uint eventKey)
        {
            if (!IsMaskable(eventKey))
            {
                return 0UL;
            }
            return 1UL << (int)(eventKey - FirstMaskKey);
        }

        /// <summary>
        /// Whether the window for <paramref name="eventKey"/> is open on this actor right now.
        /// </summary>
        /// <remarks>
        /// This is the call gameplay code should make. A pulse-only key always answers false, which
        /// is correct rather than merely safe: a key with no bit genuinely never holds a window.
        /// </remarks>
        /// <param name="mask">The actor's event mask.</param>
        /// <param name="eventKey">The key to test.</param>
        /// <returns>True when the key is maskable and its window is currently open.</returns>
        [BurstCompile]
        public static bool IsOpen(in AnimEventMask mask, uint eventKey)
        {
            ulong bit = BitOf(eventKey);
            return bit != 0UL && (mask.bits & bit) != 0UL;
        }

        /// <summary>
        /// Whether any of the keys in <paramref name="keyBits"/> is open — for a consumer that
        /// cares about several keys at once and has folded them together with <see cref="BitOf"/>.
        /// </summary>
        /// <param name="mask">The actor's event mask.</param>
        /// <param name="keyBits">A pre-folded set of bits to test against.</param>
        /// <returns>True when the mask and the given set share at least one bit.</returns>
        [BurstCompile]
        public static bool IsAnyOpen(in AnimEventMask mask, ulong keyBits)
        {
            return (mask.bits & keyBits) != 0UL;
        }
    }
}
