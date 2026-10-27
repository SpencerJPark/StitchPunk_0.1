using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Moves a knocked-down unit's root (FakeRagdollComponents.cs): a ballistic arc, landing, lying, standing up. Runs
// after gravity so its position wins, and pins Movement and Gravity so neither drags the unit while it's down.
// CutsceneActor keeps a living unit's AI and facing out of the way until it's back on its feet; a dead one is
// left alone and simply lies there until a revive clears Dead. FakeRagdollPoseSystem draws the topple.
[BurstCompile]
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
[UpdateAfter(typeof(UnitGravitySystem))]
public partial struct FakeRagdollMotionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FakeRagdoll>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Nobody down (the common case): skip scheduling a job over every unit.
        if (SystemAPI.QueryBuilder().WithAll<FakeRagdoll>().Build().IsEmpty) return;

        state.Dependency = new FakeRagdollMotionJob
        {
            deltaTime = SystemAPI.Time.DeltaTime,
            deadLookup = SystemAPI.GetComponentLookup<Dead>(true),
            movementLookup = SystemAPI.GetComponentLookup<Movement>(),
            gravityLookup = SystemAPI.GetComponentLookup<Gravity>(),
            cutsceneActorLookup = SystemAPI.GetComponentLookup<CutsceneActor>(),
            ragdollActorLookup = SystemAPI.GetComponentLookup<RagdollActor>(),
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct FakeRagdollMotionJob : IJobEntity
{
    private const float FALL_GRAVITY = 14f;
    // Airborne topple rate; a short fling still lands flat because Lying finishes the turn.
    private const float AIRBORNE_TOPPLE_DEGREES_PER_SECOND = 420f;
    private const float LYING_SETTLE_DEGREES_PER_SECOND = 360f;
    private const float STAND_UP_SECONDS = 0.45f;
    // A flight that never lands (thrown off a ledge, no ground under it) still ends.
    private const float MAXIMUM_FLIGHT_SECONDS = 3f;
    // Toolkit ragdolls have no arc to land: give the solver this long before the lie timer starts.
    private const float TOOLKIT_FLIGHT_SECONDS = 1.2f;

    public float deltaTime;
    [ReadOnly] public ComponentLookup<Dead> deadLookup;
    // Each unit only ever writes its own components through these.
    [NativeDisableParallelForRestriction] public ComponentLookup<Movement> movementLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<Gravity> gravityLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<CutsceneActor> cutsceneActorLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<RagdollActor> ragdollActorLookup;

    private void Execute(Entity unitEntity, ref LocalTransform unitTransform, ref FakeRagdoll fakeRagdoll)
    {
        bool isDead = deadLookup.HasComponent(unitEntity) && deadLookup.IsComponentEnabled(unitEntity);
        fakeRagdoll.phaseSeconds += deltaTime;

        switch (fakeRagdoll.phase)
        {
            case FakeRagdollPhase.Airborne:
                if (fakeRagdoll.drivesToolkitRagdoll)
                {
                    if (fakeRagdoll.phaseSeconds >= TOOLKIT_FLIGHT_SECONDS) EnterPhase(ref fakeRagdoll, FakeRagdollPhase.Lying);
                    break;
                }
                fakeRagdoll.velocity.y -= FALL_GRAVITY * deltaTime;
                fakeRagdoll.position += fakeRagdoll.velocity * deltaTime;
                fakeRagdoll.toppleDegrees = math.min(90f, fakeRagdoll.toppleDegrees + AIRBORNE_TOPPLE_DEGREES_PER_SECOND * deltaTime);
                bool landed = fakeRagdoll.position.y <= fakeRagdoll.groundHeight && fakeRagdoll.velocity.y <= 0f;
                if (landed || fakeRagdoll.phaseSeconds >= MAXIMUM_FLIGHT_SECONDS)
                {
                    fakeRagdoll.position.y = fakeRagdoll.groundHeight;
                    fakeRagdoll.velocity = float3.zero;
                    EnterPhase(ref fakeRagdoll, FakeRagdollPhase.Lying);
                }
                break;

            case FakeRagdollPhase.Lying:
                fakeRagdoll.toppleDegrees = math.min(90f, fakeRagdoll.toppleDegrees + LYING_SETTLE_DEGREES_PER_SECOND * deltaTime);
                if (!isDead && fakeRagdoll.phaseSeconds >= fakeRagdoll.lieSeconds)
                {
                    EnterPhase(ref fakeRagdoll, FakeRagdollPhase.StandingUp);
                    if (fakeRagdoll.drivesToolkitRagdoll && ragdollActorLookup.HasComponent(unitEntity))
                        ragdollActorLookup.SetComponentEnabled(unitEntity, false);
                }
                break;

            case FakeRagdollPhase.StandingUp:
                float standProgress = math.saturate(fakeRagdoll.phaseSeconds / STAND_UP_SECONDS);
                fakeRagdoll.toppleDegrees = 90f * (1f - standProgress * standProgress * (3f - 2f * standProgress));
                if (standProgress >= 1f)
                {
                    fakeRagdoll.toppleDegrees = 0f;
                    // FakeRagdollPoseSystem puts the rest pose back and switches the component off.
                    EnterPhase(ref fakeRagdoll, FakeRagdollPhase.Restoring);
                }
                break;
        }

        if (!isDead && cutsceneActorLookup.HasComponent(unitEntity))
            cutsceneActorLookup.SetComponentEnabled(unitEntity, fakeRagdoll.phase != FakeRagdollPhase.Restoring);

        // Restoring also covers a pooled body handed out somewhere new: it must not be pulled back to its old corpse.
        if (fakeRagdoll.phase == FakeRagdollPhase.Restoring) return;

        if (!fakeRagdoll.drivesToolkitRagdoll)
            unitTransform.Position = fakeRagdoll.position;

        if (movementLookup.HasComponent(unitEntity))
            movementLookup.GetRefRW(unitEntity).ValueRW.targetPosition = unitTransform.Position;
        if (gravityLookup.HasComponent(unitEntity))
            gravityLookup.GetRefRW(unitEntity).ValueRW.verticalVelocity = 0f;
    }

    private static void EnterPhase(ref FakeRagdoll fakeRagdoll, FakeRagdollPhase nextPhase)
    {
        fakeRagdoll.phase = nextPhase;
        fakeRagdoll.phaseSeconds = 0f;
    }
}
