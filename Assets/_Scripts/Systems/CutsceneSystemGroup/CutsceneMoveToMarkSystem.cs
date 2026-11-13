using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

// Walks units to their marks via the ordinary pathfinding; the toolkit judges arrival.
// The Player is excluded on purpose: they walk to their own mark on their own input.
[BurstCompile]
[UpdateInGroup(typeof(CutsceneSystemGroup))]
[UpdateAfter(typeof(CutsceneStartSystem))]
public partial struct CutsceneMoveToMarkSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new IssueMarkPathRequestJob().ScheduleParallel(state.Dependency);
        state.Dependency = new ClearResolvedMarkJob().ScheduleParallel(state.Dependency);
    }
}

// CutsceneMoveToMark is enrolled enabled-only by the `in` parameter, CutsceneMarkIssued disabled-only
// by [WithDisabled] — together that is "ordered, not yet asked", so the order is issued once.
// PathRequest needs [WithPresent] or the EnabledRefRW would silently narrow the query to units that
// already have a path in flight (Gotchas.md).
[BurstCompile]
[WithNone(typeof(Player))]
[WithDisabled(typeof(CutsceneMarkIssued))]
[WithPresent(typeof(PathRequest))]
public partial struct IssueMarkPathRequestJob : IJobEntity
{
    private void Execute(
        in CutsceneMoveToMark cutsceneMoveToMark,
        ref PathRequest pathRequest,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        EnabledRefRW<CutsceneMarkIssued> cutsceneMarkIssuedEnabled)
    {
        // Half the mark's own tolerance: the toolkit calls the mark reached at toleranceMeters, so a
        // path that stops at exactly that radius would leave a rendezvous hold waiting on the margin.
        MovementAPI.BeginPathRequest(
            ref pathRequest,
            pathRequestEnabled,
            cutsceneMoveToMark.position,
            cutsceneMoveToMark.toleranceMeters * 0.5f);

        cutsceneMarkIssuedEnabled.ValueRW = true;
    }
}

// Ends the walk when the toolkit disables the order. Uses [WithPresent] plus a ValueRO check, not
// [WithDisabled], which matched nothing here (Gotchas.md). Movement is [WithPresent] so a unit that died mid-walk still clears the flag.
[BurstCompile]
[WithPresent(typeof(CutsceneMoveToMark))]
[WithPresent(typeof(PathRequest))]
[WithPresent(typeof(Movement))]
public partial struct ClearResolvedMarkJob : IJobEntity
{
    private void Execute(
        in LocalTransform localTransform,
        EnabledRefRO<CutsceneMoveToMark> cutsceneMoveToMarkEnabled,
        ref PathRequest pathRequest,
        ref Movement movement,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        EnabledRefRW<CutsceneMarkIssued> cutsceneMarkIssuedEnabled)
    {
        if (cutsceneMoveToMarkEnabled.ValueRO)
            return;

        MovementAPI.HaltPathing(ref pathRequest, pathRequestEnabled);

        // Stop where the unit stands: an arrival is inside tolerance and a timeout has placed it on the mark.
        movement.targetPosition = localTransform.Position;

        cutsceneMarkIssuedEnabled.ValueRW = false;
    }
}
