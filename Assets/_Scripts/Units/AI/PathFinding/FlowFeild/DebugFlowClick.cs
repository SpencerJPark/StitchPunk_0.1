// Assets/_Scripts/Units/AI/FlowField/DebugFlowClick.cs
using UnityEngine;

public class DebugFlowClick : MonoBehaviour
{
    public LayerMask groundMask = ~0;

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && FlowFieldSystem.Instance)
        {
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 200f, groundMask))
                FlowFieldSystem.Instance.BuildToGoal(hit.point);
        }
    }
}
