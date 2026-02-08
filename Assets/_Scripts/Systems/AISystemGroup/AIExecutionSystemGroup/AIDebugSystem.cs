using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(AISystemGroup))]
public partial struct AIDebugSystem : ISystem
{
    private float logTimer;
    private const float LOG_INTERVAL = 2f;
    private bool hasValidated;

    public void OnCreate(ref SystemState state)
    {
        logTimer = 0f;
        hasValidated = false;
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();

        logTimer += SystemAPI.Time.DeltaTime;

        bool shouldLog = logTimer >= LOG_INTERVAL;
        if (shouldLog)
        {
            logTimer = 0f;
        }

        if (!hasValidated)
        {
            ValidateBrains(ref state);
            hasValidated = true;
        }

        if (shouldLog)
        {
            LogBrainStatus(ref state);
        }
    }

    private void ValidateBrains(ref SystemState state)
    {
        int brainCount = 0;
        int validBrains = 0;
        int brainsWithScores = 0;
        int brainsWithWander = 0;

        foreach (var (brainLink, entity) in SystemAPI.Query<RefRO<BrainLink>>().WithEntityAccess())
        {
            brainCount++;
            bool isValid = true;
            Entity body = brainLink.ValueRO.body;

            if (body == Entity.Null)
            {
                Debug.LogError($"[AI Validation] Brain {entity.Index} has NULL body reference!");
                isValid = false;
            }
            else if (!SystemAPI.HasComponent<LocalTransform>(body))
            {
                Debug.LogError($"[AI Validation] Brain {entity.Index} body entity does not exist or has no transform!");
                isValid = false;
            }
            else if (!SystemAPI.HasComponent<UnitMover>(body))
            {
                Debug.LogError($"[AI Validation] Brain {entity.Index} body has no UnitMover!");
                isValid = false;
            }

            if (SystemAPI.HasBuffer<ActionScore>(entity))
            {
                brainsWithScores++;
            }
            else
            {
                Debug.LogError($"[AI Validation] Brain {entity.Index} has no ActionScore buffer!");
                isValid = false;
            }

            if (SystemAPI.HasComponent<CanWander>(entity))
            {
                brainsWithWander++;
            }

            if (!SystemAPI.HasComponent<SelectedAction>(entity))
            {
                Debug.LogError($"[AI Validation] Brain {entity.Index} has no SelectedAction!");
                isValid = false;
            }

            if (!SystemAPI.HasComponent<WanderState>(entity))
            {
                Debug.LogWarning($"[AI Validation] Brain {entity.Index} has no WanderState!");
            }

            if (isValid)
            {
                validBrains++;
            }
        }

        Debug.Log($"[AI Validation] Brains: {brainCount} | Valid: {validBrains} | WithScores: {brainsWithScores} | WithWander: {brainsWithWander}");

        if (brainCount == 0)
        {
            Debug.LogWarning("[AI Validation] No brains found! Check your prefab setup.");
        }
    }

    private void LogBrainStatus(ref SystemState state)
    {
        foreach (var (selectedAction, brainLink, entity) in SystemAPI.Query<RefRO<SelectedAction>, RefRO<BrainLink>>().WithEntityAccess())
        {
            Entity body = brainLink.ValueRO.body;
            string bodyStatus = "NO BODY";
            string wanderStatus = "";

            if (body != Entity.Null &&
                SystemAPI.HasComponent<LocalTransform>(body) &&
                SystemAPI.HasComponent<UnitMover>(body))
            {
                LocalTransform transform = SystemAPI.GetComponent<LocalTransform>(body);
                UnitMover mover = SystemAPI.GetComponent<UnitMover>(body);

                bodyStatus = $"Pos:({transform.Position.x:F1},{transform.Position.z:F1}) Target:({mover.targetPosition.x:F1},{mover.targetPosition.z:F1}) Moving:{mover.isMoving}";
            }

            if (SystemAPI.HasComponent<WanderState>(entity))
            {
                WanderState wanderState = SystemAPI.GetComponent<WanderState>(entity);
                wanderStatus = $" | WanderTarget:({wanderState.wanderTarget.x:F1},{wanderState.wanderTarget.z:F1}) Radius:{wanderState.wanderRadius}";
            }

            string scoreText = "";
            if (SystemAPI.HasBuffer<ActionScore>(entity))
            {
                DynamicBuffer<ActionScore> scores = SystemAPI.GetBuffer<ActionScore>(entity);
                for (int i = 0; i < scores.Length; i++)
                {
                    if (scores[i].isValid && scores[i].score > 0f)
                    {
                        scoreText += $" {scores[i].actionType}:{scores[i].score:F2}";
                    }
                }
            }

            if (string.IsNullOrEmpty(scoreText))
            {
                scoreText = " NO VALID SCORES";
            }

            Debug.Log($"[AI] Brain {entity.Index} | Action: {selectedAction.ValueRO.current} | Body: {bodyStatus}{wanderStatus} | Scores:{scoreText}");
        }
    }
}