using System;
using System.Collections.Generic;
using UnityEngine;

// Physics config for one ragdoll joint KIND (arm elbow, knee, neck …) — one asset shared by every
// placement of that joint across rigs. Referenced by RagdollJointAuthoring on the dedicated joint
// empties and baked per-joint into RagdollJointBakeData + a RagdollLandingZone buffer by its baker;
// CharacterRigBakingSystem stamps Ragdoll2DJoint from that. Fully separate from the design pipeline:
// UnitPartSO / the PartLibrary blob carry no ragdoll data.
[CreateAssetMenu(fileName = "Ragdoll Joint", menuName = "Units/Ragdoll Joint")]
public class RagdollJointSO : ScriptableObject
{
    [Tooltip("Angular settle speed (deg/s) toward the landing angle once grounded. " +
             "A per-placement override on RagdollJointAuthoring wins when set.")]
    public float settleSpeed = 8f;

    [Tooltip("Flail pendulum length (world units) — pivot-to-tip reach of the limb it bends. " +
             "Shorter = twitchier flail.")]
    public float segmentLength = 0.5f;

    [Tooltip("Flail weight — scales inherited motion and the landing kick.")]
    public float weight = 1f;

    [Tooltip("Landing zones (degrees, local Z). One is picked at random on death and a random angle " +
             "within it becomes the joint's settle target.")]
    public List<LandingZone> zones = new();
}

[Serializable]
public class LandingZone
{
    [Tooltip("Minimum Z rotation (degrees) for this landing zone.")]
    public float min;

    [Tooltip("Maximum Z rotation (degrees) for this landing zone.")]
    public float max;
}
