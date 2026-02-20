using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

[BurstCompile]
[UpdateInGroup(typeof(AISelectionSystemGroup))]
public partial struct ActionSelectionSystem : ISystem
{
    private BufferLookup<InteractionOccupant> occupantBufferLookup;
    private ComponentLookup<Interaction> interactionLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SelectedAction>();

        occupantBufferLookup = state.GetBufferLookup<InteractionOccupant>(false);
        interactionLookup = state.GetComponentLookup<Interaction>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        occupantBufferLookup.Update(ref state);
        interactionLookup.Update(ref state);

        float time = (float)SystemAPI.Time.ElapsedTime;

        state.Dependency = new ActionSelectionJob
        {
            time = time,
            occupantBufferLookup = occupantBufferLookup,
            interactionLookup = interactionLookup
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    public partial struct ActionSelectionJob : IJobEntity
    {
        public float time;
        public BufferLookup<InteractionOccupant> occupantBufferLookup;
        [ReadOnly] public ComponentLookup<Interaction> interactionLookup;

        public void Execute(
            ref SelectedAction selectedAction,
            ref DynamicBuffer<ActionOption> options,
            EnabledRefRW<NeedsAction> needsActionEnabled,
            Entity npcEntity,
            [EntityIndexInQuery] int entityIndex)
        {
            // Filter out previous entity first
            if (FilterPreviousEntity(selectedAction, ref options, needsActionEnabled))
                return;

            // Sort AFTER filtering so top 3 selection is based on filtered list
            SortDescending(ref options);

            Random random = EntityUtils.CreateRandom(entityIndex, time);

            // Randomly pick from top 3 valid options (now based on filtered length)
            int topCount = math.min(options.Length, 3);
            int startIndex = random.NextInt(0, topCount);

            // Try from random start, then wrap around through all options
            for (int attempt = 0; attempt < options.Length; attempt++)
            {
                int i = (startIndex + attempt) % options.Length;
                ActionOption candidate = options[i];

                if (!occupantBufferLookup.TryGetBuffer(candidate.interactableEntity,
                    out DynamicBuffer<InteractionOccupant> occupantBuffer))
                    continue;

                if (!interactionLookup.TryGetComponent(candidate.interactableEntity,
                    out Interaction interaction))
                    continue;

                if (occupantBuffer.Length >= interaction.maxOccupants)
                    continue;

                occupantBuffer.Add(new InteractionOccupant
                {
                    entity = npcEntity,
                    score = candidate.score
                });

                selectedAction.previous = selectedAction.current;
                selectedAction.current = candidate.interactableEntity;
                needsActionEnabled.ValueRW = false;
                options.Clear();
                return;
            }

            // No valid option found, try again next frame
            needsActionEnabled.ValueRW = true;
            options.Clear();
        }

        private static bool FilterPreviousEntity(
            SelectedAction selectedAction,
            ref DynamicBuffer<ActionOption> options,
            EnabledRefRW<NeedsAction> needsActionEnabled)
        {
            if (options.Length == 0)
                return true;

            // Remove all options matching the previous entity
            for (int i = options.Length - 1; i >= 0; i--)
            {
                if (options[i].interactableEntity == selectedAction.previous ||
                    options[i].interactableEntity == selectedAction.current)
                {
                    options.RemoveAt(i);
                }
            }

            // If no options remain after filtering, wait for next frame
            if (options.Length == 0)
            {
                needsActionEnabled.ValueRW = true;
                return true;
            }

            return false;
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