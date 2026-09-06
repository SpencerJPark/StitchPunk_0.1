// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// A rig-agnostic collection of motion: clips plus the optional VAT texture set they were baked
    /// into. A set pins no rig — one set plays on any rig whose tags partially align, with the
    /// dense target a track drives resolved at bake against the actor's own rig.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewClipSet",
        menuName = "DOTS Animation Toolkit/Clip Set Asset",
        order = 2)]
    public sealed class ClipSetAsset : ScriptableObject, IStableIdMintReporter
    {
        [SerializeField] internal ulong stableId;

        // A set names no rig at all — not even an editor-only "last opened against". The Clip
        // Editor holds its own rig as window state, so swapping the open set never swaps the rig
        // and vice versa. Pairing happens in exactly one place: ActorAuthoring.

        [Tooltip("Clips this set registers. Duplicates are deduplicated at bake; two distinct clips sharing an id fail validation.")]
        public List<ClipAsset> clips = new List<ClipAsset>();

        [Tooltip("Baked VAT texture set. Required as soon as any clip in the set carries a VAT source.")]
        public VatTextureSetAsset vatTextures;

        /// <summary>This set's stable 64-bit identity, folded with the actor's rig and its sibling sets into the baked bind key.</summary>
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
                stableId = StableIdMinting.NewAssetStableId();
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
        // persisted "needs persisting" flag would contradict itself.
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
