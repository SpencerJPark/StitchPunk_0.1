using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// One per dedicated ragdoll joint empty (arm/leg/neck bend pivots). Owns the joint's PHYSICS config,
// fully separate from the design pipeline: references a shared RagdollJointSO (per joint kind) and
// bakes the resolved values into RagdollJointBakeData (bake-only) + the RagdollLandingZone buffer.
// CharacterRigBakingSystem stamps Ragdoll2DJoint (disabled) from the bake data; Ragdoll2DInitSystem
// rolls the landing angle from the zone buffer on death. Sits next to BodyPartAuthoring on the same
// GameObject — BodyPartAuthoring detects this component and sets the RagdollJoint flag in the rig
// registry.
public class RagdollJointAuthoring : MonoBehaviour
{
    [Tooltip("Shared physics config for this joint KIND (settle speed, flail pendulum, landing zones).")]
    public RagdollJointSO joint;

    [Tooltip("Per-placement settle speed (deg/s). 0 = use the RagdollJointSO value.")]
    public float settleSpeedOverride;

    [Tooltip("Reserved per-placement ragdoll ground buffer override. 0 = use the global root buffer.")]
    public float groundBufferOverride;

    public class Baker : Baker<RagdollJointAuthoring>
    {
        public override void Bake(RagdollJointAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            float settleSpeed   = 8f;
            float segmentLength = 0.5f;
            float weight        = 1f;

            if (authoring.joint != null)
            {
                DependsOn(authoring.joint);
                if (authoring.joint.settleSpeed > 0f)   settleSpeed   = authoring.joint.settleSpeed;
                if (authoring.joint.segmentLength > 0f) segmentLength = authoring.joint.segmentLength;
                if (authoring.joint.weight > 0f)        weight        = authoring.joint.weight;
            }

            if (authoring.settleSpeedOverride > 0f)
                settleSpeed = authoring.settleSpeedOverride;

            AddComponent(entity, new RagdollJointBakeData
            {
                settleSpeed          = settleSpeed,
                segmentLength        = segmentLength,
                weight               = weight,
                groundBufferOverride = authoring.groundBufferOverride,
            });

            DynamicBuffer<RagdollLandingZone> zones = AddBuffer<RagdollLandingZone>(entity);
            if (authoring.joint != null && authoring.joint.zones != null)
            {
                foreach (LandingZone landingZone in authoring.joint.zones)
                    zones.Add(new RagdollLandingZone { zone = new float2(landingZone.min, landingZone.max) });
            }
        }
    }
}
