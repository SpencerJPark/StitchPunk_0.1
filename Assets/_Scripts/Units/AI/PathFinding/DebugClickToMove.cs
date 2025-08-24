using UnityEngine;
using UnityEngine.AI;
using AI;

namespace PathFinding
{
    public class DebugClickToMove : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [SerializeField] private Brain target;

        void Awake()
        {
            if (!cam) cam = Camera.main;
            if (!target) target = FindObjectOfType<Brain>(); // fallback
        }

        void Update()
        {
            if (Input.GetMouseButtonDown(0) && cam && target)
            {
                var ray = cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                {
                    Vector3 pos = hit.point;
                    if (NavMesh.SamplePosition(pos, out NavMeshHit nh, 2f, NavMesh.AllAreas))
                        pos = nh.position;

                    target.SetDestination(pos);
                }
            }
        }
    }
}