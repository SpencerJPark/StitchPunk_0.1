// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Authoring
{
    /// <summary>
    /// The authoring definition of an animatable thing (architecture section 3.1): its named,
    /// stable-id'd <see cref="targets"/>, its ordered <see cref="layers"/>, and the mirror table the
    /// Mirror Clip utility consumes. One rig serves many clips and many actors.
    /// </summary>
    /// <remarks>
    /// Targets carry stable ids because their meaning is independent of order; layers deliberately
    /// do not, because a layer's meaning <em>is</em> its compositing priority — index = priority,
    /// higher index composites later — so reordering layers is a semantic edit, not a rename.
    /// </remarks>
    [CreateAssetMenu(
        fileName = "NewRig",
        menuName = "DOTS Animation Toolkit/Rig Asset",
        order = 0)]
    public sealed class RigAsset : ScriptableObject, IStableIdMintReporter
    {
        /// <summary>The maximum number of playback layers a rig may define (architecture sections 3.1, 5.2).</summary>
        public const int MaxLayerCount = 8;

        [SerializeField] internal ulong stableId;

        /// <summary>
        /// The animatable slots of this rig. Each row's <c>stableId</c> is unique within the rig and
        /// is what tracks bind to — never the display name and never the list position.
        /// </summary>
        public List<RigTargetDefinition> targets = new List<RigTargetDefinition>();

        /// <summary>
        /// The rig's playback layers, lowest priority first. At most
        /// <see cref="MaxLayerCount"/> entries; at least one (validation rule V13).
        /// </summary>
        public List<LayerDefinition> layers = new List<LayerDefinition>();

        /// <summary>
        /// The user-configured left/right target pairs the Mirror Clip utility swaps. Empty when the
        /// rig is not mirrorable. Consumed by that editor utility (build step C7); it is authoring
        /// data only and never reaches the baked blob, so no bake or runtime path reads it.
        /// </summary>
        public MirrorPair[] mirrorPairs = Array.Empty<MirrorPair>();

        /// <summary>
        /// This rig's stable 64-bit identity (architecture section 3.4). Assigned once when the
        /// asset is created and never changed except through the editor's explicit remap tooling.
        /// </summary>
        public ulong StableId
        {
            get { return stableId; }
        }

        /// <summary>
        /// Assigns a fresh stable id to this rig and to every target row that still carries the
        /// reserved 0 value. Idempotent: an already-identified rig or row is left untouched, which
        /// is what makes duplicate-then-edit copy the id rather than mint a new one (the duplicate
        /// is separated later by the editor's id-collision postprocessor, architecture section 3.4).
        /// </summary>
        internal void EnsureStableIds()
        {
            if (stableId == 0UL)
            {
                stableId = StableIdUtility.NewAssetStableId();
                hasUnpersistedStableId = true;
            }
            if (targets == null)
            {
                return;
            }
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                RigTargetDefinition targetDefinition = targets[targetIndex];
                if (targetDefinition == null)
                {
                    continue;
                }
                if (targetDefinition.stableId == 0u)
                {
                    targetDefinition.stableId = StableIdUtility.NewTargetStableId();
                    hasUnpersistedStableId = true;
                }
            }
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
    }

    /// <summary>
    /// One animatable slot on a rig (architecture section 3.1): a 2D cutout part quad, a flipbook
    /// plane, or a VAT sub-mesh. Identified by its <see cref="Id"/>, never by
    /// <see cref="displayName"/> and never by list position.
    /// </summary>
    [Serializable]
    public sealed class RigTargetDefinition
    {
        /// <summary>Freely renameable label shown in the inspectors and the clip editor.</summary>
        public string displayName = string.Empty;

        [SerializeField] internal uint stableId;

        /// <summary>How this target is presented, which decides the components its part entity gets.</summary>
        public TargetKind kind = TargetKind.Quad;

        /// <summary>
        /// Conservative local half-extents used by the bake-time bounds math (architecture
        /// section 4.6). Negative components are clamped to 0 at bake.
        /// </summary>
        public float3 boundsExtents = new float3(0.5f, 0.5f, 0.5f);

        /// <summary>
        /// How many consecutive frames one <em>variant</em> of this target owns in its texture
        /// array (architecture section 5.7, amendment A37). 1 means the target has no variant
        /// blocks.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A design-driven target is laid out in blocks — for ears,
        /// <c>[pointy_front, pointy_back, round_front, round_back, …]</c> gives 2. A host's design
        /// system rolls the variant and writes its slice into <c>TargetRestPose.restSliceIndex</c>;
        /// a view offset then has to land on another frame of <em>that</em> block.
        /// </para>
        /// <para>
        /// Above 1, offsets wrap inside the block, so an over-large offset can never display a
        /// different variant's art. The failure that prevents — a character wearing someone else's
        /// ears — is invisible to every automated test and immediately obvious to a player, which is
        /// why the wrap is in the package rather than left to callers.
        /// </para>
        /// </remarks>
        [Min(1)] public int framesPerVariant = 1;

        /// <summary>
        /// Whether this target's presentation changes with the direction the actor faces
        /// (amendment A37). Targets that opt in are baked a <c>PartFacing</c> component.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Explicit rather than derived from <see cref="framesPerVariant"/>, which was the
        /// first attempt and was wrong.</strong> Facing changes a part in two independent ways: an
        /// <em>alt view</em> is a different slice and needs a variant block, while a <em>mirror</em>
        /// is the same art reflected and needs no block at all. Deriving the opt-in from
        /// <c>framesPerVariant &gt; 1</c> therefore excluded every mirror-only target — a nose that
        /// simply flips — which is precisely the case the owner tested first and found inert.
        /// </para>
        /// <para>
        /// Default false, so a rig that never opted in bakes exactly the archetype it did before
        /// A37 and pays nothing.
        /// </para>
        /// </remarks>
        public bool facesDirection;

        /// <summary>
        /// This target's stable 32-bit identity (architecture section 3.4). Unique within the
        /// owning rig (validation rule V05).
        /// </summary>
        public TargetId Id
        {
            get { return new TargetId(stableId); }
        }
    }

    /// <summary>
    /// One playback layer slot on a rig (architecture section 3.1). The layer's identity is its list
    /// position: index = priority, and a higher index composites later and therefore wins.
    /// </summary>
    [Serializable]
    public sealed class LayerDefinition
    {
        /// <summary>Cosmetic label only — layer identity is the list position, never this name.</summary>
        public string displayName = string.Empty;

        /// <summary>
        /// Whether the baked actor starts with this layer active. Authored here and consumed by the
        /// entity baker (build step C3), which seeds the actor's <c>PlaybackLayer</c> buffer — no
        /// module reads it yet, so an unused-field search will not find a consumer until then.
        /// </summary>
        public bool defaultActive;
    }

    /// <summary>
    /// One left/right target pairing consumed by the editor's Mirror Clip utility (architecture
    /// sections 3.1, 10 answer 7). Authored per rig; the package never infers mirrors from names.
    /// </summary>
    [Serializable]
    public struct MirrorPair
    {
        /// <summary>Stable id of the left-hand target.</summary>
        public uint leftTargetId;

        /// <summary>Stable id of the right-hand target.</summary>
        public uint rightTargetId;

    }
}
