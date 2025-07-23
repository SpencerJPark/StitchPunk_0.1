using UnityEngine;

public abstract class CharacterControllerBase : MonoBehaviour, IUpdateObserver
{
    [Header("Controller Dependencies")]
    [SerializeField] protected IInputProvider input;
    [SerializeField] protected CharacterController cc;
    [SerializeField] protected Camera mainCamera;


    [Header("View Dependencies")]
    [SerializeField] protected RiveAnimator animator;


    [Header("Model Dependencies")]
    [SerializeField] protected UnitDataProfile unitDataProfile;

    [Header("Optional")]
     [SerializeField] protected UnitStateData currentState;


    protected IUnitData unitData;

    // ───────────────────────────────────────────────────────────────
    // Override this in subclasses to supply the correct data instance
    protected abstract IUnitData CreateUnitData();
    // ───────────────────────────────────────────────────────────────


    // Will swith to dependecy injection
    protected virtual void Awake()
    {
        // Build your data model here
        unitData = CreateUnitData();
        if (unitData == null)
            Debug.LogError($"{name} failed to CreateUnitData()");
    } 
    
    void OnEnable()
    {
        UpdateManager.RegisterObserver(this);

        unitData.FallSpeed = 0f;
    }

    void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate()
    {
        if (!unitData.Mount)
        {
            HandleMovement();
        }
        
        UpdateFacing();

        HandleAction();
    }

    public virtual void ApplyState(UnitStateData state)
    {
        currentState = state;
    }

    protected virtual void HandleMovement()
    {
        unitData.MovementVector = new Vector3(input.MoveInput.x, 0f, input.MoveInput.y);
        unitData.IsMoving = unitData.MovementVector.sqrMagnitude > 0.01f;

        float speed = unitData != null ? unitData.MoveSpeed : 3f;
        Vector3 motion = unitData.MovementVector * speed;

        // apply gravity if needed
        if (unitData != null &&
            (unitData.Movement == MovementType.Grounded ||
             unitData.Movement == MovementType.Floating))
        {
            HandleGravity();
            motion.y = -unitData.FallSpeed;
        }

        // **CharacterController.Move expects units per second**
        cc.Move(motion * Time.deltaTime);

        UpdateMovementAnimation();
    }

    private void HandleGravity()
    {
        unitData.IsGrounded = Physics.Raycast(transform.position, Vector3.down,
            unitData.GroundCheckDistance, unitData.GroundLayer);

        if (!unitData.IsGrounded)
        {
            unitData.FallSpeed += unitData.Gravity * Time.deltaTime;
            unitData.FallSpeed = Mathf.Clamp(unitData.FallSpeed, 0f, unitData.MaxFallSpeed);
        }
        else
        {
            unitData.FallSpeed = 0f;
        }
    }

    public void UpdateFacing()
    {
        if (mainCamera == null || animator == null)
            return;

        // Convert movement to camera-relative direction
        Vector3 camForward = mainCamera.transform.forward;
        camForward.y = 0f;

        Quaternion camRot = Quaternion.LookRotation(camForward);
        Vector3 camRelativeMove = camRot * unitData.MovementVector;

        Vector2 dir = new Vector2(camRelativeMove.x, camRelativeMove.z).normalized;

        Direction newDirection = DirectionUtil.GetDirection(dir);

        if (newDirection != unitData.CurrentDirection)
        {
            unitData.CurrentDirection = newDirection;
            animator.SetEnum("Direction", unitData.CurrentDirection.ToString());
        }
    }

    protected virtual void HandleAction()
    {
        // UpdateActionAnimation() or FireTriggerAnimation()
    }

    // Handle Animation

    protected virtual void UpdateMovementAnimation()
    {
        Actions animState = unitData.IsMoving ? unitData.WalkAnimation : unitData.IdleAnimation;
        animator.SetEnum("Actions", animState.ToString());
    }

    protected virtual void UpdateActionAnimation(Actions action)
    {
        animator.SetEnum("Actions", action.ToString());
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
        UpdateActionAnimation(Actions.Drive);
        cc.enabled = false;
        unitData.Mount = true;
        // turn off CharacterController collision & gravity
    }

    /// <summary>
    /// Called by your vehicle code when the player leaves the seat.
    /// Restores movement and gravity.
    /// </summary>
    public void OnDismount()
    {
        UpdateActionAnimation(unitData.IdleAnimation);
        cc.enabled = true;
        unitData.Mount = false;
        // Reset movement ability and gravity
    }
}
