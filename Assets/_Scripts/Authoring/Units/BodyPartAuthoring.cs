using System.Collections.Generic;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Serialization;

// One per part GameObject in a character rig (quad or item socket). CharacterRigBakingSystem
// assembles the root's BodyPart buffer from these. Rest pose and ragdoll bodies belong to the toolkit.
public class BodyPartAuthoring : MonoBehaviour
{
    [Tooltip("Which body part this GameObject is. Stays the single part-identity key everywhere.")]
    public AnimationTarget target;

    [Tooltip("The character root GameObject this part belongs to (baked into BaseParent).")]
    public GameObject characterRoot;

    [FormerlySerializedAs("partDef")]
    [Tooltip("Static config for this part KIND (design grid + ragdoll zones). Optional — leave null " +
             "for parts with no design variants and no ragdoll config; the part still animates.")]
    public UnitPartSO unitPartDef;

    [Tooltip("Per-instance multiply tint for this part's sprite (drives _BaseColor). White = authored " +
             "colour unchanged; black outline survives (0 * tint = 0). Ignored for non-rendering parts. " +
             "Placeholder until a global palette/skin system writes the colour.")]
    public Color tintColor = Color.white;

    [Tooltip("This part's packed mask genuinely uses the G/B layer channels (e.g. a bloody-mouth " +
             "overlay). Unchecked (default) bakes the secondary/tertiary layer tints with ALPHA 0 — " +
             "PackedChannelRecolor treats alpha as layer blend strength, so stray G/B data in the " +
             "texture can never composite. Palette designs that write those slots re-activate the " +
             "layers regardless (their colour alpha wins at apply).")]
    public bool useLayerChannels;

    [Tooltip("This part is an item attach socket (e.g. ItemLeftHand). Flagged in the BodyPart buffer.")]
    public bool isItemSocket;

    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<BodyPartAuthoring>
    {
        public override void Bake(BodyPartAuthoring authoring)
        {
            bool hasRenderer = authoring.GetComponent<Renderer>() != null;

            Entity entity = GetEntity(hasRenderer
                ? TransformUsageFlags.Dynamic | TransformUsageFlags.NonUniformScale
                : TransformUsageFlags.Dynamic);

            UnitPartId unitPartId = UnitPartId.None;
            if (authoring.unitPartDef != null)
            {
                DependsOn(authoring.unitPartDef);
                unitPartId = authoring.unitPartDef.id;
            }

            BodyPartFlags flags = BodyPartFlags.None;
            if (hasRenderer)                     flags |= BodyPartFlags.HasQuad;
            if (authoring.unitPartDef != null)   flags |= BodyPartFlags.DesignSlot;
            if (authoring.isItemSocket)          flags |= BodyPartFlags.ItemSocket;

            AddComponent(entity, new BodyPartInfo
            {
                target  = authoring.target,
                unitPart = unitPartId,
                flags   = flags,
            });

            if (authoring.characterRoot != null)
            {
                Entity rootEntity = GetEntity(authoring.characterRoot, TransformUsageFlags.Dynamic);
                AddComponent(entity, new BaseParent { baseParentEntity = rootEntity });
            }

            AddComponent(entity, new PostTransformMatrix { Value = float4x4.identity });

            // Starts visible; CameraVisibilitySystem propagates the root's state here.
            AddComponent<CameraVisible>(entity);
            SetComponentEnabled<CameraVisible>(entity, true);

            if (hasRenderer)
            {
                // Skipped where RigTargetBaker already added it: a duplicate component is a bake error.
                if (!FacesDirectionAlreadyBaked(authoring))
                {
                    AddComponent(entity, new PartFacing { viewOffset = 0, mirrorX = false });
                }

                // sRGB to linear: the DOTS MaterialProperty upload is raw and the project renders in Linear.
                Color linearTint = authoring.tintColor.linear;
                AddComponent(entity, new BodyPartTint
                {
                    Value = new float4(linearTint.r, linearTint.g, linearTint.b, linearTint.a),
                });
                // Alpha is layer blend strength in PackedChannelRecolor; 0 stops stray G/B data compositing.
                // Present on every rendering part to keep one batchable archetype.
                float layerAlpha = authoring.useLayerChannels ? 1f : 0f;
                AddComponent(entity, new BodyPartSecondaryTint { Value = new float4(1f, 1f, 1f, layerAlpha) });
                AddComponent(entity, new BodyPartTertiaryTint { Value = new float4(1f, 1f, 1f, layerAlpha) });
                AddComponent(entity, new BodyPartFlash { Value = new float4(1f, 1f, 1f, 0f) });
            }
        }

        // Mirrors RigTargetBaker's rig resolution (target's own rig, else the actor's) so the two
        // bakers agree without sharing code across the package boundary.
        private bool FacesDirectionAlreadyBaked(BodyPartAuthoring authoring)
        {
            RigTargetAuthoring rigTarget = GetComponent<RigTargetAuthoring>();
            if (rigTarget == null) return false;

            ActorAuthoring actorAuthoring = GetComponentInParent<ActorAuthoring>();
            RigAsset partRig = DependsOn(rigTarget.rig);
            RigAsset actorRig = actorAuthoring != null && actorAuthoring.profile != null
                ? DependsOn(actorAuthoring.profile.rig)
                : null;
            RigAsset effectiveRig = partRig != null ? partRig : actorRig;
            if (effectiveRig == null) return false;

            List<RigTargetDefinition> targets = effectiveRig.targets;
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                RigTargetDefinition target = targets[targetIndex];
                if (target != null && target.Id.Value == rigTarget.targetStableId)
                    return target.facesDirection;
            }
            return false;
        }
    }
}
