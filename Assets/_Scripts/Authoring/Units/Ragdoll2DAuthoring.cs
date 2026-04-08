using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Place ONLY on the root body entity (the empty at ground level).
/// Drag in the visual child and all joint pivot GameObjects here.
///
/// The baker ONLY writes to the root entity (DOTS baking rule: each baker owns its GO's entity).
/// Ragdoll2DBakingSystem then adds Ragdoll2D / Ragdoll2DJoint to the child entities.
/// </summary>
public class Ragdoll2DAuthoring : MonoBehaviour
{
    [Tooltip("The direct visual child of root that holds the whole character (this is what tilts on Z).")]
    public GameObject visualChild;

    [Tooltip("Each joint pivot GO plus an optional per-joint ground buffer override. " +
             "Set groundBuffer > 0 to override the global buffer for that joint. " +
             "Rule of thumb: set it to the length of the longest limb segment below that pivot " +
             "so the limb tip can never reach the ground plane.")]
    public List<Ragdoll2DJointEntry> joints = new();

    [Tooltip("Angular speed at which the body tips over (deg/s). 180 = reaches 90° in ~0.5s.")]
    public float bodyFallSpeed = 180f;

    [FormerlySerializedAs("groundBuffer")]
    [Tooltip("Global ground buffer when falling FORWARD (fallSideSign >= 0): how far above root.Y to clamp all joints. " +
             "Also lifts the visual body child off the ground by this amount.")]
    public float groundBufferForward = 0.15f;

    [Tooltip("Global ground buffer when falling BACKWARD (fallSideSign < 0). " +
             "Separate value because limb geometry differs between forward and backward falls. " +
             "Falls back to groundBuffer if left at 0.")]
    public float groundBufferBackward = 0.15f;

    [Tooltip("Additive degrees on top of the base 88° tilt target when falling FORWARD (fallSideSign >= 0). " +
             "Positive pushes further past flat; negative stops the fall short.")]
    public float tiltOffsetForward = 0f;

    [Tooltip("Additive degrees on top of the base 88° tilt target when falling BACKWARD (fallSideSign < 0). " +
             "Negative pushes further past flat; positive stops the fall short.")]
    public float tiltOffsetBackward = 0f;


    public class Baker : Baker<Ragdoll2DAuthoring>
    {
        public override void Bake(Ragdoll2DAuthoring authoring)
        {
            if (authoring.visualChild == null)
            {
                Debug.LogError($"Ragdoll2DRootAuthoring on {authoring.gameObject.name}: visualChild is not assigned.");
                return;
            }

            Entity rootEntity       = GetEntity(TransformUsageFlags.Dynamic);
            Entity visualRootEntity = GetEntity(authoring.visualChild, TransformUsageFlags.Dynamic);

            // Only write to rootEntity — Ragdoll2DBakingSystem handles the child entities.
            // Ragdoll2DLaunch is on the root entity so we can bake it here directly.
            AddComponent<Ragdoll2DLaunch>(rootEntity);
            SetComponentEnabled<Ragdoll2DLaunch>(rootEntity, false);

            float bwd = authoring.groundBufferBackward > 0f ? authoring.groundBufferBackward : authoring.groundBufferForward;
            AddComponent(rootEntity, new Ragdoll2DConfig
            {
                visualRoot           = visualRootEntity,
                groundBufferForward  = authoring.groundBufferForward,
                groundBufferBackward = bwd,
                tiltOffsetForward    = authoring.tiltOffsetForward,
                tiltOffsetBackward   = authoring.tiltOffsetBackward,
                fallSpeed            = authoring.bodyFallSpeed
            });

            var jointBuffer = AddBuffer<Ragdoll2DJointRef>(rootEntity);
            var zoneBuffer  = AddBuffer<Ragdoll2DJointZone>(rootEntity);
            int zoneIndex   = 0;
            foreach (var entry in authoring.joints)
            {
                if (entry.joint == null) continue;
                int zoneStart = zoneIndex;
                if (entry.landingZones != null)
                {
                    foreach (var zone in entry.landingZones)
                    {
                        zoneBuffer.Add(new Ragdoll2DJointZone { min = zone.min, max = zone.max });
                        zoneIndex++;
                    }
                }
                jointBuffer.Add(new Ragdoll2DJointRef
                {
                    joint        = GetEntity(entry.joint, TransformUsageFlags.Dynamic),
                    settleSpeed  = entry.settleSpeed > 0f ? entry.settleSpeed : 8f,
                    zoneStart    = zoneStart,
                    zoneCount    = zoneIndex - zoneStart,
                });
            }
        }
    }
}

[System.Serializable]
public struct Ragdoll2DJointEntry
{
    public GameObject joint;
    [Tooltip("Exponential settle rate. Higher = snappier flop. 0 = use default (8). " +
             "Typical range: 2 (slow, heavy) → 5 (natural flop) → 15 (snappy).")]
    public float settleSpeed;
    [Tooltip("One or more angle ranges (degrees, local Z) where this joint can land. " +
             "One zone is picked randomly at death; the joint moves to a random angle within it. " +
             "E.g. add two zones: (-170, -150) and (0, 5) for over-head or along-body.")]
    public List<Ragdoll2DZoneEntry> landingZones;
}

[System.Serializable]
public struct Ragdoll2DZoneEntry
{
    [Tooltip("Minimum Z rotation (degrees) for this landing zone.")]
    public float min;
    [Tooltip("Maximum Z rotation (degrees) for this landing zone.")]
    public float max;
}
