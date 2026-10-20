using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

public struct EmoteCandidate
{
    public Entity unit;
    public float3 headPosition;
    public EmoteIcon icon;
    public byte priority;
    public float remaining;
    public float distanceToCamera;
}

public struct EmoteCandidateComparer : System.Collections.Generic.IComparer<EmoteCandidate>
{
    public int Compare(EmoteCandidate left, EmoteCandidate right)
    {
        if (left.priority != right.priority)
        {
            return right.priority.CompareTo(left.priority);
        }
        return left.distanceToCamera.CompareTo(right.distanceToCamera);
    }
}

[BurstCompile]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct EmotePresentationSystem : ISystem
{
    private ComponentLookup<CrowdLifeLod> crowdLifeLodLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EmotePoolConfig>();
        state.RequireForUpdate<AnimationToolkitCameraData>();
        crowdLifeLodLookup = state.GetComponentLookup<CrowdLifeLod>();
        if (!SystemAPI.HasSingleton<CrowdLifeLod>())
        {
            state.EntityManager.CreateSingleton(new CrowdLifeLod { beatIntervalScale = 1f });
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EmotePoolConfig config = SystemAPI.GetSingleton<EmotePoolConfig>();
        AnimationToolkitCameraData camera = SystemAPI.GetSingleton<AnimationToolkitCameraData>();
        Entity crowdLifeLodEntity = SystemAPI.GetSingletonEntity<CrowdLifeLod>();
        crowdLifeLodLookup.Update(ref state);

        NativeList<EmoteCandidate> candidates = new NativeList<EmoteCandidate>(64, Allocator.TempJob);
        NativeReference<int> visibleHumanCount = new NativeReference<int>(0, Allocator.TempJob);
        NativeReference<int> emoteBudget = new NativeReference<int>(0, Allocator.TempJob);

        // Cull around the ground point the camera looks at (the screen centre), not the camera itself:
        // the follow camera sits ~35 m from the player, so a camera-distance cull rejected everyone on screen.
        float3 cameraForward = math.normalizesafe(camera.forward);
        float3 viewFocus = cameraForward.y < -0.05f
            ? camera.position + cameraForward * (-camera.position.y / cameraForward.y)
            : camera.position;

        // Single-threaded on purpose: it appends to one list and one counter, and the crowd is a few hundred units.
        CollectVisibleEmotesJob collectJob = new CollectVisibleEmotesJob
        {
            cameraPosition = camera.position,
            cameraForward = cameraForward,
            viewFocus = viewFocus,
            viewDistance = config.viewDistance,
            headHeight = config.headHeight,
            candidates = candidates,
            visibleHumanCount = visibleHumanCount
        };
        state.Dependency = collectJob.Schedule(state.Dependency);

        // Priority first, then nearest: when the view is crowded the pool shows the emotes that matter most.
        BudgetAndSortJob budgetJob = new BudgetAndSortJob
        {
            candidates = candidates,
            visibleHumanCount = visibleHumanCount,
            emoteBudget = emoteBudget,
            poolSize = config.poolSize,
            crowdLifeLodEntity = crowdLifeLodEntity,
            crowdLifeLodLookup = crowdLifeLodLookup
        };
        state.Dependency = budgetJob.Schedule(state.Dependency);

        float3 flatCameraForward = new float3(camera.forward.x, 0f, camera.forward.z);
        // Yaw-only, same as VisualBillboardSystem: the camera lean is applied elsewhere, a full facing would tilt twice.
        quaternion facingRotation = math.lengthsq(flatCameraForward) < 1e-6f
            ? quaternion.identity
            : quaternion.LookRotation(math.normalize(flatCameraForward), math.up());

        PresentEmoteSlotsJob presentJob = new PresentEmoteSlotsJob
        {
            candidates = candidates.AsDeferredJobArray(),
            emoteBudget = emoteBudget,
            facingRotation = facingRotation,
            firstIconMaterialIndex = config.firstIconMaterialIndex,
            deltaTime = SystemAPI.Time.DeltaTime
        };
        state.Dependency = presentJob.ScheduleParallel(state.Dependency);

        candidates.Dispose(state.Dependency);
        visibleHumanCount.Dispose(state.Dependency);
        emoteBudget.Dispose(state.Dependency);
    }
}

