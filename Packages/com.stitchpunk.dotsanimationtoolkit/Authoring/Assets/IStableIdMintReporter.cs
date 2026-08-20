// Copyright (c) 2026 Stitch Punk. All rights reserved.

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Implemented by every identity-bearing authoring asset so tooling can discover that an asset
    /// minted a stable id which is not yet on disk (architecture section 3.4, amendment A14).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Assets mint an id for themselves when the stored value is the reserved 0 — on creation, and
    /// on deserialization of an asset saved before it had one. Minting alone is not enough: an id
    /// that lives only in memory is re-minted on the next load, so the asset silently changes
    /// identity every session and any baked reference to it rots. Persisting is therefore part of
    /// the identity contract, not an optimisation.
    /// </para>
    /// <para>
    /// The save itself cannot happen here. It needs <c>EditorUtility.SetDirty</c> plus an asset
    /// save, and section 1.3 keeps this assembly free of editor-only APIs so it stays valid in
    /// player builds. So the authoring layer reports the fact and the editor layer acts on it.
    /// </para>
    /// <para>
    /// Reporting is a flag rather than an event on purpose. Minting happens inside
    /// <c>Awake</c>/<c>OnEnable</c>, which Unity raises during deserialization, and marking an
    /// asset dirty while it is still loading is not permitted. A flag lets the editor layer poll
    /// once loading has settled — from an asset postprocessor, or a deferred editor callback —
    /// instead of being called at the one moment it cannot act.
    /// </para>
    /// <para>
    /// The flag is deliberately not serialized: it describes this session's in-memory state, and a
    /// persisted "needs persisting" flag would be self-contradictory.
    /// </para>
    /// </remarks>
    public interface IStableIdMintReporter
    {
        /// <summary>
        /// True when this asset minted a stable id that has not been written to disk yet. Clears
        /// once <see cref="MarkStableIdPersisted"/> is called by whoever performed the save.
        /// </summary>
        bool HasUnpersistedStableId { get; }

        /// <summary>
        /// Called by the editor layer after it has saved the asset, to clear
        /// <see cref="HasUnpersistedStableId"/>. Calling this without actually saving re-opens the
        /// silent-re-mint bug this contract exists to close.
        /// </summary>
        void MarkStableIdPersisted();
    }
}
