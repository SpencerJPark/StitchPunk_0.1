// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit.Authoring
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
    /// <para>
    /// <strong>Internal.</strong> Writer (<c>RigTargetBaker</c>) and reader
    /// (<c>RigBindingBakingSystem</c>'s internal job) are both in this assembly, and the tests reach
    /// it through the contracted <c>InternalsVisibleTo</c>. Nothing about Entities requires a baking
    /// type to be public, and this one carries no contract a consumer could use.
    /// </para>
    /// </remarks>
    [BakingType]
    internal struct RigPartBakeLink : IComponentData
    {
        /// <summary>The actor-root entity this part belongs to, resolved from the authoring hierarchy.</summary>
        public Entity actorRoot;

        /// <summary>The rig target's stable id, still to be resolved into a dense target index.</summary>
        public uint targetId;

        /// <summary>
        /// The authoring GameObject's hierarchy path, carried as text so a Bursted system can name
        /// the offending object without touching a managed reference.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A path, not an id. Unity's <c>GetInstanceID</c> is deprecated and its successor
        /// <c>EntityId</c> is documented as no longer representable by an <c>int</c>, and neither
        /// survives a session — baking one would make the same prefab produce different bytes every
        /// time. A path is stable across bakes and, unlike the hash this field replaced, is
        /// something a user can act on: <c>Rig/Torso/LeftArm</c> tells them where to look, whereas
        /// <c>authoring path hash 2463534242</c> tells them only that something is wrong.
        /// </para>
        /// <para>
        /// Diagnostic text, never an identity. Stable identity is what the section 3.4 ids are for,
        /// and nothing may key off this field.
        /// </para>
        /// </remarks>
        public FixedString128Bytes authoringPath;
    }
}
