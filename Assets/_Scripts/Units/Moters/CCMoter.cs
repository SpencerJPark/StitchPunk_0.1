using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class CCMotor : UnitMotorBase
{
    [SerializeField] CharacterController cc;
    [SerializeField] float speed = 4f;
    [SerializeField] float gravity = 20f;
    [SerializeField] float maxFall = 30f;

    public Vector2 MoveInput; // set by your player input provider
    float fallSpeed;
    Vector3 velocity;

    void Reset() => cc = GetComponent<CharacterController>();

    public override Vector3 Velocity => velocity;
    public override bool IsGrounded => cc.isGrounded;

    public override void Tick(float dt)
    {
        Vector3 move = new Vector3(MoveInput.x, 0f, MoveInput.y) * speed;

        // gravity
        if (!cc.isGrounded)
        {
            fallSpeed = Mathf.Min(fallSpeed + gravity * dt, maxFall);
        }
        else
        {
            fallSpeed = 0f;
        }
        move.y = -fallSpeed;

        cc.Move(move * dt);
        velocity = cc.velocity;
    }

    public override void Halt()
    {
        fallSpeed = 0f;
        velocity = Vector3.zero;
    }

    // Movement Updates
    protected virtual void HandleMovement()
    {
        unitModel.SetMovementVector(new Vector3(input.MoveInput.x, 0f, input.MoveInput.y));
        unitModel.SetMoving(unitModel.MovementVector.sqrMagnitude > 0.01f);

        float speed = unitModel != null ? unitModel.MoveSpeed : 3f;
        Vector3 motion = unitModel.MovementVector * speed;

        // apply gravity if needed
        if (unitModel != null &&
            (unitModel.Movement == MovementType.Grounded ||
             unitModel.Movement == MovementType.Floating))
        {
            HandleGravity();
            motion.y = -unitModel.FallSpeed;
        }

        // **CharacterController.Move expects units per second**
        cc.Move(motion * Time.deltaTime);
    }

    private void HandleGravity()
    {
        unitModel.SetGrounding(Physics.Raycast(transform.position, Vector3.down, unitModel.GroundCheckDistance, unitModel.GroundLayer));

        if (!unitModel.IsGrounded)
        {
            unitModel.SetFallSpeed(unitModel.FallSpeed + unitModel.Gravity * Time.deltaTime);
            unitModel.SetFallSpeed(Mathf.Clamp(unitModel.FallSpeed, 0f, unitModel.MaxFallSpeed));
        }
        else
        {
            unitModel.SetFallSpeed(0f);
        }
    }
}
