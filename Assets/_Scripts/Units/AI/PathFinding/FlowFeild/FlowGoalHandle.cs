// Assets/_Scripts/Units/AI/FlowField/FlowGoalHandle.cs
using UnityEngine;

/// <summary>
/// Tiny helper to push goal updates to FlowFieldSystem on a cadence.
/// Attach near your target object if it moves.
/// </summary>
public class FlowGoalHandle : MonoBehaviour
{
    public float rebuildInterval = 0.3f;

    float _next;
    void Update()
    {
        if (!FlowFieldSystem.Instance) return;
        if (Time.time < _next) return;
        FlowFieldSystem.Instance.BuildToGoal(transform.position);
        _next = Time.time + rebuildInterval;
    }
}
