using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Code-driven roll feel on the player's billboarded Visual (RL-D9): wind-up squash, a stretched spin,
// and a springy landing squash. Everything rides the Visual's PostTransformMatrix (scale about the
// feet, then spin about the body's middle), so the billboard's rotation write is never fought.
// Stand-in until the player has a real roll clip.
[BurstCompile]
[UpdateInGroup(typeof(AnimationSystemGroup))]
[UpdateAfter(typeof(VisualBillboardSystem))]
public partial struct PlayerRollVisualSystem : ISystem
{
    private const float WIND_UP_FRACTION = 0.08f;
    private const float WIND_UP_SQUASH = 0.2f;
    private const float MID_ROLL_STRETCH = 0.12f;
    private const float LANDING_SQUASH = 0.15f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<AnimationToolkitCameraData>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        float3 cameraForward = SystemAPI.GetSingleton<AnimationToolkitCameraData>().forward;
        float3 flatCameraRight = math.normalizesafe(math.cross(math.up(), new float3(cameraForward.x, 0f, cameraForward.z)),
            new float3(1f, 0f, 0f));

        EntityCommandBuffer entityCommandBuffer = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        foreach ((RefRW<PlayerRoll> playerRoll, EnabledRefRO<PlayerRoll> playerRollEnabled,
                     RefRO<PlayerRollSettings> rollSettings, Entity playerEntity) in
                 SystemAPI.Query<RefRW<PlayerRoll>, EnabledRefRO<PlayerRoll>, RefRO<PlayerRollSettings>>()
                     .WithAll<Player>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                     .WithEntityAccess())
        {
            Entity visualEntity = FindBillboardedVisual(ref state, playerEntity);
            if (visualEntity == Entity.Null) continue;

            PlayerRollSettings settings = rollSettings.ValueRO;
            bool isRolling = playerRollEnabled.ValueRO;
            bool isLanding = !isRolling && playerRoll.ValueRO.landingSquashRemaining > 0f;

            if (!SystemAPI.HasComponent<PostTransformMatrix>(visualEntity))
            {
                if (isRolling || isLanding)
                    entityCommandBuffer.AddComponent(visualEntity, new PostTransformMatrix { Value = float4x4.identity });
                continue;
            }

            // Positive = wide and short, negative = tall and thin; volume is kept by dividing the other axis.
            float squashAmount = 0f;
            float spinRadians = 0f;

            if (isRolling)
            {
                float rollFraction = math.saturate(playerRoll.ValueRO.elapsed / settings.rollDuration);
                float screenHorizontalness = math.abs(math.dot(playerRoll.ValueRO.direction, flatCameraRight));

                if (rollFraction < WIND_UP_FRACTION)
                {
                    squashAmount = WIND_UP_SQUASH * math.smoothstep(0f, 1f, rollFraction / WIND_UP_FRACTION);
                }
                else
                {
                    float spinFraction = (rollFraction - WIND_UP_FRACTION) / (1f - WIND_UP_FRACTION);
                    // Stretch along the roll as it reads on screen: wide when rolling sideways, tall toward/away.
                    float stretchTarget = MID_ROLL_STRETCH * (2f * screenHorizontalness - 1f);
                    squashAmount = math.lerp(WIND_UP_SQUASH, stretchTarget, math.smoothstep(0f, 0.25f, spinFraction))
                        * (1f - math.smoothstep(0.75f, 1f, spinFraction));

                    float easedSpin = 1f - math.pow(1f - spinFraction, 3f);
                    // Rolling screen-right spins clockwise as the viewer sees it.
                    float spinSign = math.dot(playerRoll.ValueRO.direction, flatCameraRight) >= 0f ? -1f : 1f;
                    spinRadians = spinSign * settings.spinTurns * 2f * math.PI * easedSpin;
                }
            }
            else if (isLanding)
            {
                playerRoll.ValueRW.landingSquashRemaining = math.max(0f, playerRoll.ValueRO.landingSquashRemaining - deltaTime);
                float landingProgressLeft = settings.landingSquashDuration > 0f
                    ? playerRoll.ValueRO.landingSquashRemaining / settings.landingSquashDuration
                    : 0f;
                // Starts squashed, overshoots slightly tall, settles at rest.
                squashAmount = LANDING_SQUASH * landingProgressLeft * math.cos((1f - landingProgressLeft) * 2.5f);
            }

            RefRW<PostTransformMatrix> visualMatrix = SystemAPI.GetComponentRW<PostTransformMatrix>(visualEntity);
            visualMatrix.ValueRW.Value = BuildRollMatrix(squashAmount, spinRadians, settings.visualPivotHeight);
        }
    }

    // Scale about the feet (origin), then spin about the body's middle, both in the Visual's camera-facing space.
    private static float4x4 BuildRollMatrix(float squashAmount, float spinRadians, float pivotHeight)
    {
        float horizontalScale = 1f + squashAmount;
        float verticalScale = 1f / horizontalScale;
        float4x4 squashAboutFeet = float4x4.Scale(horizontalScale, verticalScale, 1f);

        float3 bodyMiddle = new float3(0f, pivotHeight, 0f);
        float4x4 spinAboutMiddle = math.mul(float4x4.Translate(bodyMiddle),
            math.mul(float4x4.RotateZ(spinRadians), float4x4.Translate(-bodyMiddle)));

        return math.mul(spinAboutMiddle, squashAboutFeet);
    }

    private Entity FindBillboardedVisual(ref SystemState state, Entity playerEntity)
    {
        if (!SystemAPI.HasBuffer<Child>(playerEntity)) return Entity.Null;
        DynamicBuffer<Child> children = SystemAPI.GetBuffer<Child>(playerEntity);
        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            if (SystemAPI.HasComponent<VisualBillboard>(children[childIndex].Value))
                return children[childIndex].Value;
        }
        return Entity.Null;
    }
}
