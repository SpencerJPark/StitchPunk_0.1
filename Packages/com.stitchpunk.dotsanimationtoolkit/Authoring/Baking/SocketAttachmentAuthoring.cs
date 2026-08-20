// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Makes this GameObject's entity ride a socket on an actor (architecture section 5.2's part
    /// model, extended to attachments).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Bakes with <see cref="TransformUsageFlags.Dynamic"/> and no parent.</strong>
    /// <c>SocketResolveSystem</c> writes a world transform into <c>LocalTransform</c>, which is
    /// only the same thing on a transform root — parenting the attachment to the actor as well
    /// would apply the actor's matrix twice. That bug reads as a subtle scale or offset error
    /// rather than an obvious break, so the shape is enforced here at bake rather than left to
    /// scene setup.
    /// </para>
    /// </remarks>
    [AddComponentMenu("DOTS Animation Toolkit/Socket Attachment")]
    [DisallowMultipleComponent]
    public sealed class SocketAttachmentAuthoring : MonoBehaviour
    {
        /// <summary>The actor carrying the socket. Must bake to an entity with a socket registry.</summary>
        public GameObject actor;

        /// <summary>The rig the socket is declared on — used to pick the socket in the inspector.</summary>
        public RigAsset rig;

        /// <summary>Stable id of the socket to follow.</summary>
        public uint socketId;

        /// <summary>Extra offset in socket space, on top of the socket's own.</summary>
        public Vector3 localOffset = Vector3.zero;

    }

    /// <summary>Bakes a <see cref="SocketAttachmentAuthoring"/> into a <see cref="SocketAttachment"/>.</summary>
    public sealed class SocketAttachmentBaker : Baker<SocketAttachmentAuthoring>
    {
        public override void Bake(SocketAttachmentAuthoring authoring)
        {
            if (authoring.actor == null || authoring.socketId == 0u)
            {
                // An unconfigured attachment bakes to nothing rather than to a socket-less
                // component: baking it anyway would snap the entity to the world origin on the
                // first frame, which reads as a bug in the socket system rather than as an
                // unfinished prefab.
                return;
            }

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new SocketAttachment
            {
                actorRoot = GetEntity(authoring.actor, TransformUsageFlags.Dynamic),
                socketId = authoring.socketId,
                localOffset = new Unity.Mathematics.float3(
                    authoring.localOffset.x, authoring.localOffset.y, authoring.localOffset.z)
            });
        }
    }
}
