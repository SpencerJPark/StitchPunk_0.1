using System;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public struct ReputationTierThresholds
{
    public int known;
    public int trusted;
    public int partner;

    public int3 ToInt3() => new int3(known, trusted, partner);
}

[CreateAssetMenu(menuName = "Stitch Punk/Trade/Client")]
public class ClientSO : ScriptableObject
{
    [Tooltip("Stable and serialized into saves. Authored clients are positive; household one-offs are negative.")]
    public int clientId;
    public string displayName;
    public ClientKind kind = ClientKind.Recurring;
    public int homeDropOffId;
    public Sprite letterSeal;
    [Range(0f, 1f)] public float baseOrderChancePerWeek = 0.6f;
    public OrderTemplateSO[] orderPool;
    public ReputationTierThresholds tiers = new ReputationTierThresholds { known = 25, trusted = 50, partner = 80 };
}
