using UnityEngine;

public abstract class CharacterControllerBase : MonoBehaviour, IUpdateObserver
{
    [Header("Dependencies")]
    [SerializeField] protected InputProviderBase input;
    [SerializeField] protected CharacterController cc;
    [SerializeField] protected RiveAnimator animator;
    [SerializeField] private FacingDirectionBase facingController;


    [Header("State Data")]
    [SerializeField] protected UnitStateData currentState;


    [Header("UnitBaseData")]
       protected IUnitData unitData;

    // ───────────────────────────────────────────────────────────────
    // Override this in subclasses to supply the correct data instance
    protected abstract IUnitData CreateUnitData();
    // ───────────────────────────────────────────────────────────────




    private bool isGrounded;
    private float fallSpeed;
    protected Vector3 moveDirection;
    protected bool isMoving;
    private bool mount = false;

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

        fallSpeed = 0f;
    }

    void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate()
    {
        if (!mount)
        {
            HandleMovement();
        }
        
        HandleFacing();

        HandleAction();
    }

    public virtual void ApplyState(UnitStateData state)
    {
        currentState = state;
    }

    protected virtual void HandleMovement()
    {
        moveDirection = new Vector3(input.MoveInput.x, 0f, input.MoveInput.y);
        isMoving = moveDirection.sqrMagnitude > 0.01f;

        float speed = unitData != null ? unitData.MoveSpeed : 3f;
        Vector3 motion = moveDirection * speed;

        // apply gravity if needed
        if (unitData != null &&
            (unitData.Movement == MovementType.Grounded ||
             unitData.Movement == MovementType.Floating))
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
            unitData.GroundCheckDistance, unitData.GroundLayer);

        if (!isGrounded)
        {
            fallSpeed += unitData.Gravity * Time.deltaTime;
            fallSpeed = Mathf.Clamp(fallSpeed, 0f, unitData.MaxFallSpeed);
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

        Actions animState = moving ? currentState.WalkAnimation : currentState.IdleAnimation;
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
        mount = true;
        // turn off CharacterController collision & gravity
    }

    /// <summary>
    /// Called by your vehicle code when the player leaves the seat.
    /// Restores movement and gravity.
    /// </summary>
    public void OnDismount()
    {
        UpdateActionAnimation(Actions.Idle);
        cc.enabled = true;
        mount = false;
        // Reset movement ability and gravity
    }
}
