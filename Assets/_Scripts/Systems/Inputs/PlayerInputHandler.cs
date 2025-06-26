using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Reads input values from the built-in PlayerInput / Input Actions asset.
/// Supports any number of action maps—just switch via PlayerInput.SwitchCurrentActionMap.
/// </summary>
public class PlayerInputHandler : InputProviderBase
{
    [Header("Core")]
    [Tooltip("The PlayerInput component that holds your Input Actions asset")]
    [SerializeField] private PlayerInput playerInput;

    [Header("On-Foot Actions")]
    [Tooltip("Drag in the Move action from your 'Player' map")]
    [SerializeField] private InputActionReference moveAction;
    [Tooltip("Drag in the Interact action from your 'Player' map")]
    [SerializeField] private InputActionReference interactAction;

    [Header("Vehicle Actions")]
    [Tooltip("Drag in the Steer action from your 'Vehicle' map")]
    [SerializeField] private InputActionReference steerAction;

    // --------------------------------------------------
    // Runtime state
    // --------------------------------------------------
    Vector2 _moveInput;
    bool    _interactFired;
    float   _steerInput;

    // --------------------------------------------------
    // Overrides from InputProviderBase
    // --------------------------------------------------
    public override Vector2 MoveInput      => _moveInput;
    public override bool    InteractFired  => _interactFired;
    public override float   SteerInput     => _steerInput;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // --------------------------------------------------
    // Hook up callbacks
    // --------------------------------------------------
    void OnEnable()
    {
        // Enable all actions (only the active map fires its callbacks)
        moveAction.action.Enable();
        interactAction.action.Enable();
        steerAction.action.Enable();

        // On-Foot
        moveAction.action.performed     += ctx => _moveInput       = ctx.ReadValue<Vector2>();
        moveAction.action.canceled      += ctx => _moveInput       = Vector2.zero;
        interactAction.action.performed += ctx => _interactFired   = true;
        interactAction.action.canceled  += ctx => _interactFired   = false;

        // Vehicle
        steerAction.action.performed    += ctx => _steerInput      = ctx.ReadValue<float>();
        steerAction.action.canceled     += ctx => _steerInput      = 0f;
    }

    void OnDisable()
    {
        // Disable all at once (unsubscribing lambdas inline is tricky)
        moveAction.action.Disable();
        interactAction.action.Disable();
        steerAction.action.Disable();
    }

    /// <summary>
    /// Switch the active map.
    /// </summary>
    public void SwitchActionMap(string mapName) => playerInput.SwitchCurrentActionMap(mapName);
}
