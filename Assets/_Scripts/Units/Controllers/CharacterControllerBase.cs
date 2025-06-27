using UnityEngine;

public abstract class CharacterControllerBase : MonoBehaviour, IUpdateObserver
{
    [Header("Dependencies")]
    [SerializeField] protected InputProviderBase input;
    [SerializeField] protected CharacterController cc;
    [SerializeField] protected RiveAnimator animator;
    // Option B: serialize a generic MonoBehaviour and cast it in OnEnable
    [SerializeField]
    private FacingDirectionBase facingController;

    [Header("State Data")]
    [SerializeField] protected CharacterStateData currentState;


    [Header("Movement Profile")]
    [SerializeField] private MovementProfile movementProfile;


    private bool isGrounded;
    private float fallSpeed;
    protected Vector3 moveDirection;
    protected bool isMoving;


    void OnEnable()
    {
        UpdateManager.RegisterObserver(this);

        fallSpeed = 0f;
    }

    void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate()
    {
        HandleMovement();
        HandleFacing();
        HandleAction();
    }

    public virtual void ApplyState(CharacterStateData state)
    {
        currentState = state;
    }

    protected virtual void HandleMovement()
    {
        moveDirection = new Vector3(input.MoveInput.x, 0f, input.MoveInput.y);
        isMoving = moveDirection.sqrMagnitude > 0.01f;

        float speed = currentState != null ? currentState.MoveSpeed : 3f;
        Vector3 motion = moveDirection * speed;

        // apply gravity if needed
        if (movementProfile != null &&
            (movementProfile.movementType == MovementType.Grounded ||
             movementProfile.movementType == MovementType.Floating))
        {
            HandleGravity();
            motion.y = -fallSpeed;
        }

        // **CharacterController.Move expects units per second**
        cc.Move(motion * Time.deltaTime);

        UpdateMovementAnimation(isMoving);
    }

    private void HandleGravity()
    {
        isGrounded = Physics.Raycast(transform.position, Vector3.down,
            movementProfile.groundCheckDistance, movementProfile.groundLayer);

        if (!isGrounded)
        {
            fallSpeed += movementProfile.gravity * Time.deltaTime;
            fallSpeed = Mathf.Clamp(fallSpeed, 0f, movementProfile.maxFallSpeed);
        }
        else
        {
            fallSpeed = 0f;
        }
    }

    protected virtual void HandleFacing()
    {
        if (!isMoving || facingController == null)
            return;

        facingController.UpdateFacing(moveDirection);
    }

    protected virtual void HandleAction()
    {
        // UpdateActionAnimation() or FireTriggerAnimation()
    }

    // Handle Animation

    protected virtual void UpdateMovementAnimation(bool moving)
    {
        if (currentState == null) return;

        string animState = moving ? currentState.WalkAnimation : currentState.IdleAnimation;
        animator.SetEnum("Actions", animState.ToString());
    }

    protected virtual void UpdateActionAnimation(string action)
    {
        animator.SetEnum("Actions", action);
    }

    protected virtual void FireTriggerAnimation(string trigger)
    {
        animator.Trigger(trigger);
    }


    // Other Object Interactions

    /// <summary>
    /// Called by your vehicle code when the player gets into a seat.
    /// Disables all movement, gravity, and collision so we can snap them into place.
    /// </summary>
    public void OnMount()
    {
        // Set Sitting Animation
        // stop ObservedUpdate entirely
        // turn off CharacterController collision & gravity
    }

    /// <summary>
    /// Called by your vehicle code when the player leaves the seat.
    /// Restores movement and gravity.
    /// </summary>
    public void OnDismount()
    {
        // Set Idle Animation
        // Reset movement ability and gravity
    }
}
