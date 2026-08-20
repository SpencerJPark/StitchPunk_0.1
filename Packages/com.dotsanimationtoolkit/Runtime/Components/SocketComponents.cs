// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The actor's link to its baked socket data. Present only on actors whose rig declares
    /// sockets — a rig without them bakes nothing and carries nothing.
    /// </summary>
    public struct SocketRegistry : IComponentData
    {
        /// <summary>The baked socket blob, owned by the bake-time <c>BlobAssetStore</c>.</summary>
        public BlobAssetReference<SocketRegistryBlob> Value;
    }

    /// <summary>
    /// Marks an entity as riding a socket on some actor (architecture section 5.2's part model,
    /// extended to attachments).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The attached entity must be a transform root.</strong> <c>SocketResolveSystem</c>
    /// writes a world transform into <c>LocalTransform</c>, which is only the same thing when the
    /// entity has no <c>Parent</c>. Parenting the attachment to the actor and *also* driving it
    /// from a socket would apply the actor's transform twice — the classic double-transform bug,
    /// and one that looks like a subtle scale error rather than an obvious break. Attachments are
    /// independent entities that follow; they are not children.
    /// </para>
    /// <para>
    /// This is why the socket carries its own offset rather than expecting the attachment to be
    /// pre-positioned: the entity's own transform is overwritten every frame, so any offset stored
    /// there would be lost on the first update.
    /// </para>
    /// </remarks>
    public struct SocketAttachment : IComponentData
    {
        /// <summary>The actor whose socket this entity follows.</summary>
        public Entity actorRoot;

        /// <summary>Which socket, by stable id.</summary>
        public uint socketId;

        /// <summary>
        /// Extra offset applied after the socket's own, in socket space. Lets two things share one
        /// socket without needing two sockets authored on the rig.
        /// </summary>
        public float3 localOffset;
    }
}
