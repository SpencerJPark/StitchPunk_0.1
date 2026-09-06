// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>How a socket finds the thing it follows.</summary>
    public enum SocketAttachMode : byte
    {
        /// <summary>Follows a rig target's part entity; no baked motion needed, since sampling systems already compute the part's transform every frame.</summary>
        RigTarget = 0,

        /// <summary>Follows a bone of the VAT source rig, sampled at bake time into <see cref="SocketClipTrackBlob"/> since the bone exists only inside a texture at runtime.</summary>
        Bone = 1
    }

    /// <summary>Baked attachment points for one rig — the socket counterpart to <see cref="ClipRegistryBlob"/>, kept in its own blob so a rig with no sockets pays nothing.</summary>
    public struct SocketRegistryBlob
    {
        public int schemaVersion; // bumped on any layout change, stamped at bake

        public ulong rigKey; // stable id of the source RigAsset

        public BlobArray<uint> sortedSocketIds; // ascending order; binary-search key array, dense index = position here

        public BlobArray<SocketDefinitionBlob> sockets; // per dense socket index

        public BlobArray<SocketClipTrackBlob> clipTracks; // sorted by clipId then socketIndex; empty if the rig declares no bone sockets
    }

    /// <summary>One socket's binding and rest offset.</summary>
    public struct SocketDefinitionBlob
    {
        public SocketAttachMode mode;

        public int targetIndex; // dense target index for RigTarget mode; -1 for bone sockets

        public byte layerIndex; // which playback layer drives a bone socket's time; unused by rig-target sockets

        public float3 localPosition; // offset from the followed target/bone, in its local space

        public quaternion localRotation; // rotation offset from the followed target/bone, in its local space
    }

    /// <summary>One bone socket's motion for one clip: uniformly spaced samples in actor-root space.</summary>
    public struct SocketClipTrackBlob
    {
        public ulong clipId;

        public int socketIndex; // dense index into SocketRegistryBlob.sortedSocketIds

        public float fps; // matches the clip's baked VAT rate

        // Samples are uniform, so a sample index is a frame index; no per-sample time is stored.
        public BlobArray<SocketSampleBlob> samples;
    }

    /// <summary>One baked socket transform, relative to the actor root.</summary>
    public struct SocketSampleBlob
    {
        public float3 position;

        public quaternion rotation;
    }
}
