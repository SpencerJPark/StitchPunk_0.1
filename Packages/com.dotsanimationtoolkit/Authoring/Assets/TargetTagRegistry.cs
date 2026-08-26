// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Project-wide vocabulary of target tags (Phase E target-tags spec §2, §4.1): the asset that
    /// answers "what is this rig target <em>for</em>?", once, so a face-blink clip authored against
    /// one character's <c>EyeL</c> can play on every other rig that tags a target the same way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A tag answers a different question than a target's own stable id.</strong> A rig
    /// target's own id answers "which slot is this?" and is unique within one rig; a tag answers
    /// "what is this slot for?" and is shared across every rig in the project. A target keeps its
    /// own stable id exactly as before — this registry never replaces that identity, it only adds a
    /// second, optional one that several rigs can agree to share.
    /// </para>
    /// <para>
    /// <strong>Authoring only — this asset is never baked and never read at runtime.</strong> Same
    /// contract as <see cref="AnimEventKeyRegistry"/>, and for the same reason: a project must be
    /// able to rename a tag, reorder the list, or delete a row entirely without invalidating a
    /// single baked clip. Nothing downstream stores a tag's <em>name</em> — a track binds a tag's
    /// <see cref="TargetTagEntry.stableId"/>, which a rename cannot touch.
    /// </para>
    /// <para>
    /// <strong>The id is minted, not derived from the name.</strong> Hashing the name would make a
    /// rename indistinguishable from a delete-and-recreate — every clip bound to the old name would
    /// silently stop resolving, which is precisely the enum failure mode (§1) this whole feature
    /// exists to remove. Ids are minted through <see cref="StableIdUtility.NewTargetStableId"/>, the
    /// same random-fold generator a rig target's own stable id uses, so a tag id and a target id
    /// share one identity scheme even though they occupy separate namespaces.
    /// </para>
    /// <para>
    /// <strong>A project-scoped instance, auto-created on first use — never an asset a person creates
    /// by hand (amendment E6 Task 1, owner directive 2026-08-23: "I don't want to manually create and
    /// wire it — it should just exist").</strong> <c>Editor/ClipEditor/Preview/RagdollPreviewScenery.cs</c>
    /// is this package's precedent for exactly this shape — a <c>ScriptableSingleton&lt;T&gt;</c>
    /// under <c>ProjectSettings/</c> — but that base class lives in the editor assembly and this type
    /// cannot inherit it: <c>ClipValidation</c> (architecture section 3.5) takes a
    /// <see cref="TargetTagRegistry"/> parameter and is documented as having "no editor-assembly
    /// dependency" so it keeps compiling in a player build, and <see cref="TargetTagRegistry"/> must
    /// stay a plain <see cref="ScriptableObject"/> for that to hold. <c>VocabularyRegistryProvider</c> instead
    /// reproduces the same contract by hand, entirely inside <c>#if UNITY_EDITOR</c>: a lazily
    /// created in-memory instance, hydrated from (and saved back to)
    /// <c>ProjectSettings/DotsAnimationToolkitTargetTagRegistry.asset</c> via
    /// <see cref="EditorJsonUtility"/>, which every predefined and custom assembly's editor-compiled
    /// variant can reach without an explicit assembly reference. There is deliberately no
    /// <c>[CreateAssetMenu]</c>, so there is no second, competing instance a person could create by
    /// mistake — <c>VocabularyRegistryProvider</c> is the only way this type is ever obtained in the editor.
    /// </para>
    /// </remarks>
    public sealed class TargetTagRegistry : ScriptableObject, IVocabularyRegistry
    {
        /// <summary>The tags this project defines.</summary>
        public List<TargetTagEntry> entries = new List<TargetTagEntry>();


        /// <summary>
        /// The display name for <paramref name="tagId"/>, or null when the registry does not name
        /// it — the same "unresolved reference" shape <see cref="AnimEventKeyRegistry.FindName"/>
        /// uses for event keys, so a dangling tag id (rule T3) and a dangling event key report
        /// identically to whatever inspector asks.
        /// </summary>
        /// <param name="tagId">The tag id to look up.</param>
        /// <returns>The entry's name, or null.</returns>
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
        /// <param name="tagId">The id to test.</param>
        /// <returns>True when an entry uses it.</returns>
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

        /// <summary>
        /// Mints a fresh id that collides with nothing already in this registry.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="AnimEventKeyRegistry.FindFirstFreeKey"/>, there is no fixed range to
        /// search: a tag id has no maskable-bit constraint, so it is minted the same way a rig
        /// target's own stable id is — a random fold, retried on the vanishingly unlikely event of
        /// a collision with an id this registry already holds. See
        /// <see cref="StableIdUtility.NewTargetStableId"/>.
        /// </remarks>
        /// <returns>A non-zero id unique within this registry.</returns>
        public uint MintTagId()
        {
            uint candidateId = StableIdUtility.NewTargetStableId();
            while (ContainsId(candidateId))
            {
                candidateId = StableIdUtility.NewTargetStableId();
            }
            return candidateId;
        }

        /// <summary>
        /// Appends a tag named <paramref name="name"/> with a freshly minted id, persists the
        /// change (editor only), and returns the minted id — the one code path every "add a tag"
        /// surface (the registry inspector's Add Tag button, the picker's inline "Create tag…") goes
        /// through, so "another tag, please" can never produce a duplicate id by accident even
        /// though ids are random.
        /// </summary>
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

        /// <summary>
        /// The id a rig target's <c>tagId</c> or a track's tag binding actually stores. Minted once
        /// by <see cref="TargetTagRegistry.MintTagId"/>, never derived from <see cref="name"/>, and
        /// never reassigned by a rename — see the type's remarks.
        /// </summary>
        public uint stableId;
    }
}
