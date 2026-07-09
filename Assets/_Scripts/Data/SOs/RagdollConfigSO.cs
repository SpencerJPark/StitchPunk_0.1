using UnityEngine;

// Global ragdoll simulation tuning — flattened into the RagdollSimConfig singleton by
// RagdollSimConfigAuthoring (flat floats, so no blob pipeline). Systems fall back to these same
// defaults when the authoring isn't in the scene, so the asset only needs to exist once tuning
// starts deviating from the baseline.
[CreateAssetMenu(fileName = "RagdollConfig", menuName = "Units/Ragdoll Config")]
public class RagdollConfigSO : ScriptableObject
{
    [Header("Flight")]
    [Tooltip("Downward acceleration (units/s²) on airborne corpses.")]
    public float gravity = 20f;

    [Tooltip("Exponential horizontal (XZ) deceleration while airborne.")]
    public float horizontalDrag = 2.5f;

    [Tooltip("How far below the root to raycast for ground each airborne frame.")]
    public float groundRaycastDistance = 5f;

    [Header("Bounce")]
    [Tooltip("Bounce energy kept on ground/wall impacts when the attack authored none (0..1).")]
    [Range(0f, 1f)] public float defaultRestitution = 0.3f;

    [Tooltip("Impact speeds below this (units/s) rest instead of bouncing.")]
    public float bounceMinSpeed = 2f;

    [Header("Flail")]
    [Tooltip("Joint angular kick per unit of landing impact speed. 1 = physical baseline.")]
    public float landingImpulseScale = 1f;

    [Tooltip("Exponential decay on joint angular velocity (and grounded spin). Higher = stiffer.")]
    public float flailDamping = 1.5f;

    [Tooltip("The corpse sleeps (zero sim cost) once every angular speed drops below this (deg/s).")]
    public float sleepAngularSpeedDeg = 1f;

    [Header("Corpse Stacking")]
    [Tooltip("XZ cell size (world units) of the corpse-stacking hash.")]
    public float corpseCellSize = 1f;

    [Tooltip("Landing-height raise per settled corpse already in the landing cell.")]
    public float corpseStackOffset = 0.15f;

    [Tooltip("Cap on counted corpses per cell (max pile height = cap × offset).")]
    public int corpseStackMax = 5;
}
