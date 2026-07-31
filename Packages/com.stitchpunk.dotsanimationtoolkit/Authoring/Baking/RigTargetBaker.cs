// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Authoring
{
    /// <summary>
    /// Bakes a <see cref="RigTargetAuthoring"/> into the part archetype of architecture section 5.2:
    /// the binding back to its actor, the rest pose captured from this transform, the seeded output
    /// pose, and the material-property components its <see cref="TargetKind"/> needs. It also runs
    /// the managed half of bake validation — the material ↔ VAT-texture-set check of section 4.4 —
    /// because a Baker may touch managed objects and the Bursted binding system may not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dense target index is deliberately <em>not</em> resolved here. It comes from the actor's
    /// registry blob, which lives on a different entity, and a Baker may only write the entity it is
    /// baking; <see cref="RigBindingBakingSystem"/> completes the binding in
    /// <c>PostBakingSystemGroup</c>. Until it does, <see cref="RigPartBinding.targetIndex"/> stays
    /// −1, so a part whose id never resolves is inert rather than wrongly bound to target 0.
    /// </para>
    /// <para>
    /// <c>PostTransformMatrix</c> is not added by hand: requesting
    /// <see cref="TransformUsageFlags.NonUniformScale"/> makes transform baking add it, with the
    /// value <c>float4x4.Scale(localScale)</c> — identity for the ordinary unit-scaled part — while
    /// leaving <c>LocalTransform.Scale</c> at 1. That is exactly the channel
    /// <c>TransformApplySystem</c> writes each frame (section 5.6), so live scale and flip work on
    /// every part kind rather than only on the quads that happen to be authored scaled.
    /// </para>
    /// </remarks>
    public sealed class RigTargetBaker : Baker<RigTargetAuthoring>
    {
        private const string MessagePrefix = "[DOTS Animation Toolkit] ";
        private const string BoneTexturePropertyName = "_VatBoneTex";
        private const string PositionTexturePropertyName = "_VatPosTex";

        /// <inheritdoc />
        public override void Bake(RigTargetAuthoring authoring)
        {
            ActorAuthoring actorAuthoring = GetComponentInParent<ActorAuthoring>();
            if (actorAuthoring == null)
            {
                Debug.LogError(
                    MessagePrefix + "Rig target '" + authoring.name +
                    "' has no Actor component on itself or any parent, so it belongs to no actor " +
                    "and cannot be bound.",
                    authoring);
                return;
            }

            ClipSetAsset clipSet = DependsOn(actorAuthoring.clipSet);
            RigAsset partRig = DependsOn(authoring.rig);
            RigAsset actorRig = clipSet != null ? DependsOn(clipSet.rig) : null;
            RigAsset effectiveRig = ResolveEffectiveRig(authoring, partRig, actorRig);
            if (effectiveRig == null)
            {
                Debug.LogError(
                    MessagePrefix + "Rig target '" + authoring.name +
                    "' has no rig: neither the component nor the owning actor's clip set names one.",
                    authoring);
                return;
            }

            RigTargetDefinition targetDefinition = FindTargetDefinition(effectiveRig, authoring.targetStableId);
            TargetKind targetKind = ResolveTargetKind(authoring, targetDefinition);

            Entity partEntity = GetEntity(
                TransformUsageFlags.Dynamic | TransformUsageFlags.NonUniformScale);

            // actorRoot and targetIndex are both filled by RigBindingBakingSystem; the neutral values
            // here are what an unresolved part keeps.
            AddComponent(partEntity, new RigPartBinding
            {
                actorRoot = Entity.Null,
                targetIndex = -1
            });
            if (targetDefinition == null)
            {
                // Report it here rather than leaving it to the binding pass. That pass is Bursted,
                // so it can only name blittable values — an entity index and a path hash, neither of
                // which a user can act on. This baker is managed: it can name the GameObject, the
                // rig, and the id that does not exist in it, and pass the object itself as the log
                // context so clicking the message selects the offending part.
                //
                // The part is then left without a RigPartBakeLink, so the binding pass never sees it
                // and the same mistake is not reported twice in two different vocabularies.
                Debug.LogError(
                    MessagePrefix + "Rig target '" + authoring.name + "' on actor '" +
                    actorAuthoring.name + "' references target id " +
                    authoring.targetStableId.ToString() + ", which rig '" + effectiveRig.name +
                    "' does not declare. The part will not animate. Fix the Target Stable Id on " +
                    "this part, or add that target to the rig.",
                    authoring);
            }
            else
            {
                AddComponent(partEntity, new RigPartBakeLink
                {
                    actorRoot = GetEntity(actorAuthoring, TransformUsageFlags.Dynamic),
                    targetId = authoring.targetStableId,
                    authoringPath = AuthoringPathHash.PathOf(authoring.transform)
                });
            }

            TargetRestPose restPose = CaptureRestPose(authoring);
            AddComponent(partEntity, restPose);
            AddComponent(partEntity, new TargetPose
            {
                localPosition = restPose.localPosition,
                rotationZ = restPose.rotationZ,
                scale = restPose.scale,
                sliceIndex = restPose.restSliceIndex,
                atlasRect = ClipSampler.IdentityAtlasRect
            });

            // Propagated from the actor: a part animates unless some provider says otherwise (5.9).
            AddComponent<AnimVisible>(partEntity);

            AddTechniqueComponents(authoring, partEntity, targetKind, restPose);

            if (targetKind == TargetKind.VatMesh)
            {
                ValidateVatMaterial(authoring, actorAuthoring, clipSet);
            }
        }

        // -----------------------------------------------------------------------------------
        // Rig and target resolution.
        // -----------------------------------------------------------------------------------

        private RigAsset ResolveEffectiveRig(
            RigTargetAuthoring authoring,
            RigAsset partRig,
            RigAsset actorRig)
        {
            if (partRig != null && actorRig != null && partRig != actorRig)
            {
                Debug.LogError(
                    MessagePrefix + "Rig target '" + authoring.name + "' quotes its target id against rig '" +
                    partRig.name + "', but the owning actor's clip set animates rig '" + actorRig.name +
                    "'. The actor's rig is used; clear the component's rig field or fix the reference.",
                    authoring);
                return actorRig;
            }
            return partRig != null ? partRig : actorRig;
        }

        private static RigTargetDefinition FindTargetDefinition(RigAsset rig, uint targetStableId)
        {
            List<RigTargetDefinition> targets = rig.targets;
            if (targets == null || targetStableId == 0u)
            {
                return null;
            }
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                RigTargetDefinition targetDefinition = targets[targetIndex];
                if (targetDefinition != null && targetDefinition.Id.Value == targetStableId)
                {
                    return targetDefinition;
                }
            }
            return null;
        }

        /// <remarks>
        /// A target id the rig does not declare is <em>not</em> reported here: architecture
        /// section 4.1 gives that error to <see cref="RigBindingBakingSystem"/>, which is the one
        /// place that can see whether the id resolves against the actor's baked registry. Reporting
        /// it in both places would double every message.
        /// </remarks>
        private static TargetKind ResolveTargetKind(
            RigTargetAuthoring authoring,
            RigTargetDefinition targetDefinition)
        {
            if (authoring.useKindOverride)
            {
                return authoring.kindOverride;
            }
            return targetDefinition != null ? targetDefinition.kind : TargetKind.Quad;
        }

        // -----------------------------------------------------------------------------------
        // Rest pose and technique components.
        // -----------------------------------------------------------------------------------

        /// <remarks>
        /// The transform is fetched through the Baker's own <c>GetComponent</c>, not read off
        /// <c>authoring.transform</c>. Both return the same object, but only the former
        /// records a bake dependency on it. Without that dependency, dragging a part in the scene
        /// would move its rendered position (transform baking tracks its own components) while
        /// <see cref="TargetRestPose"/> kept the position captured at the last full bake — so every
        /// animated pose, which section 5.6 composes as an offset from the rest pose, would be
        /// applied against a stale origin until something unrelated forced a rebake.
        /// <c>ActorBaker.TryGetRestPoseInActorSpace</c> already takes the dependency this way.
        /// </remarks>
        private TargetRestPose CaptureRestPose(RigTargetAuthoring authoring)
        {
            Transform partTransform = GetComponent<Transform>(authoring);
            if (partTransform == null)
            {
                return new TargetRestPose
                {
                    localPosition = float3.zero,
                    rotationZ = 0f,
                    scale = new float2(1f, 1f),
                    restSliceIndex = math.max(0, authoring.restSliceIndex)
                };
            }

            Vector3 localPosition = partTransform.localPosition;
            Quaternion localRotation = partTransform.localRotation;
            Vector3 localScale = partTransform.localScale;

            // Signed rotation about z extracted from the quaternion rather than read from
            // localEulerAngles, which reports [0, 360) and would turn a −30° part into +330°.
            float rotationZ = math.atan2(
                2f * (localRotation.w * localRotation.z + localRotation.x * localRotation.y),
                1f - 2f * (localRotation.y * localRotation.y + localRotation.z * localRotation.z));

            return new TargetRestPose
            {
                localPosition = new float3(localPosition.x, localPosition.y, localPosition.z),
                rotationZ = rotationZ,
                scale = new float2(localScale.x, localScale.y),
                restSliceIndex = math.max(0, authoring.restSliceIndex)
            };
        }

        private void AddTechniqueComponents(
            RigTargetAuthoring authoring,
            Entity partEntity,
            TargetKind targetKind,
            TargetRestPose restPose)
        {
            switch (targetKind)
            {
                case TargetKind.FlipbookPlane:
                    // Both flipbook rows of the section 6.2 table: which one a clip drives is a
                    // per-track SpriteFrameMode decision, so one plane may use either across clips.
                    AddComponent(partEntity, new SpriteSliceProperty { Value = restPose.restSliceIndex });
                    AddComponent(partEntity, new AtlasFrameProperty { Value = ClipSampler.IdentityAtlasRect });
                    break;

                case TargetKind.VatMesh:
                    AddComponent(partEntity, new VatFrameAProperty { Value = 0f });
                    AddComponent(partEntity, new VatFrameBProperty { Value = 0f });
                    AddComponent(partEntity, new VatBlendProperty { Value = 0f });
                    AddComponent(partEntity, new VatDriven
                    {
                        layerIndex = (byte)math.clamp(authoring.vatDrivingLayerIndex, 0, RigAsset.MaxLayerCount - 1)
                    });
                    break;

                case TargetKind.Quad:
                default:
                    // Transform-only: the pose reaches the screen through LocalTransform and
                    // PostTransformMatrix, so a quad needs no per-instance material property.
                    break;
            }
        }

        // -----------------------------------------------------------------------------------
        // Material ↔ VAT texture set validation (section 4.4). Managed, and therefore here.
        // -----------------------------------------------------------------------------------

        private void ValidateVatMaterial(
            RigTargetAuthoring authoring,
            ActorAuthoring actorAuthoring,
            ClipSetAsset clipSet)
        {
            Material material = ResolveMaterialUnderTest(authoring);
            if (material == null)
            {
                // Nothing to compare against: a VAT part whose renderer is supplied at runtime is a
                // supported setup, and `expectedMaterial` is how such a part opts back into the check.
                return;
            }

            VatTextureSetAsset vatTextures = clipSet != null ? DependsOn(clipSet.vatTextures) : null;
            if (vatTextures == null)
            {
                Debug.LogWarning(
                    MessagePrefix + "Rig target '" + authoring.name + "' is a VatMesh part on actor '" +
                    actorAuthoring.name + "', but its clip set has no VAT texture set, so material '" +
                    material.name + "' has nothing to be validated against.",
                    authoring);
                return;
            }

            bool isBoneFlavor = vatTextures.flavor == VatFlavor.BoneMatrix;
            string texturePropertyName = isBoneFlavor
                ? BoneTexturePropertyName
                : PositionTexturePropertyName;
            Texture2D expectedTexture = isBoneFlavor
                ? DependsOn(vatTextures.boneTexture)
                : DependsOn(vatTextures.positionTexture);

            if (!material.HasProperty(texturePropertyName))
            {
                Debug.LogWarning(
                    MessagePrefix + "Material '" + material.name + "' on rig target '" + authoring.name +
                    "' declares no '" + texturePropertyName + "' slot, so it cannot display VAT texture set '" +
                    vatTextures.name + "'. Assign a VAT material to this part.",
                    authoring);
                return;
            }

            Texture boundTexture = material.GetTexture(texturePropertyName);
            if (boundTexture != expectedTexture)
            {
                Debug.LogWarning(
                    MessagePrefix + "Material '" + material.name + "' on rig target '" + authoring.name +
                    "' binds '" + texturePropertyName + "' to '" + DescribeTexture(boundTexture) +
                    "', but VAT texture set '" + vatTextures.name + "' baked '" +
                    DescribeTexture(expectedTexture) + "'. The part will animate against the wrong frames.",
                    authoring);
            }
        }

        private Material ResolveMaterialUnderTest(RigTargetAuthoring authoring)
        {
            if (authoring.expectedMaterial != null)
            {
                return DependsOn(authoring.expectedMaterial);
            }
            Renderer partRenderer = GetComponent<Renderer>();
            if (partRenderer == null)
            {
                return null;
            }
            return DependsOn(partRenderer.sharedMaterial);
        }

        private static string DescribeTexture(Texture texture)
        {
            return texture != null ? texture.name : "nothing";
        }
    }
}
