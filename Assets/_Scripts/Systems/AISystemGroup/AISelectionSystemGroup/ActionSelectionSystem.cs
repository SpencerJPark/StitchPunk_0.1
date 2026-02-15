using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AISelectionSystemGroup))]
public partial struct ActionSelectionSystem : ISystem
{
    private BufferLookup<InteractionOccupant> occupantBufferLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SelectedAction>();

        occupantBufferLookup = state.GetBufferLookup<InteractionOccupant>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        occupantBufferLookup.Update(ref state);

        float time = (float)SystemAPI.Time.ElapsedTime;

        new ActionSelectionJob
        {
            time = time,
            occupantBufferLookup = occupantBufferLookup
        }.Schedule();
    }

    [BurstCompile]
    public partial struct ActionSelectionJob : IJobEntity
    {
        public float time;
        public BufferLookup<InteractionOccupant> occupantBufferLookup;

        public void Execute(
            ref SelectedAction selectedAction,
            ref DynamicBuffer<ActionOption> options,
            EnabledRefRW<NeedsAction> needsAction,
            Entity npcEntity,
            [EntityIndexInQuery] int entityIndex)
        {
            if (options.Length == 0)
                return;

            SortDescending(ref options);

            int topCount = math.min(options.Length, 3);

            uint seed = (uint)(entityIndex + 1) * 747796405u + (uint)(time * 1000f);
            seed = math.max(seed, 1u);
            Random random = new Random(seed);

            int chosen = random.NextInt(0, topCount);
            ActionOption selectedOption = options[chosen];

            selectedAction.startedAction = false;

            if (occupantBufferLookup.TryGetBuffer(selectedOption.interactableEntity, out DynamicBuffer<InteractionOccupant> occupantBuffer))
            {
                occupantBuffer.Add(new InteractionOccupant
                {
                    entity = npcEntity,
                    score = selectedOption.score
                });
            }

            needsAction.ValueRW = false;
            options.Clear();
        }

        private static void SortDescending(ref DynamicBuffer<ActionOption> options)
        {
            for (int i = 0; i < options.Length - 1; i++)
            {
                for (int j = i + 1; j < options.Length; j++)
                {
                    if (options[j].score > options[i].score)
                    {
                        ActionOption temp = options[i];
                        options[i] = options[j];
                        options[j] = temp;
                    }
                }
            }
        }
    }
}