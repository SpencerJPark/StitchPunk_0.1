// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// A rig-agnostic collection of motion (Phase F §2): clips plus the optional VAT texture set
    /// they were baked into. An actor names a rig and one or more sets; which dense target a track
    /// drives is resolved at bake against <em>that actor's</em> rig, never against anything stored
    /// here.
    /// </summary>
    /// <remarks>
    /// A set no longer pins a rig. One rig takes several sets, and one set plays on any rig whose
    /// tags partially align — only the aligning tracks animate, the rest skip with a warning
    /// (rules T2 and T6).
    /// </remarks>
    [CreateAssetMenu(
        fileName = "NewClipSet",
        menuName = "DOTS Animation Toolkit/Clip Set Asset",
        order = 2)]
    public sealed class ClipSetAsset : ScriptableObject, IStableIdMintReporter
    {
        [SerializeField] internal ulong stableId;

        // A set names no rig at all — not even an editor-only "last opened against". The Clip
        // Editor holds its own rig as window state, so swapping the open set never swaps the rig
        // and vice versa (owner directive 2026-08-28). Pairing happens in exactly one place:
        // ActorAuthoring, which states a rig and the sets played on it.

        /// <summary>
        /// The clips this set registers. Duplicate entries are a warning (validation rule V11) and
        /// are deduplicated at bake; two distinct clips sharing an id are an error (validation
        /// rule V05).
        /// </summary>
        public List<ClipAsset> clips = new List<ClipAsset>();

        /// <summary>
        /// The baked VAT texture set. Required as soon as any clip in the set carries a
        /// <see cref="VatClipSource"/> (validation rule V07); null otherwise.
        /// </summary>
        public VatTextureSetAsset vatTextures;

        /// <summary>
        /// This set's stable 64-bit identity (architecture section 3.4), folded with the actor's rig
        /// and its sibling sets into the baked <see cref="ClipRegistryBlob.setKey"/> bind key.
        /// </summary>
        public ulong StableId
        {
            get { return stableId; }
        }

        /// <summary>
        /// Assigns a fresh stable id when this set still carries the reserved 0 value. Idempotent.
        /// </summary>
        internal void EnsureStableIds()
        {
            if (stableId == 0UL)
            {
                stableId = StableIdUtility.NewAssetStableId();
                hasUnpersistedStableId = true;
            }
        }

        // Unity raises Awake when an instance is created and OnEnable both after creation and
        // after an asset is deserialized, so between them no asset can reach an inspector, a bake,
        // or a test without an id. Both funnel into the same idempotent assignment.
        private void Awake()
        {
            EnsureStableIds();
        }

        private void OnEnable()
        {
            EnsureStableIds();
        }

        private void OnValidate()
        {
            EnsureStableIds();
        }

        private void Reset()
        {
            EnsureStableIds();
        }

        // Not serialized: this describes an in-memory condition for the current session, and a
        // persisted "needs persisting" flag would contradict itself (amendment A14).
        [System.NonSerialized] private bool hasUnpersistedStableId;

        /// <inheritdoc />
        public bool HasUnpersistedStableId
        {
            get { return hasUnpersistedStableId; }
        }

        /// <inheritdoc />
        public void MarkStableIdPersisted()
        {
            hasUnpersistedStableId = false;
        }

    }
}
