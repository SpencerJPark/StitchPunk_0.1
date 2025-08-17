using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class AgentMotor : UnitMotorBase
{
    [SerializeField] NavMeshAgent agent;
    [SerializeField] private UnitMovementData data;

    // cached ref
    float Speed => data ? data.moveSpeed : 3f;

    // motor specific
    [SerializeField] private readonly bool useKinematicMove = false; // set true if you want manual position writes
    private Vector3 lastPos;


    void Reset() => agent = GetComponent<NavMeshAgent>();

    public override void Initialize(UnitMovementData movementData)
    {
        data = movementData;

        if (!agent) agent = GetComponent<NavMeshAgent>();

        // Let agent move position, but NEVER rotate (billboard handles visuals)
        agent.updatePosition = !useKinematicMove;
        agent.updateRotation = false;
        lastPos = transform.position;
    }

    public override void SetDestination(Vector3 worldPosition)
    {
        if (!agent) return;
        agent.isStopped = false;
        agent.SetDestination(worldPosition);
    }

    public override void SetMoveDirection(Vector3 _) { /* no-op */ }

    public override void Tick(float dt)
    {
        if (!agent) return;

        // Direction for anim (planar, normalized)
        Vector3 planarVel = new Vector3(agent.velocity.x, 0f, agent.velocity.z);
        if (planarVel.sqrMagnitude > 1e-6f) MovementVector = planarVel.normalized;
        else
        {
            Vector3 want = new Vector3(agent.desiredVelocity.x, 0f, agent.desiredVelocity.z);
            MovementVector = want.sqrMagnitude > 1e-6f ? want.normalized : Vector3.zero;
        }
    }

    public override void Halt()
    {
        if (!agent) return;
        agent.isStopped = true;
        agent.ResetPath();
        MovementVector = Vector3.zero;
    }
    
    public override void Go()
    {
        if (!agent) return;
        agent.isStopped = false;
        agent.ResetPath();
        MovementVector = Vector3.zero;
    }
}
