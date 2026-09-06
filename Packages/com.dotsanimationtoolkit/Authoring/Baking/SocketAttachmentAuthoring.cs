// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>Makes this GameObject's entity ride a socket on an actor.</summary>
    [AddComponentMenu("DOTS Animation Toolkit/Socket Attachment")]
    [DisallowMultipleComponent]
    public sealed class SocketAttachmentAuthoring : MonoBehaviour
    {
        /// <summary>Must bake to an entity with a socket registry.</summary>
        public GameObject actor;

        /// <summary>The rig the socket is declared on — used to pick the socket in the inspector.</summary>
        public RigAsset rig;

        public uint socketId;

        public Vector3 localOffset = Vector3.zero;
    }

    /// <summary>Bakes a <see cref="SocketAttachmentAuthoring"/> into a <see cref="SocketAttachment"/>.</summary>
    public sealed class SocketAttachmentBaker : Baker<SocketAttachmentAuthoring>
    {
        public override void Bake(SocketAttachmentAuthoring authoring)
        {
            if (authoring.actor == null || authoring.socketId == 0u)
            {
                // Bakes to nothing rather than a socket-less component: baking it anyway would snap
                // the entity to the world origin on the first frame.
                return;
            }

            // Dynamic and no parent: SocketResolveSystem writes a world transform directly, so
            // parenting to the actor here would apply the actor's matrix twice.
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
