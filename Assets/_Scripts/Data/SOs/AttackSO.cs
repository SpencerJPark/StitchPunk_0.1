using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Attack", menuName = "AttackSO/Attack")]
public class AttackSO : ScriptableObject
{
    [Tooltip("Attack Name")]
    public AttackType attackType;

    [Tooltip("Attack Delivery Behaviour")]
    public AttackDelivery attackDelivery;

    [FormerlySerializedAs("attackEffect")] [Tooltip("Damage Behaviour")]
    public DamageBehaviour damageBehaviour;

    [Tooltip("Damage dealt to target(s)")]
    public int damageAmount;

    [Tooltip("How close to perform attack")]
    public float range;

    [Tooltip("Seconds between attacks")]
    public float cooldown;

    [Tooltip("Scales ragdoll violence on kill. 1 = baseline (sword). 0.5 = weak/glancing. 2+ = heavy/explosive.")]
    public float ragdollForce = 1f;
}
