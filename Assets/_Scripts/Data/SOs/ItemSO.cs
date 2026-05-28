using UnityEngine;

[CreateAssetMenu(fileName = "Item", menuName = "AI/Item")]
public class ItemSO : ScriptableObject
{
    [SearchableEnum] public ItemType     itemType;
    [SearchableEnum] public ItemCategory category;

    [Header("Healing (Healing category)")]
    [Tooltip("HP restored when this item is used.")]
    public int healAmount;

    [Header("Consumable (Food / Drink category)")]
    [SearchableEnum] public MotivationType satisfiedMotivation;
    [Tooltip("Flat motivation restored on consume (0–100).")]
    public float restorationAmount;

    [Header("Pickup")]
    [Tooltip("How close the unit must be to pick up / consume the item.")]
    public float pickupRange = 1.5f;

    [Tooltip("Seconds spent picking up / consuming.")]
    public float consumeDuration = 1f;

    [Tooltip("Base desirability of seeking this item; scales the awareness utility.")]
    public float baseUtility = 1f;
}
