// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Entities;

namespace StitchPunk.AnimationToolkit.Authoring
{
    /// <summary>
    /// The bake-time half of a rig part's binding: what <c>RigTargetBaker</c> knows and
    /// <c>RigBindingBakingSystem</c> needs, carried as entity data so the cross-entity pass can stay
    /// Bursted and touch no managed objects (architecture section 4.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A baker may only write components on the entity it is baking, so a part cannot append itself
    /// to its actor's <see cref="RigPartRef"/> buffer and cannot read the actor's registry blob to
    /// turn its <see cref="targetId"/> into a dense index. It records both facts here instead, and
    /// the cross-entity system in <c>PostBakingSystemGroup</c> completes the binding.
    /// </para>
    /// <para>
    /// <see cref="BakingTypeAttribute"/> rather than <c>TemporaryBakingType</c>: incremental baking
    /// re-runs only the bakers whose inputs changed, but <c>RigBindingBakingSystem</c> rebuilds every
    /// actor's whole <see cref="RigPartRef"/> buffer on every pass, so it must still see the parts
    /// that were <em>not</em> re-baked. A temporary type would be stripped after each pass and those
    /// parts would silently drop out of the buffer. Baking types never reach the built entity scene.
    /// </para>
    /// </remarks>
    [BakingType]
    public struct RigPartBakeLink : IComponentData
    {
        /// <summary>The actor-root entity this part belongs to, resolved from the authoring hierarchy.</summary>
        public Entity actorRoot;

        /// <summary>The rig target's stable id, still to be resolved into a dense target index.</summary>
        public uint targetId;

        /// <summary>
        /// A hash of the authoring GameObject's hierarchy path, carried purely so a Bursted error
        /// message can point at the offending object without touching a managed reference.
        ///
        /// Not an instance id: Unity's <c>GetInstanceID</c> is deprecated and its successor
        /// <c>EntityId</c> is explicitly documented as no longer representable by an <c>int</c>.
        /// A path hash is also the better value regardless, because it is <em>stable across
        /// bakes</em> — an instance id changes every session, which would make the same prefab bake
        /// to different bytes each time. It is a diagnostic aid, never an identity: stable identity
        /// is what the section 3.4 ids are for.
        /// </summary>
        public uint authoringPathHash;
    }
}
