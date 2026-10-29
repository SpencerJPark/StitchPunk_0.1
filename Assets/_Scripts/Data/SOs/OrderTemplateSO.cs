using System;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public struct ResourceBonus
{
    public ResourceType resourceType;
    public int amount;
}

[CreateAssetMenu(menuName = "Stitch Punk/Trade/Order Template")]
public class OrderTemplateSO : ScriptableObject
{
    [TextArea] public string flavourText;
    public UnitType unitType;
    public int2 quantityRange = new int2(1, 1);
    [Range(0f, 1f)] public float minHealthFraction;
    public int2 dueDayRange = new int2(3, 5);
    public ReputationTier minimumTier;
    public float weight = 1f;
    public ResourceBonus[] bonusResources;
}
