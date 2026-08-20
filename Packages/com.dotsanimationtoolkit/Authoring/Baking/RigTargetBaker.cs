// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
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
                // Amendment A22 moved this error here from the binding pass. That pass is Bursted,
                // so it can only name blittable values; this baker is managed, so it can name the
                // GameObject, the rig, and the id that does not exist in it, and pass the object
                // itself as the log context so clicking the message selects the offending part.
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

            // Propagated from the actor: a part animates unless some provider says otherwise (5.9).
            AddComponent<AnimVisible>(partEntity);

            // Amendment A37. The opt-in is the target's explicit `facesDirection`, NOT
            // `framesPerVariant > 1` as the first cut had it. That derivation looked tidier — one
            // source of truth instead of two flags — but it was wrong: framesPerVariant describes
            // alt-view blocks, and a mirror-only target (a nose that simply flips) has no blocks at
            // all. Deriving the opt-in from it silently excluded exactly those parts, so mirroring
            // was inert on the first rig that tried it.
            // To revert: drop this block and the component is simply never baked.
            if (targetDefinition != null && targetDefinition.facesDirection)
            {
                AddComponent(partEntity, new PartFacing
                {
                    viewOffset = 0,
                    mirrorX = authoring.startMirrored
                });
            }

            AddBillboardMember(authoring, actorAuthoring, effectiveRig, partEntity);

            AddTechniqueComponents(authoring, partEntity, targetKind, restPose);

            if (targetKind == TargetKind.VatMesh)
            {
                ValidateVatMaterial(authoring, actorAuthoring, clipSet);
            }
        }

        /// <summary>
        /// Records which billboard root this part inherits, when it inherits one (amendment A44).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Only the nearest ancestor root is stored, and only when there is one.</strong>
        /// The walk is inclusive of the part itself, so a part that <em>is</em> a billboard root
        /// names itself — which is the override rule, and what lets a held item billboard
        /// independently of the character holding it.
        /// </para>
        /// <para>
        /// A part under no root gets no component, keeping billboarding as opt-in as
        /// <c>AnimLod</c> and <c>PartFacing</c> (amendment A23's precedent).
        /// </para>
        /// <para>
        /// The root is named by <em>id</em> rather than by its position in the actor's baked buffer.
        /// Two bakers resolve this hierarchy independently — this one and <c>ActorBaker</c> — and an
        /// index would require them to agree on an ordering neither can see the other compute. An
        /// id is authored data both simply read.
        /// </para>
        /// </remarks>
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
                // The whole-actor billboard A41 shipped, expressed as the implicit root ActorBaker
                // bakes with id 0. Every part inherits it, because it sits on the actor root.
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
        /// A null <paramref name="targetDefinition"/> means the rig does not declare the part's
        /// target id. <see cref="Bake"/> has already reported that (architecture section 4.1,
        /// amendment A22) and withheld the part's <see cref="RigPartBakeLink"/>, so nothing is
        /// reported a second time here — the part simply falls back to <see cref="TargetKind.Quad"/>
        /// so its entity is well formed rather than half built. An explicit
        /// <c>useKindOverride</c> still wins, because a part whose id is wrong may still have been
        /// authored with the right technique.
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
        /// <para>
        /// The cost is over-invalidation, and it is accepted knowingly: <c>GetComponent</c> on a
        /// <c>Transform</c> also registers a dependency on the <em>whole</em> parent hierarchy,
        /// because <c>transform.position</c> and friends are computed from every ancestor. This
        /// method reads only <c>localPosition</c> / <c>localRotation</c> / <c>localScale</c>, none
        /// of which an ancestor can change, so dragging the actor root re-runs this baker for every
        /// part beneath it without any baked byte differing. Correctness beats bake speed here, and
        /// the narrower alternative — <c>DependsOn(authoring.transform)</c> — does not register a
        /// transform-value dependency at all.
        /// </para>
        /// <para>
        /// The result is returned unconditionally: every GameObject has a Transform, and
        /// <c>GetComponentInternal</c> resolves it through <c>TryGetComponent</c> on that
        /// GameObject, so the lookup cannot fail. A null guard here would be unreachable code whose
        /// only possible behaviour — fabricating an identity rest pose in silence — is worse than
        /// the null-reference it would be hiding.
        /// </para>
        /// </remarks>
        private TargetRestPose CaptureRestPose(RigTargetAuthoring authoring)
        {
            Transform partTransform = GetComponent<Transform>(authoring);
            Vector3 localPosition = partTransform.localPosition;
            Quaternion localRotation = partTransform.localRotation;
            Vector3 localScale = partTransform.localScale;

            // Signed Euler angles taken from the quaternion in the same ZXY order the pose is
            // rebuilt with, rather than from localEulerAngles, which reports [0, 360) and would turn
            // a −30° part into +330°.
            float3 restRotation = ExtractZxyEulerRadians(localRotation);

            return new TargetRestPose
            {
                localPosition = new float3(localPosition.x, localPosition.y, localPosition.z),
                rotation = restRotation,
                scale = new float3(localScale.x, localScale.y, localScale.z),
                restSliceIndex = math.max(0, authoring.restSliceIndex)
            };
        }

        /// <summary>
        /// Signed ZXY Euler angles, in radians, from a rotation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ZXY because that is the order <c>quaternion.Euler</c> and <c>Transform.eulerAngles</c>
        /// both use, and <c>TransformApplySystem</c> rebuilds the pose with the former. Extracting
        /// in a different order than the one used to rebuild would give a rest pose that is correct
        /// only while two of the three angles are zero — that is, correct on every flat rig and
        /// wrong on the first tilted one.
        /// </para>
        /// <para>
        /// Signed, in (−π, π], rather than <c>localEulerAngles</c>'s [0, 360): a part authored at
        /// −30° must not come back as +330°, because the two compose differently once a clip adds
        /// a delta to them.
        /// </para>
        /// </remarks>
        private static float3 ExtractZxyEulerRadians(Quaternion rotation)
        {
            float x = rotation.x;
            float y = rotation.y;
            float z = rotation.z;
            float w = rotation.w;

            // sin(pitch) for the ZXY order; clamped because a value a hair outside [-1, 1] from
            // accumulated float error would make asin return NaN at exactly the poles.
            float sinPitch = math.clamp(2f * (w * x + y * z), -1f, 1f);
            float pitch = math.asin(sinPitch);

            float cosPitch = math.sqrt(math.max(0f, 1f - sinPitch * sinPitch));
            if (cosPitch < 1e-6f)
            {
                // Gimbal lock: yaw and roll describe the same turn, so the split between them is
                // arbitrary. Putting all of it in yaw is the conventional choice and keeps the
                // rebuilt rotation identical.
                return new float3(pitch, 2f * math.atan2(y, w), 0f);
            }

            float yaw = math.atan2(2f * (w * y - z * x), 1f - 2f * (x * x + y * y));
            float roll = math.atan2(2f * (w * z - x * y), 1f - 2f * (x * x + z * z));
            return new float3(pitch, yaw, roll);
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
