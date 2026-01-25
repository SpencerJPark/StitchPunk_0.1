using Unity.Burst;
using Unity.Entities;

[BurstCompile]
public static class AIScoreUtil
{
    public static void SetScore(ref DynamicBuffer<ActionScore> scores, ActionType actionType, float score, bool isValid)
    {
        for (int i = 0; i < scores.Length; i++)
        {
            if (scores[i].actionType == actionType)
            {
                scores[i] = new ActionScore
                {
                    actionType = actionType,
                    score = score,
                    isValid = isValid
                };
                return;
            }
        }

        scores.Add(new ActionScore
        {
            actionType = actionType,
            score = score,
            isValid = isValid
        });
    }

    public static float GetScore(ref DynamicBuffer<ActionScore> scores, ActionType actionType)
    {
        for (int i = 0; i < scores.Length; i++)
        {
            if (scores[i].actionType == actionType)
            {
                return scores[i].score;
            }
        }
        return 0f;
    }

    public static void AddToScore(ref DynamicBuffer<ActionScore> scores, ActionType actionType, float amount)
    {
        for (int i = 0; i < scores.Length; i++)
        {
            if (scores[i].actionType == actionType)
            {
                ActionScore current = scores[i];
                current.score += amount;
                scores[i] = current;
                return;
            }
        }

        scores.Add(new ActionScore
        {
            actionType = actionType,
            score = amount,
            isValid = true
        });
    }

    public static void MultiplyScore(ref DynamicBuffer<ActionScore> scores, ActionType actionType, float multiplier)
    {
        for (int i = 0; i < scores.Length; i++)
        {
            if (scores[i].actionType == actionType)
            {
                ActionScore current = scores[i];
                current.score *= multiplier;
                scores[i] = current;
                return;
            }
        }
    }
}