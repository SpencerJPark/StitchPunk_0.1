using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class CCMotor : UnitMotorBase
{
    [SerializeField] private CharacterController cc;
    [SerializeField] private UnitMovementData data;

    // cached ref
    float Speed => data ? data.moveSpeed : 3f;

    // motor specific
    [SerializeField] private readonly float Gravity = 9.8f;
    [SerializeField] private readonly float MaxFall = 20f;
    [SerializeField] private readonly float GroundCheckDistance = 0.2f;
    [SerializeField] private readonly LayerMask GroundLayer;


    bool isGrounded;
    float fallSpeed;
    Vector2 moveInput; // x=world x, y=world z

    public override bool IsGrounded => isGrounded;

    void Reset() => cc = GetComponent<CharacterController>();

    public override void Build(UnitMovementData movementData) => data = movementData;

    public override void SetMoveDirection(Vector3 worldDirectionXZ)
    {
        // accept either (x,0,z) or (x,y,0) by falling back to y if z==0
        float z = Mathf.Abs(worldDirectionXZ.z) > 1e-6f ? worldDirectionXZ.z : worldDirectionXZ.y;
        moveInput = new Vector2(worldDirectionXZ.x, z);
    }

    public override void Tick(float dt)
    {
        // Grounding
        isGrounded = CheckGrounded();

        // Gravity
        fallSpeed = isGrounded ? 0f : Mathf.Min(fallSpeed + Gravity * dt, MaxFall);

        // Planar motion
        Vector3 planar = new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 motion = planar * Speed;
        motion.y = -fallSpeed;

        // Move & output MovementVector for anim (direction only, planar)
        cc.Move(motion * dt);
        var planarVel = new Vector3(cc.velocity.x, 0f, cc.velocity.z);
        MovementVector = planarVel.sqrMagnitude > 1e-6f
            ? planarVel.normalized
            : (planar.sqrMagnitude > 1e-6f ? planar.normalized : Vector3.zero);
    }

    public override void Halt()
    {
        fallSpeed = 0f;
        moveInput = Vector2.zero;
        MovementVector = Vector3.zero;
    }

    bool CheckGrounded()
    {
        if (!cc) return false;
        Vector3 feet = transform.position + cc.center + Vector3.down * (cc.height * 0.5f - cc.radius);
        Vector3 origin = feet + Vector3.up * 0.05f;
        float rayLen = GroundCheckDistance + cc.skinWidth;
        return Physics.Raycast(origin, Vector3.down, rayLen, GroundLayer, QueryTriggerInteraction.Ignore);
        // or: return Physics.Raycast(...) || cc.isGrounded;
    }
}
