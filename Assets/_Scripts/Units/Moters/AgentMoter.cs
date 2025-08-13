using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class AgentMotor : UnitMotorBase
{
    [SerializeField] NavMeshAgent agent;
    [SerializeField] bool useKinematicMove = false; // set true if you want manual position writes
    Vector3 lastPos;

    void Reset() => agent = GetComponent<NavMeshAgent>();

    void Awake()
    {
        // Let agent move position, but NEVER rotate (billboard handles visuals)
        agent.updatePosition = !useKinematicMove;
        agent.updateRotation = false;
        lastPos = transform.position;
    }

    public override Vector3 Velocity => agent.velocity;
    public override Vector3 Desired  => agent.desiredVelocity;

    public override void Tick(float dt)
    {
        if (useKinematicMove)
        {
            // Kinematic: advance by desired velocity, then sync agent
            Vector3 step = agent.desiredVelocity * dt;
            transform.position += step;
            agent.nextPosition  = transform.position;
        }
        // If not kinematic, agent moves itself; nothing to do here.
        lastPos = transform.position;
    }

    public void SetDestination(Vector3 world) {
        if (agent.isOnNavMesh) agent.SetDestination(world);
    }

    public override void Halt()
    {
        if (agent.isOnNavMesh) agent.ResetPath();
    }
}
