// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Project-wide vocabulary of animation names: the asset that turns an actor profile layer's
    /// <c>animationKey</c> into a name authors pick by. Authoring only — never baked, never read at runtime.
    /// </summary>
    public sealed class AnimationNameRegistry : ScriptableObject, IVocabularyRegistry
    {
        /// <summary>The animation names this project defines.</summary>
        public List<AnimationNameEntry> entries = new List<AnimationNameEntry>();

        /// <summary>Backing store for <see cref="IVocabularyRegistry.GeneratedConstantsPath"/>.</summary>
        public string generatedConstantsPath = string.Empty;

        /// <inheritdoc />
        public string GeneratedConstantsPath
        {
            get { return generatedConstantsPath; }
            set { generatedConstantsPath = value; }
        }

        /// <summary>The display name for <paramref name="animationKey"/>, or null when the registry does not name it.</summary>
        public string FindName(uint animationKey)
        {
            if (entries == null)
            {
                return null;
            }
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                AnimationNameEntry entry = entries[entryIndex];
                if (entry != null && entry.animationKey == animationKey)
                {
                    return string.IsNullOrEmpty(entry.name) ? null : entry.name;
                }
            }
            return null;
        }

        /// <summary>Whether any entry already claims <paramref name="animationKey"/>.</summary>
        public bool ContainsId(uint animationKey)
        {
            if (entries == null)
            {
                return false;
            }
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                AnimationNameEntry entry = entries[entryIndex];
                if (entry != null && entry.animationKey == animationKey)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Mints a fresh key that collides with nothing already in this registry.</summary>
        public uint MintAnimationKey()
        {
            uint candidateKey = StableIdMinting.NewTargetStableId();
            while (ContainsId(candidateKey))
            {
                candidateKey = StableIdMinting.NewTargetStableId();
            }
            return candidateKey;
        }

        /// <summary>Appends an animation name with a freshly minted key and returns it. Does not persist; the editor-side caller must.</summary>
        public uint CreateVocabularyEntry(string name)
        {
            if (entries == null)
            {
                entries = new List<AnimationNameEntry>();
            }
            uint newAnimationKey = MintAnimationKey();
            entries.Add(new AnimationNameEntry { name = name, animationKey = newAnimationKey });
            return newAnimationKey;
        }

        int IVocabularyRegistry.VocabularyEntryCount
        {
            get { return entries != null ? entries.Count : 0; }
        }

        string IVocabularyRegistry.VocabularyEntryName(int entryIndex)
        {
            AnimationNameEntry entry = entries[entryIndex];
            return entry != null ? entry.name : null;
        }

        uint IVocabularyRegistry.VocabularyEntryId(int entryIndex)
        {
            AnimationNameEntry entry = entries[entryIndex];
            return entry != null ? entry.animationKey : 0u;
        }
    }

    /// <summary>One named animation in an <see cref="AnimationNameRegistry"/>.</summary>
    [Serializable]
    public sealed class AnimationNameEntry
    {
        /// <summary>How the animation is shown wherever a profile layer picks one, e.g. <c>Walk</c>.</summary>
        public string name = string.Empty;

        /// <summary>The id an actor profile layer's <c>animationKey</c> actually stores. Minted once, never derived from <see cref="name"/> or reassigned by a rename.</summary>
        public uint animationKey;
    }
}
