using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

// The hand-over: a pigeon that has landed on the arm opens the letter after a short beat (the player's reach animation),
// and flies off once the letter is closed. The letter can also be opened from the HUD button while the pigeon is on the arm;
// that counts as delivered too.
[BurstCompile]
[UpdateInGroup(typeof(TradeSystemGroup))]
public partial struct PigeonLetterSystem : ISystem
{
    private static readonly float3 FlyOffVelocity = new float3(-9f, 7f, 9f);

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TradePigeonSettings>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity boardEntity = SystemAPI.GetSingletonEntity<TradePigeonSettings>();
        float letterOpenDelaySeconds = SystemAPI.GetComponent<TradePigeonSettings>(boardEntity).letterOpenDelaySeconds;
        bool letterIsOpen = SystemAPI.IsComponentEnabled<TradeLetterOpen>(boardEntity);

        foreach (RefRW<PigeonFlight> flight in SystemAPI.Query<RefRW<PigeonFlight>>().WithAll<CarrierPigeon>())
        {
            if (flight.ValueRO.phase != PigeonFlightPhase.Perched)
                continue;

            if (flight.ValueRO.letterDelivered)
            {
                if (!letterIsOpen)
                {
                    flight.ValueRW.phase = PigeonFlightPhase.FlyingOff;
                    flight.ValueRW.elapsedSeconds = 0f;
                    flight.ValueRW.flyOffVelocity = FlyOffVelocity;
                }
            }
            else if (letterIsOpen || flight.ValueRO.elapsedSeconds >= letterOpenDelaySeconds)
            {
                flight.ValueRW.letterDelivered = true;
                letterIsOpen = true;
                SystemAPI.SetComponentEnabled<TradeLetterOpen>(boardEntity, true);
            }
        }
    }
}
