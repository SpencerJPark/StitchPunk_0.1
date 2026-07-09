using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Attack", menuName = "Units/Attack")]
public class AttackSO : ScriptableObject
{
    [Tooltip("Attack Name")]
    [FormerlySerializedAs("attackType")]
    [SearchableEnum] public DamageSource damageSource;

    [FormerlySerializedAs("attackEffect")] [Tooltip("Damage Behaviour")]
    [SearchableEnum] public DamageBehaviour damageBehaviour;

    [Tooltip("Damage dealt to target(s)")]
    public int damageAmount;

    [Tooltip("How close to perform attack")]
    public float range;

    [Header("Ragdoll Profile")]
    [Tooltip("Optional shared ragdoll response. When assigned, its values are flattened into the " +
             "AttackBlob at bake and the inline ragdoll/launch fields below are IGNORED. Leave null " +
             "to author this attack's response inline.")]
    public RagdollProfileSO ragdollProfile;

    [Tooltip("Scales ragdoll violence on kill. 1 = baseline (sword). 0.5 = weak/glancing. 2+ = heavy/explosive.")]
    public float ragdollForce = 1f;

    [Header("Launch Arc")]
    [Tooltip("Direct upward launch velocity (units/s). 0 = no arc. 5 = solid knock-up. 15+ = explosive.")]
    public float launchForceY = 0f;

    [Tooltip("Direct sideways launch velocity (units/s), away from attacker. 0 = no sideways drift.")]
    public float launchForceX = 0f;

    [Header("Hit Timing")]
    [Tooltip("Seconds into the attack animation when damage fires. Must be less than the clip duration.")]
    public float hitTime = 0.3f;

    [Header("Attack Rate")]
    [Tooltip("Seconds between swings. Must be >= hitTime so the hit lands before the next swing.")]
    public float cooldown = 1f;
}
