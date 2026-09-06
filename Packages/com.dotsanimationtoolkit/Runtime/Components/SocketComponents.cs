// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>The actor's link to its baked socket data. Present only on actors whose rig declares sockets.</summary>
    public struct SocketRegistry : IComponentData
    {
        public BlobAssetReference<SocketRegistryBlob> Value; // owned by the bake-time BlobAssetStore
    }

    /// <summary>
    /// Marks an entity as riding a socket on some actor. The attached entity must be a transform
    /// root, not a child of the actor: <c>SocketResolveSystem</c> writes a world transform into
    /// <c>LocalTransform</c>, and parenting it too would apply the actor's transform twice.
    /// </summary>
    public struct SocketAttachment : IComponentData
    {
        public Entity actorRoot; // whose socket this entity follows

        public uint socketId; // stable id

        public float3 localOffset; // extra offset after the socket's own, in socket space; lets two things share one socket
    }
}
