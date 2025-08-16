using UnityEngine;
using Data;

public abstract class UnitControllerBase : MonoBehaviour, IUpdateObserver
{
    [Header("Controller Dependencies")]
    [SerializeField] public IInputProvider input;


    [Header("Motor (plug in CCMotor or AgentMotor)")]
    [SerializeField] UnitMotorBase motor;


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

        // Setup Motor using unit data
        motor.Build(unitModel.MovementData);
    } 
    
    void OnEnable() => UpdateManager.RegisterObserver(this);
    void OnDisable() => UpdateManager.UnregisterObserver(this);


    public void ObservedUpdate()
    {
        if (unitModel.Mount) return;

        HandleMovement();
        HandleAction();
        HandleAnimation();
    }

    protected virtual void HandleMovement()
    {
        unitModel.SetMoving(input.MoveInput.sqrMagnitude > 0.01f); // Adjust for ai

        motor.SetMoveDirection(input.MoveInput); // add logic for handling different move insturctions

        motor.Tick(Time.deltaTime);
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
            UpdateFacing(motor.MovementVector);
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
        unitModel.SetMount(true);
        motor.Halt();
    }

    public void OnDismount()
    {
        UpdateActionAnimation(unitModel.IdleAnimation);
        unitModel.SetMount(false);
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
