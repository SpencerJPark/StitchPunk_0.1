using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

[BurstCompile]
[UpdateInGroup(typeof(AISelectionSystemGroup))]
public partial struct ActionSelectionSystem : ISystem
{
    private BufferLookup<InteractionOccupant>    occupantBufferLookup;
    private ComponentLookup<Interaction>         interactionLookup;
    private ComponentLookup<InteractionProvider> interactionProviderLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SelectedAction>();

        occupantBufferLookup      = state.GetBufferLookup<InteractionOccupant>(false);
        interactionLookup         = state.GetComponentLookup<Interaction>(true);
        interactionProviderLookup = state.GetComponentLookup<InteractionProvider>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        occupantBufferLookup.Update(ref state);
        interactionLookup.Update(ref state);
        interactionProviderLookup.Update(ref state);

        float time = (float)SystemAPI.Time.ElapsedTime;

        state.Dependency = new ActionSelectionJob
        {
            time                      = time,
            occupantBufferLookup      = occupantBufferLookup,
            interactionLookup         = interactionLookup,
            interactionProviderLookup = interactionProviderLookup,
        }.Schedule(state.Dependency);
    }

    // [WithAll(typeof(NeedsAction))] is required — NeedsAction is IEnableableComponent.
    // Without it the query uses WithPresent (runs on all units) and FilterPreviousEntity
    // re-enables NeedsAction on dead/inactive units that have 0 options, creating an infinite loop.
    [BurstCompile]
    [WithAll(typeof(ActiveBrain))]
    [WithAll(typeof(NeedsAction))]
    public partial struct ActionSelectionJob : IJobEntity
    {
        public float time;
        public BufferLookup<InteractionOccupant>    occupantBufferLookup;
        [ReadOnly] public ComponentLookup<Interaction>         interactionLookup;
        public            ComponentLookup<InteractionProvider> interactionProviderLookup;

        public void Execute(
            ref SelectedAction selectedAction,
            ref DynamicBuffer<ActionOption> options,
            EnabledRefRW<NeedsAction> needsActionEnabled,
            Entity npcEntity,
            [EntityIndexInQuery] int entityIndex)
        {
            // Filter out previous entity first to avoid re-selecting the same target.
            if (FilterPreviousEntity(selectedAction, ref options, needsActionEnabled))
                return;

            // Sort AFTER filtering so the top-3 pick is based on the filtered list.
            SortDescending(ref options);

            Random random    = EntityUtils.CreateRandom(entityIndex, time);
            int    topCount  = math.min(options.Length, 3);
            int    startIndex = random.NextInt(0, topCount);

            // Try from random start, wrap through all options.
            for (int attempt = 0; attempt < options.Length; attempt++)
            {
                int          i         = (startIndex + attempt) % options.Length;
                ActionOption candidate = options[i];

                // ── Interaction options: enforce occupancy cap ─────────────────
                if (candidate.category == ActionCategory.Interaction)
                {
                    if (!occupantBufferLookup.TryGetBuffer(candidate.targetEntity,
                        out DynamicBuffer<InteractionOccupant> occupantBuffer))
                        continue;

                    if (!interactionLookup.TryGetComponent(candidate.targetEntity,
                        out Interaction interaction))
                        continue;

                    if (occupantBuffer.Length >= interaction.maxOccupants)
                        continue;

                    occupantBuffer.Add(new InteractionOccupant
                    {
                        entity        = npcEntity,
                        score         = candidate.score,
                        behaviourType = candidate.behaviourType,
                    });

                    if (occupantBuffer.Length >= interaction.maxOccupants)
                        interactionProviderLookup.SetComponentEnabled(candidate.targetEntity, false);
                }
                // ── Behaviour / combat options: no occupancy check ─────────────
                // (Chase, Attack, Flee, Wander don't own world-object slots.)

                // Commit selection.
                selectedAction.category      = candidate.category;
                selectedAction.targetEntity  = candidate.targetEntity;
                selectedAction.targetPosition = candidate.targetPosition;
                needsActionEnabled.ValueRW   = false;
                options.Clear();
                return;
            }

            // No valid option found — try again next frame.
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

            // Remove all options matching the previously selected target.
            for (int i = options.Length - 1; i >= 0; i--)
            {
                if (options[i].targetEntity == selectedAction.targetEntity)
                    options.RemoveAt(i);
            }

            // If no options remain after filtering, wait for next frame.
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
                        options[i]        = options[j];
                        options[j]        = temp;
                    }
                }
            }
        }
    }
}
