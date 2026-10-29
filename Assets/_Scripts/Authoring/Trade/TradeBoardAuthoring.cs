using Unity.Entities;
using UnityEngine;

// One per playable subscene; bakes the TradeBoard singleton plus its client library reference.
public class TradeBoardAuthoring : MonoBehaviour
{
    public ClientLibrarySO library;
    [Tooltip("0 = pick a random seed on the first weekly roll. Any other value makes every run roll the same weeks.")]
    public uint worldSeed;

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
            AddComponent<TradeRerollRequest>(entity);
            SetComponentEnabled<TradeRerollRequest>(entity, false);
            AddComponent(entity, new ClientLibraryReference { library = authoring.library });
            AddComponent(entity, new ClientLibrary());
        }
    }
}
