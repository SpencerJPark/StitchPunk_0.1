// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The bake-time half of a rig part's binding: what a part's own baker knows, carried as entity
    /// data so <c>RigBindingBakingSystem</c>'s cross-entity pass can stay Bursted.
    /// </summary>
    // BakingType, not TemporaryBakingType: RigBindingBakingSystem rebuilds every actor's whole
    // RigPartRef buffer on every pass, so it must still see parts that were not re-baked this time.
    [BakingType]
    internal struct RigPartBakeLink : IComponentData
    {
        // A baker may only write components on the entity it is baking, so the part records the
        // actor here instead of appending itself to the actor's RigPartRef buffer directly.
        public Entity actorRoot;

        /// <summary>Still to be resolved into a dense target index.</summary>
        public uint targetId;

        /// <summary>Diagnostic text only — never an identity, nothing may key off it.</summary>
        public FixedString128Bytes authoringPath;
    }
}
