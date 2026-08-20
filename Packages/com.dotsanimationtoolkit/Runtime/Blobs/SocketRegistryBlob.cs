// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>How a socket finds the thing it follows.</summary>
    public enum SocketAttachMode : byte
    {
        /// <summary>
        /// Follows a rig target's part entity. Needs no baked motion — the part's transform is
        /// already computed every frame by the sampling systems, so the socket is a read.
        /// </summary>
        RigTarget = 0,

        /// <summary>
        /// Follows a bone of the VAT source rig. The bone exists only inside a texture at runtime,
        /// so its motion is sampled at bake time into <see cref="SocketClipTrackBlob"/>.
        /// </summary>
        Bone = 1
    }

    /// <summary>
    /// Baked attachment points for one rig (the socket counterpart to <see cref="ClipRegistryBlob"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Sockets live in their own blob, deliberately.</strong> Folding them into the clip
    /// registry would bump its schema version and invalidate its golden content hash for every
    /// project, whether or not that project uses sockets. A separate blob keeps attachment an
    /// opt-in bolt-on: a rig with no sockets bakes nothing and pays nothing, and the clip format is
    /// untouched.
    /// </para>
    /// <para>
    /// <strong>Why baked samples rather than a second VAT texture.</strong> A texture is the right
    /// home for data the GPU consumes. Attachments are entities with a <c>LocalTransform</c>, so
    /// the consumer is the CPU — and reading a texture from the CPU means a readback that is slow,
    /// asynchronous, and unavailable at all when the texture is not marked readable in a build. The
    /// data is also small: a socket costs one position and one rotation per frame, so eight sockets
    /// over six hundred frames is on the order of a hundred kilobytes. A blob answers the same
    /// question with one indexed load inside a Burst job. A socket texture would earn its place
    /// only if a shader needed the position directly.
    /// </para>
    /// </remarks>
    public struct SocketRegistryBlob
    {
        /// <summary>Blob layout version; bumped on any layout change and stamped at bake.</summary>
        public int schemaVersion;

        /// <summary>Stable id of the source <c>RigAsset</c>.</summary>
        public ulong rigKey;

        /// <summary>
        /// All socket ids in ascending order — the binary-search key array. A socket's dense index
        /// is its position here, and <see cref="sockets"/> is written in the same order.
        /// </summary>
        public BlobArray<uint> sortedSocketIds;

        /// <summary>Per dense socket index: how that socket attaches and where it sits.</summary>
        public BlobArray<SocketDefinitionBlob> sockets;

        /// <summary>
        /// Baked bone-socket motion, sorted ascending by <see cref="SocketClipTrackBlob.clipId"/>
        /// then by <see cref="SocketClipTrackBlob.socketIndex"/>. Empty when the rig declares no
        /// bone sockets.
        /// </summary>
        public BlobArray<SocketClipTrackBlob> clipTracks;
    }

    /// <summary>One socket's binding and rest offset.</summary>
    public struct SocketDefinitionBlob
    {
        /// <summary>Whether this socket follows a rig target or a baked bone.</summary>
        public SocketAttachMode mode;

        /// <summary>
        /// Dense target index for <see cref="SocketAttachMode.RigTarget"/>; −1 for bone sockets.
        /// </summary>
        public int targetIndex;

        /// <summary>
        /// Which playback layer drives a bone socket's time, mirroring <c>VatDriven.layerIndex</c>.
        /// A hand and a cape on one actor may follow different layers, so the layer is per socket
        /// rather than per actor. Unused by rig-target sockets, which follow their part's live
        /// transform whatever drove it.
        /// </summary>
        public byte layerIndex;

        /// <summary>Offset from the followed target or bone, in its local space.</summary>
        public float3 localPosition;

        /// <summary>Rotation offset from the followed target or bone, in its local space.</summary>
        public quaternion localRotation;
    }

    /// <summary>
    /// One bone socket's motion for one clip: uniformly spaced samples in actor-root space.
    /// </summary>
    /// <remarks>
    /// Samples are uniform, so a sample index <em>is</em> a frame index and no per-sample time is
    /// stored — the same reasoning that lets a VAT texture address rows by frame. Time maps to a
    /// sample through <see cref="fps"/>, which mirrors the VAT range's own rate so an attachment
    /// and the mesh it rides on never disagree about which frame "now" is.
    /// </remarks>
    public struct SocketClipTrackBlob
    {
        /// <summary>The clip this motion belongs to.</summary>
        public ulong clipId;

        /// <summary>Dense socket index into <see cref="SocketRegistryBlob.sortedSocketIds"/>.</summary>
        public int socketIndex;

        /// <summary>Sample rate; matches the clip's baked VAT rate.</summary>
        public float fps;

        /// <summary>Per-frame transforms, relative to the actor root.</summary>
        public BlobArray<SocketSampleBlob> samples;
    }

    /// <summary>One baked socket transform, relative to the actor root.</summary>
    public struct SocketSampleBlob
    {
        /// <summary>Position relative to the actor root.</summary>
        public float3 position;

        /// <summary>Rotation relative to the actor root.</summary>
        public quaternion rotation;
    }
}
