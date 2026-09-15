// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>Bakes an <see cref="AnimEventRoutingAsset"/> into the world's singleton <see cref="AnimEventRouting"/>.</summary>
    [AddComponentMenu("DOTS Animation Toolkit/Anim Event Routing")]
    [DisallowMultipleComponent]
    public sealed class AnimEventRoutingAuthoring : MonoBehaviour
    {
        [Tooltip("One per world: the baked component is read as a singleton, so a second instance would be ambiguous.")]
        public AnimEventRoutingAsset routing;
    }

    public sealed class AnimEventRoutingBaker : Baker<AnimEventRoutingAuthoring>
    {
        public override void Bake(AnimEventRoutingAuthoring authoring)
        {
            if (authoring.routing == null)
            {
                // An unconfigured router bakes to nothing, the same rule as other optional asset bakers.
                return;
            }

            DependsOn(authoring.routing);

            BlobAssetReference<AnimEventRoutingBlob> routingBlob = AnimEventRoutingBuilder.Build(authoring.routing, Allocator.Persistent);

            Unity.Entities.Hash128 blobHash;
            AddBlobAsset(ref routingBlob, out blobHash);

            Entity routingEntity = GetEntity(TransformUsageFlags.None);
            AddComponent(routingEntity, new AnimEventRouting { Value = routingBlob });
        }
    }
}
