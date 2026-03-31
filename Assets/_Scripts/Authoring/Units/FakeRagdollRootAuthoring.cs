using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Place ONLY on the root body entity (the empty at ground level).
/// Drag in the visual child and all joint pivot GameObjects here.
///
/// The baker ONLY writes to the root entity (DOTS baking rule: each baker owns its GO's entity).
/// FakeRagdollBakingSystem then adds FakeRagdoll / FakeRagdollJoint to the child entities.
/// </summary>
public class FakeRagdollRootAuthoring : MonoBehaviour
{
    [Tooltip("The direct visual child of root that holds the whole character (this is what tilts on Z).")]
    public GameObject visualChild;

    [Tooltip("Each joint pivot GO plus an optional per-joint ground buffer override. " +
             "Set groundBuffer > 0 to override the global buffer for that joint. " +
             "Rule of thumb: set it to the length of the longest limb segment below that pivot " +
             "so the limb tip can never reach the ground plane.")]
    public List<FakeRagdollJointEntry> joints = new();

    [Tooltip("Angular speed at which the body tips over (deg/s). 180 = reaches 90° in ~0.5s.")]
    public float bodyFallSpeed = 180f;

    [Tooltip("Global ground buffer: how far above root.Y to clamp all joints unless overridden per-joint. " +
             "Also lifts the visual body child off the ground by this amount.")]
    public float groundBuffer = 0.15f;


    public class Baker : Baker<FakeRagdollRootAuthoring>
    {
        public override void Bake(FakeRagdollRootAuthoring authoring)
        {
            if (authoring.visualChild == null)
            {
                Debug.LogError($"FakeRagdollRootAuthoring on {authoring.gameObject.name}: visualChild is not assigned.");
                return;
            }

            Entity rootEntity       = GetEntity(TransformUsageFlags.Dynamic);
            Entity visualRootEntity = GetEntity(authoring.visualChild, TransformUsageFlags.Dynamic);

            // Only write to rootEntity — FakeRagdollBakingSystem handles the child entities
            AddComponent(rootEntity, new FakeRagdollConfig
            {
                visualRoot   = visualRootEntity,
                groundBuffer = authoring.groundBuffer,
                fallSpeed    = authoring.bodyFallSpeed
            });

            var jointBuffer = AddBuffer<FakeRagdollJointRef>(rootEntity);
            foreach (var entry in authoring.joints)
            {
                if (entry.joint == null) continue;
                float buf = entry.groundBuffer > 0f ? entry.groundBuffer : authoring.groundBuffer;
                jointBuffer.Add(new FakeRagdollJointRef
                {
                    joint        = GetEntity(entry.joint, TransformUsageFlags.Dynamic),
                    groundBuffer = buf
                });
            }
        }
    }
}

[System.Serializable]
public struct FakeRagdollJointEntry
{
    public GameObject joint;
    [Tooltip("Per-joint ground buffer. 0 = use the global buffer on this authoring component. " +
             "For shoulder/hip pivots, set this to roughly the limb length so the hand/foot " +
             "can never reach the ground even at full flail.")]
    public float groundBuffer;
}