[BurstCompile]
public partial struct CollectVisibleEmotesJob : IJobEntity
{
    public float3 cameraPosition;
    public float3 cameraForward;
    public float3 viewFocus;
    public float viewDistance;
    public float headHeight;
    public NativeList<EmoteCandidate> candidates;
    public NativeReference<int> visibleHumanCount;

    private void Execute(Entity entity, in EmoteState emoteState, in LocalToWorld localToWorld)
    {
        float3 toUnit = localToWorld.Position - cameraPosition;
        float cameraDistance = math.length(toUnit);
        if (cameraDistance < 1e-4f || math.dot(toUnit / cameraDistance, cameraForward) <= 0.2f)
        {
            return;
        }
        float distance = math.distance(localToWorld.Position.xz, viewFocus.xz);
        if (distance > viewDistance)
        {
            return;
        }

        visibleHumanCount.Value += 1;
        if (emoteState.icon == EmoteIcon.None)
        {
            return;
        }

        candidates.Add(new EmoteCandidate
        {
            unit = entity,
            headPosition = localToWorld.Position + math.up() * headHeight,
            icon = emoteState.icon,
            priority = emoteState.priority,
            remaining = emoteState.remaining,
            distanceToCamera = distance
        });
    }
}

[BurstCompile]
public struct BudgetAndSortJob : IJob
{
    public NativeList<EmoteCandidate> candidates;
    [ReadOnly] public NativeReference<int> visibleHumanCount;
    public NativeReference<int> emoteBudget;
    public int poolSize;
    public Entity crowdLifeLodEntity;
    public ComponentLookup<CrowdLifeLod> crowdLifeLodLookup;

    public void Execute()
    {
        candidates.Sort(new EmoteCandidateComparer());

        int visibleCount = visibleHumanCount.Value;
        int budget = CrowdLifeBudget.Budget(visibleCount, poolSize);
        emoteBudget.Value = budget;
        crowdLifeLodLookup[crowdLifeLodEntity] = new CrowdLifeLod
        {
            visibleHumans = visibleCount,
            emoteBudget = budget,
            beatIntervalScale = CrowdLifeBudget.BeatIntervalScale(visibleCount)
        };
    }
}

[BurstCompile]
public partial struct PresentEmoteSlotsJob : IJobEntity
{
    const float WorldSize = 0.6f;
    const float PopInSeconds = 0.15f;
    const float FadeOutSeconds = 0.3f;

    [ReadOnly] public NativeArray<EmoteCandidate> candidates;
    [ReadOnly] public NativeReference<int> emoteBudget;
    public quaternion facingRotation;
    public int firstIconMaterialIndex;
    public float deltaTime;

    private void Execute(
        ref LocalTransform localTransform,
        ref MaterialMeshInfo materialMeshInfo,
        ref URPMaterialPropertyBaseColor baseColor,
        ref EmotePoolSlot slot)
    {
        int shownCount = math.min(emoteBudget.Value, candidates.Length);
        if (slot.slotIndex >= shownCount)
        {
            localTransform.Scale = 0f;
            baseColor.Value = new float4(1f, 1f, 1f, 0f);
            slot.owner = Entity.Null;
            slot.shownFor = 0f;
            return;
        }

        EmoteCandidate candidate = candidates[slot.slotIndex];
        if (slot.owner != candidate.unit)
        {
            slot.owner = candidate.unit;
            slot.shownFor = 0f;
        }
        slot.shownFor += deltaTime;

        localTransform.Position = candidate.headPosition;
        localTransform.Rotation = facingRotation;
        localTransform.Scale = WorldSize * math.smoothstep(0f, PopInSeconds, slot.shownFor);
        materialMeshInfo.Material = MaterialMeshInfo.ArrayIndexToStaticIndex((int)candidate.icon - 1 + firstIconMaterialIndex);
        baseColor.Value = new float4(1f, 1f, 1f, math.saturate(candidate.remaining / FadeOutSeconds));
    }
}
