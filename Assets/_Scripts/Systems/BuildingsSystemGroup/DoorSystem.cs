using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Settles what every door does this frame from its zones and any script override, then swings it (CV-D20).
// Hybrid art (CaravanPresenter) mirrors Door.angleDegrees onto its own hinge.
[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
[UpdateAfter(typeof(CaravanExitSequenceSystem))]
public partial struct DoorSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Door>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (RefRW<Door> door in SystemAPI.Query<RefRW<Door>>())
        {
            DoorControl.ApplyRequests(ref door.ValueRW);
            DoorControl.Advance(ref door.ValueRW, deltaTime);
        }

        foreach ((RefRW<LocalTransform> doorTransform, RefRO<Door> door, RefRO<DoorHinge> doorHinge)
                 in SystemAPI.Query<RefRW<LocalTransform>, RefRO<Door>, RefRO<DoorHinge>>())
        {
            quaternion swing = quaternion.AxisAngle(math.up(), math.radians(door.ValueRO.angleDegrees));
            doorTransform.ValueRW.Rotation = math.mul(swing, doorHinge.ValueRO.restRotation);
        }
    }
}
