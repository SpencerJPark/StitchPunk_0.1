using UnityEngine;
using Data;

public abstract class UnitController : MonoBehaviour, IUpdateObserver
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


// Will swith to Initialize
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
        HandleAction();
        HandleAnimation();
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


// Action Updates
    protected virtual void HandleAction()
    {
        // uses action information for timing, action type, and if it is a trigger or other action
    }


// Animation Updates
    private void HandleAnimation()
    {
        // Handle Action animation first if needed

        UpdateMovementAnimation();

        if (unitModel.IsMoving)
        {
            UpdateFacing(unitModel.MovementVector);
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

    protected virtual void UpdateMovementAnimation()
    {
        ActionType animState = unitModel.IsMoving ? unitModel.WalkAnimation : unitModel.IdleAnimation;
        riveAnimator.SetEnum("Actions", animState.ToString());
    }

    public virtual void ApplyState(UnitStateData state)
    {
        currentState = state;
    }

    public virtual void UpdateActionAnimation(ActionType action)
    {
        // Add a bool and Timer
        riveAnimator.SetEnum("Actions", action.ToString());
    }

    protected virtual void FireTriggerAnimation(TriggerType trigger)
    {
        // Add a bool and Timer
        riveAnimator.Trigger(trigger.ToString());
    }

    // Paticle System


// Object Interactions
    public void OnMount()
    {
        UpdateActionAnimation(ActionType.Sit);
        cc.enabled = false;
        unitModel.SetMount(true);
        // turn off CharacterController collision & gravity
    }

    public void OnDismount()
    {
        UpdateActionAnimation(unitModel.IdleAnimation);
        cc.enabled = true;
        unitModel.SetMount(false);
        // Reset movement ability and gravity
    }


// Customization
    protected void ApplyCustomization()
    {
        //if (DesignData != null)
        {
            // Set data in model
        }

        // Customization for npcs will be based on assigned jobs
    }
}
