using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(CrowdLifeSystemGroup))]
public partial struct IntoxicationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        IntoxicationDecayJob job = new IntoxicationDecayJob
        {
            deltaTime = SystemAPI.Time.DeltaTime,
            elapsedTime = (float)SystemAPI.Time.ElapsedTime,
            movementLookup = SystemAPI.GetComponentLookup<Movement>(false),
            stateMachineLookup = SystemAPI.GetComponentLookup<StateMachine>(true)
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct IntoxicationDecayJob : IJobEntity
{
    const float DecayPerSecond = 0.015f;
    const float TipsyThreshold = 0.3f;
    const float BrawlCooldownSeconds = 60f;

    public float deltaTime;
    public float elapsedTime;
    [ReadOnly] public ComponentLookup<StateMachine> stateMachineLookup;

    // Looked up (not a ref param) so a disabled Movement cannot drop the unit from this job.
    [NativeDisableParallelForRestriction] public ComponentLookup<Movement> movementLookup;

    public void Execute(
        Entity entity,
        ref Intoxication intoxication,
        EnabledRefRW<Intoxication> intoxicationEnabled,
        ref EmoteState emoteState)
    {
        intoxication.level = math.max(0f, intoxication.level - DecayPerSecond * deltaTime);

        // The cooldown holds at full while brawling and counts down only after, so DrunkAwareness can't chain brawls (LH-D13).
        bool isBrawling = stateMachineLookup.TryGetComponent(entity, out StateMachine stateMachine) && stateMachine.action == ActionType.Brawl;
        intoxication.brawlCooldown = isBrawling ? BrawlCooldownSeconds : math.max(0f, intoxication.brawlCooldown - deltaTime);

        if (intoxication.level <= 0f)
        {
            if (movementLookup.HasComponent(entity))
            {
                Movement movement = movementLookup[entity];
                movement.targetPosition -= intoxication.appliedWobble;
                if (intoxication.baseMoveSpeed > 0f)
                    movement.moveSpeed = intoxication.baseMoveSpeed;
                movementLookup[entity] = movement;
            }
            intoxication = default;
            intoxicationEnabled.ValueRW = false;
            return;
        }

        if (intoxication.level > TipsyThreshold)
        {
            intoxication.nextDrunkEmoteIn -= deltaTime;
            if (intoxication.nextDrunkEmoteIn <= 0f)
            {
                EmoteRules.TryShow(ref emoteState, EmoteIcon.Drunk, 2.5f, EmoteRules.AmbientPriority);
                uint hash = math.hash(new uint2((uint)entity.Index, (uint)(elapsedTime * 4f)));
                float jitter = (hash % 2000u) / 1000f;
                intoxication.nextDrunkEmoteIn = 6f + jitter;
            }
        }
    }
}
