// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Binds one GameObject under an <see cref="ActorAuthoring"/> to one rig target. Bake captures
    /// this transform as the target's rest pose and records the binding for
    /// <c>RigBindingBakingSystem</c> to resolve into a dense target index.
    /// </summary>
    [AddComponentMenu("DOTS Animation Toolkit/Rig Target")]
    [DisallowMultipleComponent]
    public sealed class RigTargetAuthoring : MonoBehaviour
    {
        [Tooltip("Rig this part's target id belongs to. Leave empty to inherit the actor's rig.")]
        public RigAsset rig;

        // Bound by id, never by name or sibling order, so renaming or reparenting a part never
        // re-binds it. A part whose id matches no target in the rig is reported at bake and skipped.
        [Tooltip("Stable id of the rig target this part represents.")]
        public uint targetStableId;

        [Tooltip("Use kindOverride instead of the kind authored on the rig target.")]
        public bool useKindOverride;

        [Tooltip("Presentation kind to bake when the override switch above is set.")]
        public TargetKind kindOverride = TargetKind.Quad;

        [Tooltip("Texture2DArray slice this part shows at rest, before any sprite track runs.")]
        [Min(0)]
        public int restSliceIndex;

        [Tooltip("Start this part horizontally mirrored. Requires the rig target to set Faces Direction.")]
        public bool startMirrored;

        [Tooltip("VatMesh parts only: index of the playback layer whose clip drives the VAT frames.")]
        [Min(0)]
        public int vatDrivingLayerIndex;

        [Tooltip("Material to validate against the actor's VAT texture set when this part has no renderer.")]
        public Material expectedMaterial;
    }
}
