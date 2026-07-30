// Copyright (c) 2026 Stitch Punk. All rights reserved.

using UnityEngine;

namespace StitchPunk.AnimationToolkit.Authoring
{
    /// <summary>
    /// Binds one GameObject under an <see cref="ActorAuthoring"/> to one rig target
    /// (architecture sections 3.1, 4.1). Bake captures this transform as the target's
    /// <see cref="TargetRestPose"/>, adds the material-property components its
    /// <see cref="TargetKind"/> needs, and records the binding that
    /// <c>RigBindingBakingSystem</c> resolves into a dense target index.
    /// </summary>
    /// <remarks>
    /// The part is bound by <see cref="targetStableId"/>, never by name and never by sibling order,
    /// so renaming or reparenting a part inside the actor never re-binds it (architecture
    /// section 3.4). A part whose id is not a target of the actor's rig is reported by
    /// <c>RigBindingBakingSystem</c> and skipped; the rest of the actor still bakes.
    /// </remarks>
    [AddComponentMenu("DOTS Animation Toolkit/Rig Target")]
    [DisallowMultipleComponent]
    public sealed class RigTargetAuthoring : MonoBehaviour
    {
        /// <summary>
        /// The rig this part's <see cref="targetStableId"/> is quoted against. Supplies the target's
        /// authored <see cref="TargetKind"/> and half-extents. Leave it null to inherit the rig of
        /// the owning actor's clip set, which is the usual case; setting it to a
        /// <em>different</em> rig is an authoring error and is reported at bake.
        /// </summary>
        [Tooltip("Rig this part's target id belongs to. Leave empty to inherit the actor's rig.")]
        public RigAsset rig;

        /// <summary>
        /// Stable id of the rig target this part represents (architecture section 3.4). 0 is the
        /// reserved unassigned value and never resolves.
        /// </summary>
        [Tooltip("Stable id of the rig target this part represents.")]
        public uint targetStableId;

        /// <summary>
        /// Presents this part as <see cref="kindOverride"/> instead of the kind authored on the rig
        /// target. <see cref="TargetKind"/> has no "unset" member — <see cref="TargetKind.Quad"/> is
        /// its zero value — so the override needs this explicit switch to be distinguishable from a
        /// freshly added component.
        /// </summary>
        [Tooltip("Use kindOverride instead of the kind authored on the rig target.")]
        public bool useKindOverride;

        /// <summary>
        /// The presentation kind to bake when <see cref="useKindOverride"/> is set. Decides which
        /// material-property components the part entity receives (architecture sections 4.1, 6.2).
        /// </summary>
        [Tooltip("Presentation kind to bake when the override switch above is set.")]
        public TargetKind kindOverride = TargetKind.Quad;

        /// <summary>
        /// The rest sprite frame this part shows before any sprite track overrides it, baked into
        /// <see cref="TargetRestPose.restSliceIndex"/> (architecture section 5.7). Host design or
        /// skin systems change a part's base look by rewriting that field at runtime.
        /// </summary>
        [Tooltip("Texture2DArray slice this part shows at rest, before any sprite track runs.")]
        [Min(0)]
        public int restSliceIndex;

        /// <summary>
        /// For <see cref="TargetKind.VatMesh"/> parts: which playback layer's clip drives the VAT
        /// frame properties, baked into <see cref="VatDriven.layerIndex"/> (architecture
        /// section 5.8). Ignored by every other kind.
        /// </summary>
        [Tooltip("VatMesh parts only: index of the playback layer whose clip drives the VAT frames.")]
        [Min(0)]
        public int vatDrivingLayerIndex;

        /// <summary>
        /// The material to check against the actor's VAT texture set instead of this GameObject's
        /// renderer material (architecture section 4.4). Set it when the part has no renderer at bake
        /// time — a runtime-instantiated mesh, or a renderer supplied by a host system — so the
        /// bake-time texture-set check still has something to compare. Purely a validation input:
        /// nothing about it reaches the baked entity.
        /// </summary>
        [Tooltip("Material to validate against the actor's VAT texture set when this part has no renderer.")]
        public Material expectedMaterial;
    }
}
