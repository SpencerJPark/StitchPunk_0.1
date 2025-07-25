using UnityEngine;

public abstract class CharacterControllerBase : MonoBehaviour, IUpdateObserver
{
    [Header("Controller Dependencies")]
    [SerializeField] protected IInputProvider input;
    [SerializeField] protected CharacterController cc;


    [Header("View Dependencies")]
    [SerializeField] protected RiveAnimator riveAnimator;


    [Header("Model Dependencies")]
    [SerializeField] protected UnitData unitData;

    [Header("Optional")]
     [SerializeField] protected UnitStateData currentState;



    protected UnitModel unitModel;

    // ───────────────────────────────────────────────────────────────
    // Override this in subclasses to supply the correct data instance
    protected abstract UnitModel CreateUnitModel();
    // ───────────────────────────────────────────────────────────────


    // Will swith to dependecy injection
    protected virtual void Awake()
    {
        // Build your data model here
        unitModel = CreateUnitModel();
        if (unitModel == null)
            Debug.LogError($"{name} failed to CreateUnitModel()");
    } 
    
    void OnEnable()
    {
        UpdateManager.RegisterObserver(this);

        unitModel.SetFallSpeed(0f);
    }

    void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate()
    {
        if (unitModel.Mount)
            return;
        
        HandleMovement();
        
        if (unitModel.IsMoving)
        {
            UpdateFacing(unitModel.MovementVector);
        }

        HandleAction();
    }

    public virtual void ApplyState(UnitStateData state)
    {
        currentState = state;
    }

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

        UpdateMovementAnimation();
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

    public void UpdateFacing(Vector3 moveVect)
    {
        if (riveAnimator == null)
            return;

        Direction newDirection = DirectionUtil.GetWorldRelativeDirection(moveVect, unitModel.DirectionType);

        if (newDirection != unitModel.CurrentDirection)
        {
            unitModel.SetDirection(newDirection);
        }
        

        riveAnimator.SetEnum("Direction", unitModel.CurrentDirection.ToString());
    }



    protected virtual void HandleAction()
    {
        // UpdateActionAnimation() or FireTriggerAnimation()
    }

    // Handle Animation

    protected virtual void UpdateMovementAnimation()
    {
        Actions animState = unitModel.IsMoving ? unitModel.WalkAnimation : unitModel.IdleAnimation;
        riveAnimator.SetEnum("Actions", animState.ToString());
    }

    protected virtual void UpdateActionAnimation(Actions action)
    {
        riveAnimator.SetEnum("Actions", action.ToString());
    }

    protected virtual void FireTriggerAnimation(string trigger)
    {
        riveAnimator.Trigger(trigger);
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
        unitModel.SetMount(true);
        // turn off CharacterController collision & gravity
    }

    /// <summary>
    /// Called by your vehicle code when the player leaves the seat.
    /// Restores movement and gravity.
    /// </summary>
    public void OnDismount()
    {
        UpdateActionAnimation(unitModel.IdleAnimation);
        cc.enabled = true;
        unitModel.SetMount(false);
        // Reset movement ability and gravity
    }
}
