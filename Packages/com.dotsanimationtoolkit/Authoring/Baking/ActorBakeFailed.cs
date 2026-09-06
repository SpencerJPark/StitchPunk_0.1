// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Tags an actor entity whose bake bailed out after already reporting why, so
    /// <c>RigBindingBakingSystem</c> stays silent about its unresolved parts instead of duplicating
    /// the diagnostic.
    /// </summary>
    // BakingType, not TemporaryBakingType: the binding pass sees every actor on every incremental
    // bake and must still see this tag on actors that were not themselves re-baked.
    [BakingType]
    internal struct ActorBakeFailed : IComponentData
    {
    }
}
