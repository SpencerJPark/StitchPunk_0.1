using UnityEngine;
using Data;
using UtilityAI;


[RequireComponent(typeof(UnitMotorBase))]
public class UnitController : MonoBehaviour, IUpdateObserver
{
    [Header("Input Source")]
    [Tooltip("If true, reads from PlayerInputHandler.Instance (must implement IInputProvider).")]
    [SerializeField] private bool player = false;

    [Tooltip("Optional: explicit on-foot input provider for NPCs (e.g., PathInputProvider, AIBrainInputProvider).")]
    [SerializeField] private InputProviderBase inputBase;   // concrete base

    private IInputProvider input;                           // interface the controller uses


    [Header("Motor (plug in CCMotor or AgentMotor)")]
    [SerializeField] private UnitMotorBase motor;


    [Header("View Dependencies")]
    [SerializeField] private RiveAnimator riveAnimator;


    [Header("Model Dependencies")]
    [SerializeField] private UnitModel unitModel;
    [SerializeField] private UnitData  unitData;

    [Header("Optional")]
    [SerializeField] private UnitStateData currentState;


    // -------------------- LIFECYCLE --------------------
    protected virtual void Awake()
    {
        ResolveInput();
        ResolveMotor();

        if (unitModel == null) Debug.LogError($"{name}: UnitModel not assigned.");
        if (unitData  == null) Debug.LogError($"{name}: UnitData not assigned.");

        unitModel?.Initialize(unitData);
        motor?.Initialize(unitModel.MovementData);
    }

    private void ResolveMotor()
    {
        if (motor == null)
        {
            motor = GetComponent<UnitMotorBase>();
            if (motor == null)
                Debug.LogError($"{name}: No UnitMotorBase found.");
        }
    }

    private void ResolveInput()
    {
        // Priority: Player → assigned InputBase → local component
        if (player)
        {
            if (PlayerInputHandler.Instance == null)
            {
                Debug.LogError($"{name}: PlayerInputHandler.Instance is null.");
                input = null;
                return;
            }

            input = PlayerInputHandler.Instance; // must implement IInputProvider
            inputBase = null; // player path uses handler, ignore serialized base
        }
        else
        {
            if (inputBase == null)
                inputBase = GetComponent<InputProviderBase>(); // auto-pick sibling if present

            if (inputBase == null)
            {
                Debug.LogError($"{name}: No InputProviderBase assigned/found for NPC.");
                input = null;
                return;
            }

            // InputBase implements the interface contract
            input = (IInputProvider)inputBase;
        }

        if (input == null)
            Debug.LogError($"{name}: Failed to resolve IInputProvider.");
    }

    /// <summary>
    /// Hot-swap the input provider at runtime (e.g., enter/exit vehicle).
    /// Pass an InputProviderBase that implements IInputProvider.
    /// </summary>
    public void SetInputProvider(InputProviderBase newProvider, bool isPlayer = false)
    {
        player = isPlayer;
        inputBase = newProvider;
        input = null; // force rebind
        ResolveInput();
    }

    void OnEnable()  => UpdateManager.RegisterObserver(this);
    void OnDisable() => UpdateManager.UnregisterObserver(this);


    // -------------------- UPDATE LOOP --------------------
    public void ObservedUpdate()
    {
        if (unitModel == null || motor == null || input == null) return;
        if (unitModel.Mount) return;

        HandleMovement();
        HandleAction();
        HandleAnimation();
    }

    protected virtual void HandleMovement()
    {
        // Input is 2D (x,z) from IInputProvider.MoveInput
        Vector2 move2D = input.MoveInput;
        //Debug.Log($"move2D = {move2D}");
        unitModel.SetMoving(move2D.sqrMagnitude > 0.01f);

        motor.SetMoveDirection(move2D);   // your motor interprets this
        motor.Tick(Time.deltaTime);
    }

    // Action Updates (extend as needed)
    protected virtual void HandleAction()
    {
        // Example edge-trigger reads:
        // if (input.ActionFired)   { ... }
        // if (input.InteractFired) { ... }
    }

    // -------------------- ANIMATION --------------------
    private void HandleAnimation()
    {
        if (riveAnimator == null || unitModel == null) return;

        UpdateMovementAnimation();

        if (unitModel.IsMoving)
            UpdateFacing(motor.MovementVector);
    }

    public void UpdateFacing(Vector3 moveVect)
    {
        if (riveAnimator == null || unitModel == null) return;

        Direction newDirection = DirectionUtil.GetWorldRelativeDirection(moveVect, unitModel.DirectionType);

        if (newDirection != unitModel.CurrentDirection)
            unitModel.SetDirection(newDirection);

        riveAnimator.SetEnum("Direction", unitModel.CurrentDirection.ToString());
    }

    protected virtual void UpdateMovementAnimation()
    {
        ActionType animState = unitModel.IsMoving ? unitModel.WalkAnimation : unitModel.IdleAnimation;
        riveAnimator.SetEnum("Actions", animState.ToString());
    }

    // -------------------- STATE/APIs --------------------
    public virtual void ApplyState(UnitStateData state) => currentState = state;

    public virtual void UpdateActionAnimation(ActionType action)
        => riveAnimator?.SetEnum("Actions", action.ToString());

    protected virtual void FireTriggerAnimation(TriggerType trigger)
        => riveAnimator?.Trigger(trigger.ToString());

    // -------------------- MOUNT / DISMOUNT --------------------
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
        motor.Go();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (motor == null) motor = GetComponent<UnitMotorBase>();
        if (!player && inputBase == null) inputBase = GetComponent<InputProviderBase>();
    }
#endif
}
