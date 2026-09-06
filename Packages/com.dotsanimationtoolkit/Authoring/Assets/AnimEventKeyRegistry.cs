// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Names for a project's event keys: the asset that turns <c>eventKey = 17</c> into
    /// <c>ApplyDamage</c> in the Clip Editor. Authoring only — never baked, never read at runtime.
    /// </summary>
    public sealed class AnimEventKeyRegistry : ScriptableObject, IVocabularyRegistry
    {
        [Tooltip("Frame rate window durations are displayed and edited at. Display only — stored durations are always seconds.")]
        [Min(1f)] public float referenceFrameRate = DefaultReferenceFrameRate;

        /// <summary>The rate assumed when no registry has been created yet.</summary>
        public const float DefaultReferenceFrameRate = 60f;

        /// <summary>The named keys this project uses.</summary>
        public List<AnimEventKeyEntry> entries = new List<AnimEventKeyEntry>();

        /// <summary>Backing store for <see cref="IVocabularyRegistry.GeneratedConstantsPath"/>.</summary>
        public string generatedConstantsPath = string.Empty;

        /// <inheritdoc />
        public string GeneratedConstantsPath
        {
            get { return generatedConstantsPath; }
            set { generatedConstantsPath = value; }
        }

        /// <summary>The display name for <paramref name="eventKey"/>, or null when the registry does not name it.</summary>
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

        /// <summary>The free-text note written against <paramref name="eventKey"/>, or null when the event has none.</summary>
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

        /// <summary>The lowest maskable key this registry has not already used, or 0 when all 64 are taken.</summary>
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
        /// Appends an event holding the lowest free maskable key, falling back to the lowest free
        /// pulse-only key above the maskable range once that's exhausted. Does not persist; the
        /// editor-side caller must.
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

        bool IVocabularyRegistry.ContainsId(uint id)
        {
            return ContainsKey(id);
        }
    }

    /// <summary>One named event key in an <see cref="AnimEventKeyRegistry"/>.</summary>
    [Serializable]
    public sealed class AnimEventKeyEntry
    {
        public string name = string.Empty;

        [Tooltip("Keys 16-79 own a bit in AnimEventMask and can hold windows; keys above 79 are pulse-only.")]
        public uint eventKey = AnimEventMaskKeys.FirstMaskKey;

        [Tooltip("Window length in frames given to a marker when first assigned this key. 0 makes it pulse-only by default.")]
        [Min(0)] public int defaultWindowFrames;

        [Tooltip("Free-text note on what this event is for. Shown in the event picker's hover card.")]
        [TextArea(1, 3)] public string description = string.Empty;
    }
}
