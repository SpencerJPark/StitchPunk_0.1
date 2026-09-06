// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Project-wide vocabulary of target tags: the asset that answers "what is this rig target for?",
    /// once, so a face-blink clip authored against one character's <c>EyeL</c> can play on every
    /// other rig that tags a target the same way. Authoring only — never baked, never read at runtime.
    /// </summary>
    public sealed class TargetTagRegistry : ScriptableObject, IVocabularyRegistry
    {
        /// <summary>The tags this project defines.</summary>
        public List<TargetTagEntry> entries = new List<TargetTagEntry>();

        /// <summary>Backing store for <see cref="IVocabularyRegistry.GeneratedConstantsPath"/>.</summary>
        public string generatedConstantsPath = string.Empty;

        /// <inheritdoc />
        public string GeneratedConstantsPath
        {
            get { return generatedConstantsPath; }
            set { generatedConstantsPath = value; }
        }

        /// <summary>The display name for <paramref name="tagId"/>, or null when the registry does not name it.</summary>
        public string FindName(uint tagId)
        {
            if (entries == null)
            {
                return null;
            }
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                TargetTagEntry entry = entries[entryIndex];
                if (entry != null && entry.stableId == tagId)
                {
                    return string.IsNullOrEmpty(entry.name) ? null : entry.name;
                }
            }
            return null;
        }

        /// <summary>Whether any entry already claims <paramref name="tagId"/>.</summary>
        public bool ContainsId(uint tagId)
        {
            if (entries == null)
            {
                return false;
            }
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                TargetTagEntry entry = entries[entryIndex];
                if (entry != null && entry.stableId == tagId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Mints a fresh id that collides with nothing already in this registry.</summary>
        public uint MintTagId()
        {
            uint candidateId = StableIdMinting.NewTargetStableId();
            while (ContainsId(candidateId))
            {
                candidateId = StableIdMinting.NewTargetStableId();
            }
            return candidateId;
        }

        /// <summary>Appends a tag with a freshly minted id and returns it. Does not persist; the editor-side caller must.</summary>
        public uint CreateVocabularyEntry(string name)
        {
            if (entries == null)
            {
                entries = new List<TargetTagEntry>();
            }
            uint newTagId = MintTagId();
            entries.Add(new TargetTagEntry { name = name, stableId = newTagId });
            return newTagId;
        }

        int IVocabularyRegistry.VocabularyEntryCount
        {
            get { return entries != null ? entries.Count : 0; }
        }

        string IVocabularyRegistry.VocabularyEntryName(int entryIndex)
        {
            TargetTagEntry entry = entries[entryIndex];
            return entry != null ? entry.name : null;
        }

        uint IVocabularyRegistry.VocabularyEntryId(int entryIndex)
        {
            TargetTagEntry entry = entries[entryIndex];
            return entry != null ? entry.stableId : 0u;
        }
    }

    /// <summary>One named target tag in a <see cref="TargetTagRegistry"/>.</summary>
    [Serializable]
    public sealed class TargetTagEntry
    {
        /// <summary>How the tag is shown wherever a rig or a track picks one, e.g. <c>EyeL</c>.</summary>
        public string name = string.Empty;

        /// <summary>The id a rig target's <c>tagId</c> or a track's tag binding actually stores. Minted once, never derived from <see cref="name"/> or reassigned by a rename.</summary>
        public uint stableId;
    }
}
