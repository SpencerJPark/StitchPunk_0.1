// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Bakes a <see cref="RigTargetAuthoring"/> into a part entity: the binding back to its actor,
    /// the rest pose, the seeded output pose, and the material-property components its
    /// <see cref="TargetKind"/> needs. Also runs the managed material/VAT-texture-set check, since a
    /// Baker can touch managed objects and the Bursted binding system cannot.
    /// </summary>
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

            RigAsset partRig = DependsOn(authoring.rig);
            ActorProfileAsset actorProfile = DependsOn(actorAuthoring.profile);
            RigAsset actorRig = DependsOn(actorProfile != null ? actorProfile.rig : null);
            RigAsset effectiveRig = ResolveEffectiveRig(authoring, partRig, actorRig);
            if (effectiveRig == null)
            {
                Debug.LogError(
                    MessagePrefix + "Rig target '" + authoring.name +
                    "' has no rig: neither the component nor the owning actor names one.",
                    authoring);
                return;
            }

            RigTargetDefinition targetDefinition = FindTargetDefinition(effectiveRig, authoring.targetStableId);
            TargetKind targetKind = ResolveTargetKind(authoring, targetDefinition);

            // NonUniformScale makes transform baking add PostTransformMatrix (identity for an
            // ordinary unit-scaled part) while LocalTransform.Scale stays 1 — the exact channel
            // TransformApplySystem writes each frame, so live scale/flip work on every part kind.
            Entity partEntity = GetEntity(
                TransformUsageFlags.Dynamic | TransformUsageFlags.NonUniformScale);

            // The dense target index is resolved later by RigBindingBakingSystem, on the actor's own
            // entity — a baker may only write the entity it is baking, and the registry blob that
            // index comes from lives on the actor. The neutral values here are what an unresolved
            // part keeps.
            AddComponent(partEntity, new RigPartBinding
            {
                actorRoot = Entity.Null,
                targetIndex = -1
            });
            if (targetDefinition == null)
            {
                // Reported here, not by the (Bursted, managed-object-free) binding pass, so the
                // message can name the GameObject, the rig, and pass the object as log context for
                // click-to-select. The part is left without a RigPartBakeLink so the binding pass
                // never sees it and the mistake is not reported a second time.
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
                    authoringPath = AuthoringPathHash.PathOf(this, authoring.transform)
                });
            }

            TargetRestPose restPose = CaptureRestPose(authoring);
            AddComponent(partEntity, restPose);
            AddComponent(partEntity, new TargetPose
            {
                localPosition = restPose.localPosition,
                rotation = restPose.rotation,
                scale = restPose.scale,
                sliceIndex = restPose.restSliceIndex,
                atlasRect = ClipSampler.IdentityAtlasRect
            });

            // Propagated from the actor: a part animates unless some provider says otherwise.
            AddComponent<AnimVisible>(partEntity);

            // The opt-in is the target's explicit facesDirection, not framesPerVariant > 1 — that
            // looked tidier but was wrong: framesPerVariant describes alt-view blocks, and a
            // mirror-only target (e.g. a nose that just flips) has no blocks at all.
            if (targetDefinition != null && targetDefinition.facesDirection)
            {
                AddComponent(partEntity, new PartFacing
                {
                    viewOffset = 0,
                    mirrorX = authoring.startMirrored
                });

                // A mirror point flips its whole subtree, so a facing part under another facing part
                // must not flip a second time and cancel it. Decided here, where the hierarchy is
                // known, so the sampler pays one lookup rather than a walk.
                if (HasFacingAncestorPart(authoring, effectiveRig))
                {
                    AddComponent<PartMirrorFromAncestor>(partEntity);
                }
            }

            AddBillboardMember(authoring, actorAuthoring, effectiveRig, partEntity);

            AddTechniqueComponents(authoring, actorAuthoring, partEntity, targetKind, restPose);

            if (targetKind == TargetKind.VatMesh)
            {
                ValidateVatMaterial(authoring, actorAuthoring);
            }
        }

        // Walked through the baker's own GetParent/GetComponent, not GetComponentInParent, so every
        // node visited registers as a baking dependency: re-parenting a part, or ticking Faces
        // Direction on an ancestor, has to re-bake this one.
        private bool HasFacingAncestorPart(RigTargetAuthoring authoring, RigAsset effectiveRig)
        {
            GameObject ancestor = GetParent(authoring.gameObject);
            while (ancestor != null)
            {
                RigTargetAuthoring ancestorPart = GetComponent<RigTargetAuthoring>(ancestor);
                if (ancestorPart != null)
                {
                    RigTargetDefinition ancestorTarget =
                        FindTargetDefinition(effectiveRig, ancestorPart.targetStableId);
                    if (ancestorTarget != null && ancestorTarget.facesDirection)
                    {
                        return true;
                    }
                }
                ancestor = GetParent(ancestor);
            }
            return false;
        }

        // Only the nearest ancestor root is stored (the walk is inclusive of the part itself, so a
        // part that IS a root names itself — the override that lets a held item billboard
        // independently of its holder). Named by id, not by buffer position: this baker and
        // ActorBaker resolve the hierarchy independently and have no shared ordering to agree on.
        private void AddBillboardMember(
            RigTargetAuthoring authoring,
            ActorAuthoring actorAuthoring,
            RigAsset effectiveRig,
            Entity partEntity)
        {
            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(effectiveRig, actorAuthoring.transform, null);

            int nearestRootIndex = BillboardRootResolver.FindNearestRootIndex(
                resolvedRoots, authoring.transform, actorAuthoring.transform);

            uint rootId;
            if (nearestRootIndex >= 0)
            {
                rootId = resolvedRoots[nearestRootIndex].definition.stableId;
            }
            else if (actorAuthoring.billboardMode != BillboardMode.Off)
            {
                // Whole-actor billboard: the implicit root ActorBaker bakes with id 0. Every part
                // inherits it, since it sits on the actor root.
                rootId = 0u;
            }
            else
            {
                return;
            }

            AddComponent(partEntity, new BillboardMember
            {
                actorRoot = GetEntity(actorAuthoring, TransformUsageFlags.Dynamic),
                rootId = rootId
            });
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
                    partRig.name + "', but the owning actor animates rig '" + actorRig.name +
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

        // A null targetDefinition means the rig does not declare the part's target id; Bake has
        // already reported that and withheld the RigPartBakeLink, so nothing is reported again here
        // — the part falls back to Quad so its entity is well formed. An explicit useKindOverride
        // still wins, since a part whose id is wrong may still have the right technique authored.
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

        // Fetched through the Baker's own GetComponent<Transform>, not authoring.transform directly:
        // both return the same object, but only GetComponent registers the bake dependency. Without
        // it, dragging a part would move its rendered position while TargetRestPose kept the stale
        // captured one, until something unrelated forced a rebake. The over-invalidation this costs
        // (GetComponent<Transform> also depends on the whole ancestor chain) is accepted knowingly.
        private TargetRestPose CaptureRestPose(RigTargetAuthoring authoring)
        {
            // Through RestPoseCapture, not inline, so the Cutscene Editor's preview poses real scene
            // parts against the identical rest pose.
            return RestPoseCapture.FromTransform(
                GetComponent<Transform>(authoring), authoring.restSliceIndex);
        }

        private void AddTechniqueComponents(
            RigTargetAuthoring authoring,
            ActorAuthoring actorAuthoring,
            Entity partEntity,
            TargetKind targetKind,
            TargetRestPose restPose)
        {
            switch (targetKind)
            {
                case TargetKind.FlipbookPlane:
                    // Which row a clip drives is a per-track SpriteFrameMode decision, so one plane
                    // may use either across clips.
                    AddComponent(partEntity, new SpriteSliceProperty { Value = restPose.restSliceIndex });
                    AddComponent(partEntity, new AtlasFrameProperty { Value = ClipSampler.IdentityAtlasRect });
                    break;

                case TargetKind.VatMesh:
                    AddComponent(partEntity, new VatFrameAProperty { Value = 0f });
                    AddComponent(partEntity, new VatFrameBProperty { Value = 0f });
                    AddComponent(partEntity, new VatBlendProperty { Value = 0f });
                    AddComponent(partEntity, new VatDriven
                    {
                        layerIndex = (byte)math.clamp(authoring.vatDrivingLayerIndex, 0, ActorProfileAsset.MaxLayerCount - 1)
                    });
                    AddVatPartTextureBinding(authoring, actorAuthoring, partEntity);
                    break;

                case TargetKind.Quad:
                default:
                    // Transform-only: the pose reaches the screen through LocalTransform and
                    // PostTransformMatrix, so a quad needs no per-instance material property.
                    break;
            }
        }

        // The part-level binding this target's own baked part uses, resolved the same way
        // ValidateVatMaterial resolves it below; a target with no matching part gets no binding,
        // and the material check is where that mismatch gets reported.
        private void AddVatPartTextureBinding(
            RigTargetAuthoring authoring,
            ActorAuthoring actorAuthoring,
            Entity partEntity)
        {
            VatTextureSetAsset vatTextures = ResolveBindVatTextures(actorAuthoring);
            if (vatTextures == null || !vatTextures.TryGetPart(authoring.targetStableId, out VatPartTextures part))
            {
                return;
            }

            bool isBoneFlavor = vatTextures.flavor == VatFlavor.BoneMatrix;
            Texture2D boneOrPositionTexture = isBoneFlavor
                ? DependsOn(part.boneTexture)
                : DependsOn(part.positionTexture);

            AddComponent(partEntity, new VatPartTextureBinding
            {
                boneOrPositionTexture = boneOrPositionTexture,
                normalTexture = DependsOn(part.normalTexture)
            });
        }

        // -----------------------------------------------------------------------------------
        // Material <-> VAT texture set validation. Managed, and therefore here.
        // -----------------------------------------------------------------------------------

        /// <summary>The one VAT texture set the owning actor's bind addresses (a second is a V39 error, so the first one found is the answer).</summary>
        private VatTextureSetAsset ResolveBindVatTextures(ActorAuthoring actorAuthoring)
        {
            List<ClipSetAsset> clipSets = actorAuthoring.profile != null ? actorAuthoring.profile.clipSets : null;
            if (clipSets == null)
            {
                return null;
            }
            for (int setIndex = 0; setIndex < clipSets.Count; setIndex++)
            {
                ClipSetAsset clipSet = clipSets[setIndex];
                if (clipSet == null)
                {
                    continue;
                }
                VatTextureSetAsset vatTextures = DependsOn(clipSet.vatTextures);
                if (vatTextures != null)
                {
                    return vatTextures;
                }
            }
            return null;
        }

        private void ValidateVatMaterial(
            RigTargetAuthoring authoring,
            ActorAuthoring actorAuthoring)
        {
            Material material = ResolveMaterialUnderTest(authoring);
            if (material == null)
            {
                // Nothing to compare against: a VAT part whose renderer is supplied at runtime is a
                // supported setup, and expectedMaterial is how such a part opts back into the check.
                return;
            }

            VatTextureSetAsset vatTextures = ResolveBindVatTextures(actorAuthoring);
            if (vatTextures == null)
            {
                Debug.LogWarning(
                    MessagePrefix + "Rig target '" + authoring.name + "' is a VatMesh part on actor '" +
                    actorAuthoring.name + "', but none of its clip sets supplies a VAT texture set, " +
                    "so material '" + material.name + "' has nothing to be validated against.",
                    authoring);
                return;
            }

            if (!vatTextures.TryGetPart(authoring.targetStableId, out VatPartTextures part))
            {
                Debug.LogWarning(
                    MessagePrefix + "Rig target '" + authoring.name + "' is a VatMesh part on actor '" +
                    actorAuthoring.name + "', but VAT texture set '" + vatTextures.name +
                    "' baked no part for this target, so material '" + material.name +
                    "' has nothing to be validated against.",
                    authoring);
                return;
            }

            bool isBoneFlavor = vatTextures.flavor == VatFlavor.BoneMatrix;
            string texturePropertyName = isBoneFlavor
                ? BoneTexturePropertyName
                : PositionTexturePropertyName;
            Texture2D expectedTexture = isBoneFlavor
                ? DependsOn(part.boneTexture)
                : DependsOn(part.positionTexture);

            if (!material.HasProperty(texturePropertyName))
            {
                Debug.LogWarning(
                    MessagePrefix + "Material '" + material.name + "' on rig target '" + authoring.name +
                    "' declares no '" + texturePropertyName + "' slot, so it cannot display part '" +
                    part.displayName + "' of VAT texture set '" + vatTextures.name +
                    "'. Assign a VAT material to this part.",
                    authoring);
                return;
            }

            Texture boundTexture = material.GetTexture(texturePropertyName);
            if (boundTexture != expectedTexture)
            {
                Debug.LogWarning(
                    MessagePrefix + "Material '" + material.name + "' on rig target '" + authoring.name +
                    "' binds '" + texturePropertyName + "' to '" + DescribeTexture(boundTexture) +
                    "', but part '" + part.displayName + "' of VAT texture set '" + vatTextures.name +
                    "' baked '" + DescribeTexture(expectedTexture) +
                    "'. The part will animate against the wrong frames.",
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
