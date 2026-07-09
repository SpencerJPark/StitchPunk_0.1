using UnityEngine;

// Shared ragdoll response for a family of attacks (e.g. "Explosive", "Heavy Swing"). Assigned on
// AttackSO.ragdollProfile and FLATTENED into the AttackBlob by AttackLibraryBakingSystem — zero
// runtime indirection; retuning the profile retunes every attack that references it after a rebake.
// Attacks with no profile fall back to their inline AttackSO fields.
[CreateAssetMenu(fileName = "RagdollProfile", menuName = "Units/Ragdoll Profile")]
public class RagdollProfileSO : ScriptableObject
{
    [Tooltip("Scales ragdoll violence on kill (body tip-over speed). 1 = baseline (sword). " +
             "0.5 = weak/glancing. 2+ = heavy/explosive.")]
    public float ragdollForce = 1f;

    [Header("Launch Arc")]
    [Tooltip("Direct upward launch velocity (units/s). 0 = no arc. 5 = solid knock-up. 15+ = explosive.")]
    public float launchForceY = 0f;

    [Tooltip("Direct horizontal launch velocity (units/s), away from the hit source. 0 = no drift.")]
    public float launchForceX = 0f;

    [Header("Flight Feel")]
    [Tooltip("Scales how violently the limbs flail in flight and kick on landing. 1 = baseline.")]
    public float flailIntensity = 1f;

    [Tooltip("Airborne body tumble (deg/s) on top of the normal tip-over; damps out on the ground. " +
             "0 = none. 360+ = full flips on explosive kills.")]
    public float spin = 0f;

    [Tooltip("Bounce energy kept on ground/wall impacts (0..1). 0 = use the RagdollSimConfig default.")]
    [Range(0f, 1f)] public float restitution = 0f;
}
