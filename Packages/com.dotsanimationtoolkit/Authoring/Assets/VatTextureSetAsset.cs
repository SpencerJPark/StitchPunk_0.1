// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The generated product of the editor's VAT texture baker: the animation textures, the static
    /// runtime mesh, the per-clip frame ranges, and the addressing parameters mirrored into
    /// <see cref="VatTextureInfoBlob"/> at bake. Every field is written by the baker and should be
    /// treated as read-only by hand.
    /// </summary>
    public sealed class VatTextureSetAsset : ScriptableObject, IStableIdMintReporter
    {
        [SerializeField] internal ulong setKey;

        /// <summary>Which encoding this set carries, which decides whether the bone or the position texture is used.</summary>
        public VatFlavor flavor = VatFlavor.BoneMatrix;

        // Deliberately never enters a blob — the blob carries only vatSetKey plus addressing metadata.
        /// <summary>Per-bone object-space skinning matrices; used by <see cref="VatFlavor.BoneMatrix"/> sets.</summary>
        public Texture2D boneTexture;

        /// <summary>Absolute object-space vertex positions; used by <see cref="VatFlavor.VertexPosition"/> sets.</summary>
        public Texture2D positionTexture;

        /// <summary>Optional per-vertex normals accompanying <see cref="positionTexture"/>.</summary>
        public Texture2D normalTexture;

        /// <summary>The static mesh the baker emitted, with bone indices and weights packed into UV1/UV2.</summary>
        public Mesh runtimeMesh;

        /// <summary>Bone count of a <see cref="VatFlavor.BoneMatrix"/> set; 0 otherwise.</summary>
        public int boneCount;

        /// <summary>Vertex count of a <see cref="VatFlavor.VertexPosition"/> set; 0 otherwise.</summary>
        public int vertexCount;

        /// <summary>Texture width in texels, mirrored into <see cref="VatTextureInfoBlob.textureWidth"/>.</summary>
        public int textureWidth;

        /// <summary>Texture rows occupied by one animation frame; 1 for the bone flavor.</summary>
        public int rowsPerFrame = 1;

        // A clip with only an untargeted ClipAsset.vatSource bakes exactly one entry here with
        // targetId == 0; a clip whose vatTracks names additional targets bakes one further entry
        // per track. See TryGetTrackRange for the per-target resolution this enables.
        /// <summary>One frame range per baked (clip, target) pair.</summary>
        public List<VatClipRange> clipRanges = new List<VatClipRange>();

        // Stored here rather than on the rig because it is baked data keyed by clip, with the same
        // lifetime and staleness rules as the textures — putting it on the rig would let the two
        // drift, so a sword could ride a hand pose that no longer matches the mesh.
        /// <summary>Bone-socket motion captured during the same bake pass that produced the textures.</summary>
        public List<VatSocketTrack> socketTracks = new List<VatSocketTrack>();

        /// <summary>Hash of the sources this set was baked from (source mesh, clips, settings). A mismatch means the bake is stale.</summary>
        public ulong sourceHash;

        // A VAT texture encodes one skinned mesh's vertex motion and cannot retarget, so unlike
        // transform and sprite content it pins its set to one rig. 0 means "baked before this field
        // existed" and passes.
        /// <summary><see cref="RigAsset.StableId"/> of the rig whose mesh this set was baked from. Binding to a different rig is an error.</summary>
        public ulong sourceRigKey;

        /// <summary>Layout version of the generated data, bumped when the baker's output shape changes. Distinct from <c>ClipRegistryBuilder.SchemaVersion</c>, which versions the baked blob layout.</summary>
        public int schemaVersion;

        /// <summary>This set's stable 64-bit key — the link between the baked registry's <see cref="ClipRegistryBlob.vatSetKey"/> and the textures bound on the material.</summary>
        public ulong SetKey
        {
            get { return setKey; }
        }

        // Pre-multi-source lookup, kept for the single-source case where clip id alone identifies
        // the range unambiguously. Once a clip also bakes vatTracks entries, "first match in list
        // order" is no longer meaningful among them — call TryGetTrackRange instead.
        /// <summary>Finds a frame range baked for a given clip, ignoring which target it belongs to.</summary>
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

        // The authoritative per-target resolution: ClipRegistryBuilder calls this while filling
        // ClipBlob.vatTargetRanges, and the runtime performs the identical fallback again against
        // the baked blob — exact target match, else the untargeted range — so a VAT part with no
        // dedicated track keeps resolving vatSource unchanged after another part gains one.
        /// <summary>
        /// Finds the frame range a specific VAT part should play for a clip: the range baked for
        /// <paramref name="targetId"/> when one exists, otherwise the clip's untargeted range.
        /// </summary>
        /// <param name="targetId">Stable id of the requesting part's target. Pass 0 to ask for the untargeted range directly.</param>
        /// <returns>True when either an exact (clip, target) range or an untargeted range exists.</returns>
        public bool TryGetTrackRange(ulong clipId, uint targetId, out VatClipRange clipRange)
        {
            VatClipRange untargetedRange = default;
            bool hasUntargetedRange = false;

            if (clipRanges != null)
            {
                for (int rangeIndex = 0; rangeIndex < clipRanges.Count; rangeIndex++)
                {
                    VatClipRange candidate = clipRanges[rangeIndex];
                    if (candidate.clipId != clipId)
                    {
                        continue;
                    }
                    if (targetId != 0u && candidate.targetId == targetId)
                    {
                        clipRange = candidate;
                        return true;
                    }
                    if (candidate.targetId == 0u && !hasUntargetedRange)
                    {
                        untargetedRange = candidate;
                        hasUntargetedRange = true;
                    }
                }
            }

            if (hasUntargetedRange)
            {
                clipRange = untargetedRange;
                return true;
            }
            clipRange = default;
            return false;
        }

        /// <summary>Assigns a fresh stable key when this set still carries the reserved 0 value. Idempotent.</summary>
        internal void EnsureStableIds()
        {
            if (setKey == 0UL)
            {
                setKey = StableIdMinting.NewAssetStableId();
                hasUnpersistedStableId = true;
            }
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

    // Positions and rotations are parallel lists rather than a list of pairs: Unity serializes a
    // list of primitives compactly and a list of small structs verbosely, and a long clip's socket
    // track is thousands of entries. The two are always written together with equal length — the
    // builder rejects the track if they do not.
    /// <summary>One bone socket's baked motion for one clip, in actor-root space.</summary>
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

    /// <summary>The frame range one (clip, target) pair occupies in a baked VAT texture.</summary>
    [Serializable]
    public struct VatClipRange
    {
        /// <summary>Stable id of the <see cref="ClipAsset"/> this range belongs to.</summary>
        public ulong clipId;

        /// <summary>Stable id of the rig target this range is scoped to, or 0 for the clip's untargeted range.</summary>
        public uint targetId;

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
