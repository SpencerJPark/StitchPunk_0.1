using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct StrategicSizeScaleSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StrategicSizeScales>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        StrategicSizeScales scales = SystemAPI.GetSingleton<StrategicSizeScales>();
        new ApplyStrategicSizeJob { scales = scales }.ScheduleParallel();
    }

    [BurstCompile]
    private partial struct ApplyStrategicSizeJob : IJobEntity
    {
        public StrategicSizeScales scales;

        private void Execute(in StrategicSizeTag sizeTag, ref PostTransformMatrix postTransformMatrix)
        {
            postTransformMatrix.Value = float4x4.Scale(scales.ScaleFor(sizeTag.category));
        }
    }
}
