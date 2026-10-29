using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Drains PendingPigeon: one pigeon at a time, so a still-unread letter is reused for the new week (Trade_System.md TR-A11).
[BurstCompile]
[UpdateInGroup(typeof(SpawnSystemGroup))]
public partial struct PigeonSpawnSystem : ISystem
{
    private static readonly float3 FlyInDirection = math.normalize(new float3(-1f, 0f, 1f));
    private static readonly float3 NoCaravanLandingOffset = new float3(2f, 1.2f, 0f);

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TradePigeonSettings>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity boardEntity = SystemAPI.GetSingletonEntity<TradePigeonSettings>();
        DynamicBuffer<PendingPigeon> pendingPigeons = SystemAPI.GetBuffer<PendingPigeon>(boardEntity);
        if (pendingPigeons.Length == 0)
            return;

        int weekNumber = pendingPigeons[pendingPigeons.Length - 1].weekNumber;
        pendingPigeons.Clear();

        foreach (RefRW<CarrierPigeon> existingPigeon in SystemAPI.Query<RefRW<CarrierPigeon>>())
        {
            existingPigeon.ValueRW.weekNumber = weekNumber;
            return;
        }

        TradePigeonSettings settings = SystemAPI.GetComponent<TradePigeonSettings>(boardEntity);
        float3 landingPosition;
        if (SystemAPI.TryGetSingletonEntity<Caravan>(out Entity caravanEntity))
        {
            landingPosition = SystemAPI.GetComponent<LocalTransform>(caravanEntity).TransformPoint(settings.perchLocalOffset);
        }
        else if (SystemAPI.TryGetSingletonEntity<Player>(out Entity playerEntity))
        {
            landingPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position + NoCaravanLandingOffset;
        }
        else
        {
            return;
        }

        float3 startPosition = landingPosition + FlyInDirection * settings.flyInStartDistance + new float3(0f, settings.flyInStartHeight, 0f);

        Entity pigeonEntity = state.EntityManager.Instantiate(settings.prefab);
        LocalTransform pigeonTransform = SystemAPI.GetComponent<LocalTransform>(pigeonEntity);
        pigeonTransform.Position = startPosition;
        SystemAPI.SetComponent(pigeonEntity, pigeonTransform);
        SystemAPI.SetComponent(pigeonEntity, new CarrierPigeon { weekNumber = weekNumber });
        SystemAPI.SetComponent(pigeonEntity, new PigeonFlight
        {
            phase = PigeonFlightPhase.FlyingIn,
            startPosition = startPosition,
            fallbackPerchPosition = landingPosition,
        });
    }
}
