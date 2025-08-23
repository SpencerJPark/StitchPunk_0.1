using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central manager that handles local avoidance (separation + wall feelers) 
/// for all registered PathInputProviders.
/// </summary>
/// 

[CreateAssetMenu(fileName = "Local Avoidance", menuName = "Scriptable Systems/Local Avoidance", order = 0)]
public class LocalAvoidanceSystem : ScriptableSystem
{
    // Tunables (can also be ScriptableObject-driven if you want)
    [Header("Separation Settings")]
    public float separationRadius = 1.0f;
    public float separationStrength = 1.0f;

    [Header("Static Obsticle Avoidance Settings")]
    public float feelerLength = 1.0f;
    public LayerMask staticObstacleMask;

    [Header("Dynamic Obsticle Avoidance Settings")]
    public LayerMask dynamicObstacleMask;

    private readonly List<PathfindingComponent> agents = new();

    public void Register(PathfindingComponent agent)
    {
        if (!agents.Contains(agent))
            agents.Add(agent);
    }

    public void Unregister(PathfindingComponent agent)
    {
        agents.Remove(agent);
    }

    public override void Tick()
    {
        // Clear from last frame
        foreach (var agent in agents)
            agent.ClearAvoidanceNudge();

        ComputeSeparation();
        ComputeWallAvoidance();
    }

    void ComputeSeparation()
    {
        int count = agents.Count;
        for (int i = 0; i < count; i++)
        {
            Vector3 posA = agents[i].transform.position;
            for (int j = i + 1; j < count; j++)
            {
                Vector3 posB = agents[j].transform.position;
                Vector3 diff = posA - posB;
                diff.y = 0f; // planar only

                float dist = diff.magnitude;
                if (dist > 0f && dist < separationRadius)
                {
                    float push = (1f - dist / separationRadius) * separationStrength;
                    Vector3 nudge = diff.normalized * push;

                    agents[i].AddAvoidanceNudge(nudge);
                    agents[j].AddAvoidanceNudge(-nudge);
                }
            }
        }
    }

    void ComputeWallAvoidance()
    {
        foreach (var agent in agents)
        {
            Vector3 pos = agent.transform.position;
            Vector3 forward = agent.transform.forward;

            // single forward ray feeler
            if (Physics.Raycast(pos, forward, out RaycastHit hit, feelerLength, staticObstacleMask))
            {
                // steer sideways away from wall
                Vector3 normal = hit.normal;
                normal.y = 0f;
                agent.AddAvoidanceNudge(normal.normalized);
            }
        }
    }
}
