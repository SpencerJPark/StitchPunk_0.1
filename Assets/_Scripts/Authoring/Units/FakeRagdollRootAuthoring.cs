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

    [Tooltip("All joint pivot GameObjects (upper/lower arm bends, upper/lower leg bends, etc.)")]
    public List<GameObject> joints = new();

    [Tooltip("How long the ragdoll plays before disabling (seconds).")]
    public float duration = 3f;

    [Tooltip("Initial Z tilt speed of the body falling over (deg/s).")]
    public float bodyFallSpeed = 180f;

    [Tooltip("How far above root.Y to clamp joints. Increase if quad corners clip through ground.")]
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
            foreach (var joint in authoring.joints)
            {
                if (joint == null) continue;
                jointBuffer.Add(new FakeRagdollJointRef
                {
                    joint = GetEntity(joint, TransformUsageFlags.Dynamic)
                });
            }
        }
    }
}
