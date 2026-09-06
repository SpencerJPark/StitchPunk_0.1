using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

// The game's legs for the toolkit's marks lane (A64): the cutscene says where a slot must stand and
// judges arrival itself, this walks the unit there through the ordinary pathfinding pipeline.
// The Player is excluded on purpose (G2 §4) — they walk to their own mark on their own input, and
// the rendezvous hold waits for them exactly as it waits for anyone else.
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

// The mirror: the toolkit disabled the order (arrived, timed out, skipped, or the cutscene ended),
// so the walk we started has to end with it. The switch-off is read as [WithPresent] plus an explicit
// ValueRO check rather than [WithDisabled] — measured 2026-09-06, a [WithDisabled] on the order
// matched nothing here while a hand-built query with the identical constraints matched (Gotchas.md).
// Movement is [WithPresent] rather than enabled-only so a unit that died mid-walk still clears the
// flag instead of carrying it into its next life.
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

        // Stop where the unit stands, not at the mark: an arrival is already inside tolerance and a
        // timeout has placed the unit on the mark, so its own position is the right answer either
        // way — and it is the same "stop now" idiom CutsceneStartSystem uses when gating an actor.
        movement.targetPosition = localTransform.Position;

        cutsceneMarkIssuedEnabled.ValueRW = false;
    }
}
