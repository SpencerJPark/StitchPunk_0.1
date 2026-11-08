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
        new ApplyStrategicSizeJob { scales = scales, elapsedSeconds = SystemAPI.Time.ElapsedTime }.ScheduleParallel();
    }

    [BurstCompile]
    private partial struct ApplyStrategicSizeJob : IJobEntity
    {
        public StrategicSizeScales scales;
        public double elapsedSeconds;

        private void Execute(in StrategicSizeTag sizeTag, ref PostTransformMatrix postTransformMatrix)
        {
            bool seated = elapsedSeconds < sizeTag.seatedUntilSeconds;
            float scale = seated ? scales.vehicleScale : scales.ScaleFor(sizeTag.category);
            postTransformMatrix.Value = float4x4.Scale(scale);
        }
    }
}
