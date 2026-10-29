using Unity.Entities;
using UnityEngine;

// One per playable subscene; bakes the TradeBoard singleton plus its client library reference.
public class TradeBoardAuthoring : MonoBehaviour
{
    public ClientLibrarySO library;
    [Tooltip("0 = pick a random seed on the first weekly roll. Any other value makes every run roll the same weeks.")]
    public uint worldSeed;

    [Header("Carrier pigeon")]
    public GameObject pigeonPrefab;
    [Tooltip("Used only when the player has no PigeonArmTarget child. In the player's local space.")]
    public Vector3 fallbackArmLocalOffset = new Vector3(0.4f, 1.3f, 0.3f);
    [Tooltip("Seconds between the pigeon landing and the letter opening: the window for the player's reach animation.")]
    public float letterOpenDelaySeconds = 1.2f;
    public float flyInStartDistance = 45f;
    public float flyInStartHeight = 18f;
    public float flyInSeconds = 5f;
    public float flyOffSeconds = 3f;
    public float arcHeight = 4f;

    public class Baker : Baker<TradeBoardAuthoring>
    {
        public override void Bake(TradeBoardAuthoring authoring)
        {
            if (authoring.library == null) return;

            DependsOn(authoring.library);
            DependsOn(authoring.library.tuning);
            DependsOn(authoring.library.household);
            if (authoring.library.household != null && authoring.library.household.templates != null)
            {
                foreach (OrderTemplateSO template in authoring.library.household.templates)
                {
                    DependsOn(template);
                }
            }
            if (authoring.library.clients != null)
            {
                foreach (ClientSO client in authoring.library.clients)
                {
                    DependsOn(client);
                    if (client == null || client.orderPool == null) continue;
                    foreach (OrderTemplateSO template in client.orderPool)
                    {
                        DependsOn(template);
                    }
                }
            }

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new TradeBoard { worldSeed = authoring.worldSeed });
            AddBuffer<TradeOrder>(entity);
            AddBuffer<ClientRelationship>(entity);
            AddBuffer<PendingPigeon>(entity);
            AddBuffer<TradeOrderResponseRequest>(entity);
            AddComponent(entity, new TradeToast());
            AddComponent<TradeLetterOpen>(entity);
            SetComponentEnabled<TradeLetterOpen>(entity, false);
            if (authoring.pigeonPrefab != null)
            {
                AddComponent(entity, new TradePigeonSettings
                {
                    prefab = GetEntity(authoring.pigeonPrefab, TransformUsageFlags.Dynamic),
                    fallbackArmLocalOffset = authoring.fallbackArmLocalOffset,
                    letterOpenDelaySeconds = authoring.letterOpenDelaySeconds,
                    flyInStartDistance = authoring.flyInStartDistance,
                    flyInStartHeight = authoring.flyInStartHeight,
                    flyInSeconds = authoring.flyInSeconds,
                    flyOffSeconds = authoring.flyOffSeconds,
                    arcHeight = authoring.arcHeight,
                });
            }
            AddComponent<TradeRerollRequest>(entity);
            SetComponentEnabled<TradeRerollRequest>(entity, false);
            AddComponent(entity, new ClientLibraryReference { library = authoring.library });
            AddComponent(entity, new ClientLibrary());
        }
    }
}
