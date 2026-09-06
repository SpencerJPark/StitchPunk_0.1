// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The shape a project vocabulary — <see cref="TargetTagRegistry"/>, <see cref="AnimEventKeyRegistry"/>
    /// — exposes to the shared editor picker and quick-edit window.
    /// </summary>
    public interface IVocabularyRegistry
    {
        /// <summary>Includes rows with an empty name or a zero id; the picker skips those itself.</summary>
        int VocabularyEntryCount { get; }

        /// <summary>The display name of the row at <paramref name="entryIndex"/>. May be null or empty.</summary>
        string VocabularyEntryName(int entryIndex);

        /// <summary>The id of the row at <paramref name="entryIndex"/>. 0 means the row is unusable.</summary>
        uint VocabularyEntryId(int entryIndex);

        /// <summary>The display name for <paramref name="id"/>, or null when this vocabulary does not name it.</summary>
        string FindName(uint id);

        /// <summary>Whether any row already claims <paramref name="id"/>.</summary>
        bool ContainsId(uint id);

        /// <summary>
        /// Defines a new row named <paramref name="name"/> with a fresh, collision-free id, appends
        /// it in memory, and returns the minted id. Does not persist — this assembly ships to
        /// players and cannot write project settings files, so the editor-side caller must.
        /// </summary>
        uint CreateVocabularyEntry(string name);

        /// <summary>Where this vocabulary's generated constants file was last written. Empty until the first generation.</summary>
        string GeneratedConstantsPath { get; set; }
    }
}
