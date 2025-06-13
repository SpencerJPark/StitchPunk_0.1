using UnityEngine;

public abstract class CharacterControllerBase : MonoBehaviour, IUpdateObserver
{
    [Header("Dependencies")]
    [SerializeField] protected IInputProvider input;
    [SerializeField] protected CharacterController cc;
    [SerializeField] protected RiveAnimator animator;
    [SerializeField] private MonoBehaviour facingComponent;
    private IFacingController facingController;


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

        if (facingComponent is IFacingController controller)
        {
            facingController = controller;
        }
        else
        {
            Debug.LogWarning($"{name}: Facing component doesn't implement IFacingController.");
        }

        fallSpeed = 0f;
    }

    void OnDisable()
    {
        UpdateManager.UnregisterObserver(this);
    }

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

    protected virtual void UpdateMovementAnimation(bool moving)
    {
        if (currentState == null) return;

        string animState = moving ? currentState.WalkAnimation : currentState.IdleAnimation;
        animator.SetEnum("Actions", animState);
    }

    protected virtual void UpdateActionAnimation(string action)
    {
        animator.SetEnum("Actions", action);
    }

    protected virtual void FireTriggerAnimation(string trigger)
    {
        animator.Trigger(trigger);
    }
}
