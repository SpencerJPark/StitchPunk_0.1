// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Implemented by an authoring asset that mints its own stable id, so the editor layer can
    /// detect and persist an id that exists only in memory.
    /// </summary>
    public interface IStableIdMintReporter
    {
        /// <summary>True until <see cref="MarkStableIdPersisted"/> runs after the asset is saved.</summary>
        bool HasUnpersistedStableId { get; }

        /// <summary>Call only once the asset has actually been saved, or the id silently re-mints next load.</summary>
        void MarkStableIdPersisted();
    }
}
