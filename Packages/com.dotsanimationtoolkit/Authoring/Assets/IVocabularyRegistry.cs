// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The shape a project vocabulary — <see cref="TargetTagRegistry"/>, <see cref="AnimEventKeyRegistry"/>
    /// — exposes to the shared editor picker and quick-edit window (Phase E target-tags spec §4.2.1,
    /// amendment E6 Task 3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why an interface rather than a common base class.</strong> The two registries already
    /// existed as unrelated sealed types before E6 with different entry shapes — a tag entry is just
    /// a name and a stable id, an event entry also carries a default window and a description — and
    /// retrofitting a shared base would mean either giving up <c>sealed</c> or dragging event-only
    /// fields onto tags. An interface asks for exactly the four operations the picker and the
    /// generator actually need and nothing else.
    /// </para>
    /// <para>
    /// <strong>Index-based rather than a copied list.</strong> A picker rebuilds its row list on
    /// every keystroke in the filter field (spec §4.2.1); handing back a fresh
    /// <c>List&lt;(string, uint)&gt;</c> on every call would allocate every frame that list is
    /// visible. Reading straight from the registry's own backing list, by index, allocates nothing
    /// the registry was not already holding.
    /// </para>
    /// </remarks>
    public interface IVocabularyRegistry
    {
        /// <summary>
        /// How many rows this vocabulary has, including any with an empty name or a zero id — the
        /// editor picker that consumes this interface skips those itself, the same way this
        /// package's tag picker always has.
        /// </summary>
        int VocabularyEntryCount { get; }

        /// <summary>The display name of the row at <paramref name="entryIndex"/>. May be null or empty.</summary>
        string VocabularyEntryName(int entryIndex);

        /// <summary>The id of the row at <paramref name="entryIndex"/>. 0 means the row is unusable.</summary>
        uint VocabularyEntryId(int entryIndex);

        /// <summary>The display name for <paramref name="id"/>, or null when this vocabulary does not name it.</summary>
        string FindName(uint id);

        // /// <summary>Whether any row already claims <paramref name="id"/>.</summary>
        bool ContainsId(uint id);

        /// <summary>
        /// Defines a new row named <paramref name="name"/> with a fresh, collision-free id chosen the
        /// way this vocabulary chooses one (a random fold for a tag, the lowest free maskable slot
        /// for an event), appends it in memory, and returns the minted id. Does not persist — this
        /// assembly ships to players and cannot write project settings files, so the editor-side
        /// caller must (<c>VocabularyRegistryProvider.PersistVocabulary</c>).
        /// </summary>
        /// <param name="name">The row's name, exactly as typed — this is the one place a vocabulary's
        /// name is ever typed (spec §4.2.1); every other surface only selects.</param>
        uint CreateVocabularyEntry(string name);

        /// <summary>
        /// Where this vocabulary's generated constants file was last written (amendment E6 Task 2),
        /// project-relative when it sits inside the project. Empty until the first generation.
        /// </summary>
        /// <remarks>
        /// <strong>Stored beside the rows rather than in <c>EditorPrefs</c>, because the destination
        /// is a project decision and not a per-machine one.</strong> A teammate pressing Regenerate
        /// must rewrite the file the constants already live in; a per-machine memory would instead
        /// have them pick again and quietly produce a second copy, at which point half the project
        /// compiles against constants nobody is regenerating. Keeping it here means it travels with
        /// the names it describes, through the same round-trip that persists them.
        /// </remarks>
        string GeneratedConstantsPath { get; set; }
    }
}
