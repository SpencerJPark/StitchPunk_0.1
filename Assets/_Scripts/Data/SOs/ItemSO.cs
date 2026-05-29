using UnityEngine;

[CreateAssetMenu(fileName = "Item", menuName = "Items/Item")]
public class ItemSO : ScriptableObject
{
    [SearchableEnum] public ItemType     itemType;
    [SearchableEnum] public ItemCategory category;

    [Header("Weapon")]
    [Tooltip("Attack this weapon enables when wielded. Damage / range / timing live on the AttackSO.")]
    [SearchableEnum] public AttackType weaponAttack;

    [Tooltip("Optional on-hit effect (poison, fire, etc). Leave None for vanilla damage.")]
    [SearchableEnum] public EffectType onHitEffect;

    [Header("Consumable")]
    [Tooltip("Effect applied when the item is consumed. EffectSO carries value / behaviours.")]
    [SearchableEnum] public EffectType consumeEffect;

    [Header("Pickup")]
    [Tooltip("How close the unit must be to pick up / consume the item.")]
    public float pickupRange = 1.5f;

    [Tooltip("Seconds spent picking up / consuming.")]
    public float consumeDuration = 1f;

    [Tooltip("Base desirability of seeking this item; scales the awareness utility.")]
    public float baseUtility = 1f;

    [Header("Throw")]
    [Tooltip("How fast the item travels when thrown (units/sec).")]
    public float throwSpeed = 10f;

    [Tooltip("Initial upward velocity when thrown (controls arc height).")]
    public float throwArc = 4f;

    [Tooltip("Damage dealt to a health entity when the thrown item hits it.")]
    public int throwDamage = 10;

    [Tooltip("Scales ragdoll violence when this item kills on impact. 1 = baseline (sword). 2+ = heavy/explosive.")]
    public float throwRagdollForce = 1f;

    [Tooltip("Direct upward launch velocity (units/s) when this item kills on impact. 0 = no arc.")]
    public float throwLaunchForceY = 0f;

    [Tooltip("Direct sideways launch velocity (units/s) when this item kills on impact. 0 = no drift.")]
    public float throwLaunchForceX = 0f;
}
