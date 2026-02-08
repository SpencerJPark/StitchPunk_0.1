using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateBefore(typeof(InteractableBroadcastSystem))]
public partial struct AwarenessClearSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new AwarenessClearJob().ScheduleParallel();
    }
}

[BurstCompile]
public partial struct AwarenessClearJob : IJobEntity
{
    public void Execute(ref Awareness awareness)
    {
        awareness.nearestFood = Entity.Null;
        awareness.nearestBed = Entity.Null;
        awareness.nearestWork = Entity.Null;
        awareness.nearestEntertainment = Entity.Null;
        awareness.nearestSmokeSpot = Entity.Null;
        awareness.nearestBar = Entity.Null;
        awareness.nearestBathroom = Entity.Null;
        awareness.nearestSeat = Entity.Null;

        awareness.nearestFoodDistance = float.MaxValue;
        awareness.nearestBedDistance = float.MaxValue;
        awareness.nearestWorkDistance = float.MaxValue;
        awareness.nearestEntertainmentDistance = float.MaxValue;
        awareness.nearestSmokeSpotDistance = float.MaxValue;
        awareness.nearestBarDistance = float.MaxValue;
        awareness.nearestBathroomDistance = float.MaxValue;
        awareness.nearestSeatDistance = float.MaxValue;

        awareness.hasFood = false;
        awareness.hasBed = false;
        awareness.hasWork = false;
        awareness.hasEntertainment = false;
        awareness.hasSmokeSpot = false;
        awareness.hasBar = false;
        awareness.hasBathroom = false;
        awareness.hasSeat = false;
    }
}