// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Authoring
{
    /// <summary>
    /// The generated product of the editor's VAT texture baker (architecture sections 3.3, 4.7):
    /// the animation textures, the static runtime mesh, the per-clip frame ranges, and the
    /// addressing parameters mirrored into <see cref="VatTextureInfoBlob"/> at bake. Every field is
    /// written by the baker and treated as read-only by hand.
    /// </summary>
    /// <remarks>
    /// The textures themselves never travel in a blob: the registry stores only
    /// <see cref="SetKey"/> plus addressing metadata, and the texture objects reach the GPU through
    /// the actor's shared material (architecture section 4.4).
    /// </remarks>
    public sealed class VatTextureSetAsset : ScriptableObject, IStableIdMintReporter
    {
        [SerializeField] internal ulong setKey;

        /// <summary>Which encoding this set carries, which decides whether the bone or the position texture is used.</summary>
        public VatFlavor flavor = VatFlavor.BoneMatrix;

        /// <summary>
        /// Per-bone object-space skinning matrices; used by <see cref="VatFlavor.BoneMatrix"/> sets.
        /// Written by the VAT baker and bound to the material (build step C6). Deliberately never
        /// enters a blob — the blob carries only <c>vatSetKey</c> plus addressing metadata.
        /// </summary>
        public Texture2D boneTexture;

        /// <summary>Absolute object-space vertex positions; used by <see cref="VatFlavor.VertexPosition"/> sets.</summary>
        public Texture2D positionTexture;

        /// <summary>Optional per-vertex normals accompanying <see cref="positionTexture"/>.</summary>
        public Texture2D normalTexture;

        /// <summary>
        /// The static mesh the baker emitted, with bone indices and weights packed into UV1/UV2.
        /// Produced and consumed by the VAT pipeline (build step C6); nothing in the authoring or
        /// bake path reads it.
        /// </summary>
        public Mesh runtimeMesh;

        /// <summary>Bone count of a <see cref="VatFlavor.BoneMatrix"/> set; 0 otherwise.</summary>
        public int boneCount;

        /// <summary>Vertex count of a <see cref="VatFlavor.VertexPosition"/> set; 0 otherwise.</summary>
        public int vertexCount;

        /// <summary>Texture width in texels, mirrored into <see cref="VatTextureInfoBlob.textureWidth"/>.</summary>
        public int textureWidth;

        /// <summary>Texture rows occupied by one animation frame; 1 for the bone flavor.</summary>
        public int rowsPerFrame = 1;

        /// <summary>One frame range per baked clip, keyed by the clip's stable id.</summary>
        public List<VatClipRange> clipRanges = new List<VatClipRange>();

        /// <summary>
        /// Bone-socket motion captured during the same bake pass that produced the textures.
        /// </summary>
        /// <remarks>
        /// Stored here rather than on the rig because it is <em>baked</em> data keyed by clip, with
        /// exactly the lifetime and staleness rules the textures have: rebake the clips and these
        /// must be rebuilt with them. Putting sampled motion on the rig would let the two drift, so
        /// a sword could ride a hand pose that no longer matches the mesh.
        /// </remarks>
        public List<VatSocketTrack> socketTracks = new List<VatSocketTrack>();

        /// <summary>
        /// Hash of the sources this set was baked from (source mesh, clips, settings). A mismatch
        /// against a freshly recomputed hash means the bake is stale (validation rule V08).
        /// </summary>
        public ulong sourceHash;

        /// <summary>
        /// Layout version of the generated data, bumped when the baker's output shape changes. This
        /// versions the <em>texture set asset</em>, and is distinct from
        /// <c>ClipRegistryBuilder.SchemaVersion</c>, which versions the baked blob layout. Written
        /// and checked by the VAT baker (build step C6).
        /// </summary>
        public int schemaVersion;

        /// <summary>
        /// This set's stable 64-bit key (architecture section 3.4) — the link between the baked
        /// registry's <see cref="ClipRegistryBlob.vatSetKey"/> and the textures bound on the
        /// material (architecture section 4.4).
        /// </summary>
        public ulong SetKey
        {
            get { return setKey; }
        }

        /// <summary>
        /// Finds the frame range baked for a given clip.
        /// </summary>
        /// <param name="clipId">Stable id of the clip whose range is wanted.</param>
        /// <param name="clipRange">The matching range on success; <c>default</c> on failure.</param>
        /// <returns>True when this set holds a range for <paramref name="clipId"/>.</returns>
        public bool TryGetClipRange(ulong clipId, out VatClipRange clipRange)
        {
            if (clipRanges != null)
            {
                for (int rangeIndex = 0; rangeIndex < clipRanges.Count; rangeIndex++)
                {
                    if (clipRanges[rangeIndex].clipId == clipId)
                    {
                        clipRange = clipRanges[rangeIndex];
                        return true;
                    }
                }
            }
            clipRange = default;
            return false;
        }

        /// <summary>
        /// Assigns a fresh stable key when this set still carries the reserved 0 value. Idempotent.
        /// </summary>
        internal void EnsureStableIds()
        {
            if (setKey == 0UL)
            {
                setKey = StableIdUtility.NewAssetStableId();
                hasUnpersistedStableId = true;
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
    /// One bone socket's baked motion for one clip, in actor-root space.
    /// </summary>
    /// <remarks>
    /// Positions and rotations are parallel lists rather than a list of pairs: Unity serializes a
    /// list of primitives compactly and a list of small structs verbosely, and a long clip's socket
    /// track is thousands of entries. The two are written together and always have equal length —
    /// the builder rejects the track if they do not.
    /// </remarks>
    [Serializable]
    public sealed class VatSocketTrack
    {
        /// <summary>The clip this motion belongs to.</summary>
        public ulong clipId;

        /// <summary>The socket, by stable id.</summary>
        public uint socketId;

        /// <summary>Sample rate; matches the clip's baked VAT rate.</summary>
        public float fps = 30f;

        /// <summary>Per-frame positions relative to the actor root.</summary>
        public List<Vector3> positions = new List<Vector3>();

        /// <summary>Per-frame rotations relative to the actor root.</summary>
        public List<Quaternion> rotations = new List<Quaternion>();
    }

    /// <summary>
    /// The frame range one clip occupies in a baked VAT texture (architecture section 3.3).
    /// </summary>
    [Serializable]
    public struct VatClipRange
    {
        /// <summary>Stable id of the <see cref="ClipAsset"/> this range belongs to.</summary>
        public ulong clipId;

        /// <summary>Index of this clip's first frame in the texture's global frame numbering.</summary>
        public int frameStart;

        /// <summary>Number of frames this clip occupies, including the duplicated loop-safe frame.</summary>
        public int frameCount;

        /// <summary>The rate these frames were sampled at.</summary>
        public float fps;

        /// <summary>Object-space bounds measured over every baked frame of this clip.</summary>
        public Bounds bounds;

    }
}
