// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Names for a project's event keys (amendment A45): the asset that turns
    /// <c>eventKey = 17</c> into <c>ApplyDamage</c> in the Clip Editor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Authoring only — this asset is never baked and never read at runtime.</strong> The
    /// key/bit relationship is arithmetic (see <see cref="AnimEventMaskKeys"/>), so nothing at
    /// runtime needs a table to interpret a marker. Keeping the registry out of the blob means a
    /// project can rename an event, reorder the list, or delete the asset entirely without
    /// invalidating a single baked clip — the names are a label on a number that already means what
    /// it means. It also means the package ships with no opinion about what events a game has.
    /// </para>
    /// <para>
    /// <strong>The key is authored, not derived from the name.</strong> Minting it from a hash of
    /// the name — the pattern this package uses for clip and target ids — would be wrong here for
    /// two reasons: the key has to land inside the 64-slot maskable range to be useful, which a hash
    /// cannot promise, and renaming an event would silently repoint every clip that used it. A typed
    /// number that a rename cannot touch is the safer of the two.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "NewAnimEventKeys",
        menuName = "DOTS Animation Toolkit/Anim Event Key Registry",
        order = 3)]
    public sealed class AnimEventKeyRegistry : ScriptableObject
    {
        /// <summary>The frame rate window durations are displayed and edited at in the Clip Editor.</summary>
        /// <remarks>
        /// Display only. The authored value is always seconds (<see cref="EventMarker.windowSeconds"/>);
        /// changing this rate re-labels existing windows without altering how long any of them lasts,
        /// which is the point of storing seconds in the first place.
        /// </remarks>
        [Min(1f)] public float referenceFrameRate = DefaultReferenceFrameRate;

        /// <summary>The rate assumed when a clip set has no registry assigned.</summary>
        public const float DefaultReferenceFrameRate = 60f;

        /// <summary>The named keys this project uses.</summary>
        public List<AnimEventKeyEntry> entries = new List<AnimEventKeyEntry>();

        /// <summary>
        /// The display name for <paramref name="eventKey"/>, or null when the registry does not
        /// name it.
        /// </summary>
        /// <param name="eventKey">The key to look up.</param>
        /// <returns>The entry's name, or null.</returns>
        public string FindName(uint eventKey)
        {
            if (entries == null)
            {
                return null;
            }
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                AnimEventKeyEntry entry = entries[entryIndex];
                if (entry != null && entry.eventKey == eventKey)
                {
                    return string.IsNullOrEmpty(entry.name) ? null : entry.name;
                }
            }
            return null;
        }

        /// <summary>
        /// The lowest maskable key this registry has not already used, or 0 when all 64 are taken.
        /// </summary>
        /// <remarks>
        /// Used when adding a row in the inspector, so the common case of "another event, please"
        /// never produces a duplicate key or an unmaskable one by accident.
        /// </remarks>
        /// <returns>An unused key in the maskable range, or 0 when the range is full.</returns>
        public uint FindFirstFreeKey()
        {
            for (uint candidate = AnimEventMaskKeys.FirstMaskKey;
                candidate <= AnimEventMaskKeys.LastMaskKey;
                candidate++)
            {
                if (!ContainsKey(candidate))
                {
                    return candidate;
                }
            }
            return 0u;
        }

        /// <summary>Whether any entry already claims <paramref name="eventKey"/>.</summary>
        /// <param name="eventKey">The key to test.</param>
        /// <returns>True when an entry uses it.</returns>
        public bool ContainsKey(uint eventKey)
        {
            if (entries == null)
            {
                return false;
            }
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                AnimEventKeyEntry entry = entries[entryIndex];
                if (entry != null && entry.eventKey == eventKey)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>One named event key in an <see cref="AnimEventKeyRegistry"/>.</summary>
    [Serializable]
    public sealed class AnimEventKeyEntry
    {
        /// <summary>How the key is shown in the Clip Editor, e.g. <c>ApplyDamage</c>.</summary>
        public string name = string.Empty;

        /// <summary>
        /// The key baked into every marker that uses this event. Keys 16–79 own a bit in
        /// <see cref="AnimEventMask"/> and can hold windows; keys above 79 are pulse-only.
        /// </summary>
        public uint eventKey = AnimEventMaskKeys.FirstMaskKey;

        /// <summary>
        /// The window length, in frames at the registry's reference rate, given to a marker when it
        /// is first assigned this key. 0 makes the event pulse-only by default.
        /// </summary>
        /// <remarks>
        /// A default rather than a rule: an <c>ApplyDamage</c> event that is nearly always a
        /// four-frame window should arrive as one, but a clip that needs six frames just edits the
        /// marker. Nothing re-reads this value after the key is assigned.
        /// </remarks>
        [Min(0)] public int defaultWindowFrames;

        /// <summary>Free-text note shown as the dropdown tooltip; purely documentation.</summary>
        [TextArea(1, 3)] public string description = string.Empty;
    }
}
