// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Authoring
{
    /// <summary>
    /// The registry asset (architecture section 3.3): one rig, the clips authored against it, and
    /// the optional VAT texture set those clips were baked into. A clip set is what an actor
    /// references, and it is what <see cref="ClipRegistryBuilder"/> turns into a single
    /// <see cref="ClipRegistryBlob"/>.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewClipSet",
        menuName = "DOTS Animation Toolkit/Clip Set Asset",
        order = 2)]
    public sealed class ClipSetAsset : ScriptableObject, IStableIdMintReporter
    {
        [SerializeField] internal ulong stableId;

        /// <summary>The rig every clip in this set must be authored against (validation rule V06).</summary>
        public RigAsset rig;

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
        /// This set's stable 64-bit identity (architecture section 3.4), stamped into the baked
        /// registry as <see cref="ClipRegistryBlob.setKey"/>.
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
