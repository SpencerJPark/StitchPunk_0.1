using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AISystemGroup), OrderFirst = true)]
public partial struct SwapBrainSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BrainLibrary>();
        state.RequireForUpdate<SwapBrainRequest>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();
        state.Dependency = new SwapBrainJob
        {
            brainLibraryBlob = brainLibrary.blob,
            elapsedTime      = (float)SystemAPI.Time.ElapsedTime,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(SwapBrainRequest))]
public partial struct SwapBrainJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> brainLibraryBlob;
    public float elapsedTime;

    public void Execute(
        ref Brain brain,
        ref DynamicBuffer<Motivation>    behaviourBuffer,
        ref DynamicBuffer<ActionOption> actionOptionBuffer,
        ref CurrentAction              currentAction,
        EnabledRefRW<SwapBrainRequest>  swapRequestEnabled,
        EnabledRefRW<ActionRequest>       needsActionEnabled,
        ref SwapBrainRequest            request,
        in  Entity                      entity)
    {
        ref BrainLibraryBlob lib = ref brainLibraryBlob.Value;
        int index = lib.FindBrainIndex(request.newBrain);
        if (index < 0)
        {
            swapRequestEnabled.ValueRW = false;
            return;
        }

        ref BrainEntryBlob brainEntry = ref lib.entries[index];

        brain.activeBrain = request.newBrain;

        behaviourBuffer.Clear();
        for (int i = 0; i < brainEntry.motivation.Length; i++)
            behaviourBuffer.Add(BrainUtil.DefaultBehaviour(brainEntry.motivation[i]));

        uint seed = math.max((uint)entity.Index * 2654435761u ^ (uint)(elapsedTime * 1000f), 1u);
        Random rng = new Random(seed);
        BrainUtil.PopulateRandomBehaviours(
            behaviourBuffer,
            ref brainEntry.randomMotivations,
            brainEntry.randomMotivationAmount,
            ref rng);

        actionOptionBuffer.Clear();
        currentAction = default;

        swapRequestEnabled.ValueRW = false;
        needsActionEnabled.ValueRW = true;
    }
}
