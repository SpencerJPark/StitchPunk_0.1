using UnityEngine;

public class DebugClickToMove : MonoBehaviour
{
    public Camera cam;
    public Brain target;
    void Awake() { if (!cam) cam = Camera.main; }
    void Update()
    {
        if (Input.GetMouseButtonDown(0) && cam && target)
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 200f))
            {
                var pos = hit.point;
                if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var nh, 2f, UnityEngine.AI.NavMesh.AllAreas))
                    pos = nh.position;
                //target.outputSpace = MoveSpace.WorldXZ;      // << match CCMotor
                target.SetDestination(pos);
            }
        }
    }
}
