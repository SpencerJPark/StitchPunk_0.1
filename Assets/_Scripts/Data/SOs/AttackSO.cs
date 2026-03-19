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
    public int range;

    [Tooltip("Seconds between attacks")]
    public float cooldown;
}
