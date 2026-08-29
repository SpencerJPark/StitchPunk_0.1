// Copyright (c) 2026 Spencer Park. All rights reserved.

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
    /// project can rename an event, reorder the list, or delete it entirely without invalidating a
    /// single baked clip — the names are a label on a number that already means what it means.
    /// </para>
    /// <para>
    /// <strong>The key is authored, not derived from the name.</strong> Minting it from a hash of
    /// the name — the pattern this package uses for clip and target ids — would be wrong here for
    /// two reasons: the key has to land inside the 64-slot maskable range to be useful, which a hash
    /// cannot promise, and renaming an event would silently repoint every clip that used it. A typed
    /// number that a rename cannot touch is the safer of the two.
    /// </para>
    /// <para>
    /// <strong>A project-scoped instance, auto-created on first use (amendment E6 Task 1, owner
    /// directive 2026-08-23: "I don't want to manually create and wire it — it should just
    /// exist").</strong> <c>VocabularyRegistryProvider</c> reproduces the same
    /// <c>ProjectSettings/</c>-backed, lazily-created contract <c>RagdollPreviewScenery</c> gets for
    /// free from <c>ScriptableSingleton&lt;T&gt;</c> — but hand-rolled behind <c>#if UNITY_EDITOR</c>
    /// rather than inherited, because this type cannot derive from an editor-assembly base class:
    /// <c>ClipValidation</c> (architecture section 3.5) takes an <see cref="AnimEventKeyRegistry"/>
    /// parameter and is documented as having "no editor-assembly dependency" so it keeps compiling
    /// in a player build. There is deliberately no <c>[CreateAssetMenu]</c> any more, so there is no
    /// second, competing instance a person could create by mistake.
    /// </para>
    /// <para>
    /// <strong>The project-scoped instance is the only one</strong> since Phase F: the per-set
    /// <c>ClipSetAsset.eventKeys</c> override is gone (decision D4), because a second source of
    /// truth for the same vocabulary has no remaining reason once the registry carries its own
    /// export/import path.
    /// </para>
    /// </remarks>
    public sealed class AnimEventKeyRegistry : ScriptableObject, IVocabularyRegistry
    {
        /// <summary>The frame rate window durations are displayed and edited at in the Clip Editor.</summary>
        /// <remarks>
        /// Display only. The authored value is always seconds (<see cref="EventMarker.windowSeconds"/>);
        /// changing this rate re-labels existing windows without altering how long any of them lasts,
        /// which is the point of storing seconds in the first place.
        /// </remarks>
        [Min(1f)] public float referenceFrameRate = DefaultReferenceFrameRate;

        /// <summary>The rate assumed when no registry has been created yet.</summary>
        public const float DefaultReferenceFrameRate = 60f;

        /// <summary>The named keys this project uses.</summary>
        public List<AnimEventKeyEntry> entries = new List<AnimEventKeyEntry>();

        /// <summary>
        /// Backing store for <see cref="IVocabularyRegistry.GeneratedConstantsPath"/> — see that
        /// property for why the destination lives with the vocabulary rather than per machine.
        /// </summary>
        public string generatedConstantsPath = string.Empty;

        /// <inheritdoc />
        public string GeneratedConstantsPath
        {
            get { return generatedConstantsPath; }
            set { generatedConstantsPath = value; }
        }


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
        /// The free-text note written against <paramref name="eventKey"/>, or null when the event
        /// has none (or is not in this registry). What the event picker shows under a row's name.
        /// </summary>
        /// <param name="eventKey">The key to look up.</param>
        /// <returns>The entry's description, or null.</returns>
        public string FindDescription(uint eventKey)
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
                    return string.IsNullOrEmpty(entry.description) ? null : entry.description;
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
        
        

        /// <summary>
        /// Appends an event named <paramref name="name"/> holding the lowest key nothing else
        /// claims — falling back to the lowest free pulse-only key above the maskable range once
        /// every maskable slot is taken — and returns the minted key. The one code path every "add
        /// an event" surface goes through, so "another event, please" cannot produce a duplicate or
        /// an unmaskable key by accident. Does not persist; the editor-side caller must.
        /// </summary>
        public uint CreateVocabularyEntry(string name)
        {
            if (entries == null)
            {
                entries = new List<AnimEventKeyEntry>();
            }

            uint freeKey = FindFirstFreeKey();
            if (freeKey == 0u)
            {
                freeKey = AnimEventMaskKeys.LastMaskKey + 1u;
                while (ContainsKey(freeKey))
                {
                    freeKey++;
                }
            }

            entries.Add(new AnimEventKeyEntry { name = name, eventKey = freeKey });
            return freeKey;
        }

        int IVocabularyRegistry.VocabularyEntryCount
        {
            get { return entries != null ? entries.Count : 0; }
        }

        string IVocabularyRegistry.VocabularyEntryName(int entryIndex)
        {
            AnimEventKeyEntry entry = entries[entryIndex];
            return entry != null ? entry.name : null;
        }

        uint IVocabularyRegistry.VocabularyEntryId(int entryIndex)
        {
            AnimEventKeyEntry entry = entries[entryIndex];
            return entry != null ? entry.eventKey : 0u;
        }

        /// <summary>
        /// Forwards to <see cref="ContainsKey"/>, which is the same question under this registry's
        /// own vocabulary: an event's id <em>is</em> its key.
        /// </summary>
        /// <remarks>
        /// Explicit rather than a rename of <see cref="ContainsKey"/>, because that method is public
        /// API this package already ships and the shared interface is a newer, more general name for
        /// it. Forwarding costs a call and keeps both callers reading naturally — event code asks
        /// about a key, vocabulary code asks about an id.
        /// </remarks>
        bool IVocabularyRegistry.ContainsId(uint id)
        {
            return ContainsKey(id);
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

        /// <summary>
        /// Free-text note on what this event is for. Purely documentation — it is the grey wording
        /// under the event's name in the picker's hover card, so it is read at the moment someone
        /// is choosing which event to fire.
        /// </summary>
        [TextArea(1, 3)] public string description = string.Empty;
    }
}
