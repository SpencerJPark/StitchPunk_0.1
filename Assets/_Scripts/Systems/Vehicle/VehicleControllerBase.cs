using UnityEngine;
using System;

[RequireComponent(typeof(Rigidbody))]
public class VehicleControllerBase : MonoBehaviour, IFixedUpdateObserver
{
    [Header("Dependencies")]
    [Tooltip("Will be set dynamically when someone enters")]
    protected InputProviderBase input;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform driverSeatAnchor;


    [Header("Driver")]
    public GameObject driverObject = null;
    private CharacterControllerBase driverCharacterController;
    private FacingDirectionBase driverFacingController;
    private Transform driverQuad;
    [SerializeField] private FacingOffsetProfile driverOffsetProfile;


    [Header("Horse")]
    [SerializeField] private RiveAnimator animator;
    [SerializeField] private FacingDirectionBase horseFacingController;
    [SerializeField] private Transform horseQuad;
    [SerializeField] private FacingOffsetProfile horseOffsetProfile;


    [Header("Vehicle Profile")]
    [SerializeField] private VehicleProfiles vehicleProfiles;
    private Vector2 Input;


    [Header("Wheels")]
    [SerializeField] private WheelData[] wheels;
    public WheelData[] Wheels => wheels;
    [Serializable]
    public struct WheelData
    {
        public Transform mesh;
        public bool isLeftSide;
        public float radius;
        [HideInInspector]
        public float targetRPM;
    }


    // Runtime state
    private bool active = false;
    private float currentSpeed;
    private Vector3 currentDirection = Vector3.forward;
    private float turnVelocity;
    private bool _exitTriggeredThisPress = false;



    void OnEnable()
    {
        // 1) Lower the COM so the pivot is closer to the ground:
        rb.centerOfMass = new Vector3(0, -1.0f, 0);  // tweak Y until it feels stable

        // 2) Increase angular drag so flips damp out instantly:
        rb.angularDamping = 10f;                        // higher = more resistance to spinning

        // 3) Manually boost inertia on X/Z so it “weighs” more against tipping:
        rb.inertiaTensor = new Vector3(1000f, 2f, 1000f);
        rb.inertiaTensorRotation = Quaternion.identity;

        // 4) Registers for updates:
        FixedUpdateManager.RegisterObserver(this);

        // 5) Checks if driver game object is set, if so set ups hooks
        if (driverObject)
        {
            SetUpDriver(driverObject);
        }
    }
    void OnDisable() => FixedUpdateManager.UnregisterObserver(this);


    public void ObservedFixedUpdate()
    {
        if (!active)
            return;

        // --- EXIT HANDLING ---
        if (input.ExitVehicleFired)
        {
            if (!_exitTriggeredThisPress)
            {
                _exitTriggeredThisPress = true;
                PerformExit();
            }
            // skip all driving logic this frame
            return;
        }
        else
        {
            // reset once button is released
            _exitTriggeredThisPress = false;
        }

        // read whichever input provider we were given
        ReadInput();

        // drive physics
        HandleMovement();
        ComputeWheelSpinTargets();

        // 2D visuals for horse + driver
        Handle2D();
    }


    private void ReadInput() => Input = input.SteerInput;

    protected virtual void HandleMovement()
    {
        Vector3 desiredVelocity = ComputeDesiredVelocity();
        float targetSpeed = desiredVelocity.magnitude;
        Vector3 targetDirection = GetTargetDirection(desiredVelocity);

        UpdateSpeed(targetSpeed);
        UpdateDirection(targetDirection);
        ApplyMovement();
    }

    private Vector3 ComputeDesiredVelocity()
    {
        return new Vector3(Input.x, 0f, Input.y) * vehicleProfiles.moveSpeed;
    }

    private Vector3 GetTargetDirection(Vector3 desiredVelocity)
    {
        return desiredVelocity.sqrMagnitude > 0.0001f
             ? desiredVelocity.normalized
             : currentDirection;
    }

    private void UpdateSpeed(float targetSpeed)
    {
        float accel = (targetSpeed > currentSpeed)
            ? vehicleProfiles.forwardAcceleration
            : vehicleProfiles.forwardDeceleration;

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            accel * Time.fixedDeltaTime
        );
    }

    private void UpdateDirection(Vector3 targetDirection)
    {
        float currentAngle = Mathf.Atan2(currentDirection.x, currentDirection.z) * Mathf.Rad2Deg;
        float targetAngle = Mathf.Atan2(targetDirection.x, targetDirection.z) * Mathf.Rad2Deg;

        float smoothAngle = Mathf.SmoothDampAngle(
            currentAngle,
            targetAngle,
            ref turnVelocity,
            vehicleProfiles.turnSmoothTime,
            vehicleProfiles.turnSpeed,
            Time.fixedDeltaTime
        );

        currentDirection = (Quaternion.Euler(0f, smoothAngle, 0f) * Vector3.forward).normalized;
    }

    private void ApplyMovement()
    {
        Vector3 velocity = currentDirection * currentSpeed;
        rb.MovePosition(rb.position + velocity * Time.fixedDeltaTime);

        if (velocity.sqrMagnitude > 0.001f)
        {
            Quaternion look = Quaternion.LookRotation(currentDirection, Vector3.up);
            rb.MoveRotation(
                Quaternion.RotateTowards(
                    rb.rotation,
                    look,
                    vehicleProfiles.turnSpeed * Time.fixedDeltaTime
                )
            );
        }
    }

    private void ComputeWheelSpinTargets()
    {
        float speed = currentSpeed;
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            float rpm = speed > 0.01f
                ? (speed / (2f * Mathf.PI * w.radius)) * 60f
                : 0f;
            wheels[i].targetRPM = rpm;
        }
    }


    // 2D Visuals
    protected virtual void Handle2D()
    {
        // horse visuals...
        horseFacingController?.UpdateFacing(currentDirection);
        UpdateHorsePosition();

        // driver visuals, if we have one
        if (driverObject)
        {
            driverFacingController.UpdateFacing(currentDirection);
            UpdateDriverPosition();
        }

        // optionally horse animation
        UpdateHorseAnimation();
    }

    protected virtual void UpdateHorsePosition()
    {
        var dir = horseFacingController.CurrentDirection;
        horseQuad.localPosition = horseOffsetProfile.GetOffset(dir);
    }

    protected virtual void UpdateDriverPosition()
    {
        var dir = driverFacingController.CurrentDirection;
        driverQuad.localPosition = driverOffsetProfile.GetOffset(dir);
    }

    protected virtual void UpdateHorseAnimation()
    {
        if (animator == null || vehicleProfiles == null)
            return;

        // normalize your speed into [0,1]
        float t = currentSpeed / vehicleProfiles.moveSpeed;
        string action;

        if (t <= vehicleProfiles.walkThreshold)
            action = Actions.Idle.ToString();
        else if (t <= vehicleProfiles.runThreshold)
            action = Actions.Walk.ToString();
        else
            action = Actions.Run.ToString();

        animator.SetEnum("Actions", action);
    }


    // Public controls
    /// <summary>
    /// Handles unseating the driver, switching maps/cams back to the player
    /// </summary>
    private void PerformExit()
    {
        // 1) Disable vehicle drive
        DisableVehicle();

        // 2) Switch back to Player action map
        PlayerInputHandler.Instance.SwitchActionMap(ActionMaps.Player.ToString());

        // 3) Switch camera back
        CameraManager.Instance.SwitchCamera(CameraType.Player);
    }

    /// <summary>
    /// Called to make someone start driving.
    /// Pass in their transform and their IInputProvider.
    /// </summary>
    public void EnableVehicle(GameObject driver, InputProviderBase driverInput)
    {
        // 1) Vehicle becomes kinematic/dynamic as desired
        rb.isKinematic = false;
        active = true;

        // 2) Capture which input to read
        input = driverInput;

        // 3) Hook up driver Refrences
        SetUpDriver(driver);

        // 4) Parent Driver
        
    }

    /// <summary>
    /// Called to drop the driver back out.
    /// </summary>
    public void DisableVehicle()
    {
        // 0) Kill drive state immediately
        active = false;
        currentSpeed = 0f;
        turnVelocity = 0f;
        Input = Vector2.zero;

        // 1) Stop all wheel spins
        for (int i = 0; i < wheels.Length; i++)
            wheels[i].targetRPM = 0f;

        // 2) Reset horse animator to idle
        if (animator != null && vehicleProfiles != null)
            animator.SetEnum("Actions", Actions.Idle.ToString());

        // 3) Unparent the driver


        // 4) Stop reading input
        input = null;

        // 5) Moves Driver out of vehicle and sets its state to dimounted.

        // 6) Resets Driver variables
        UnsetDriver();

        // 7) Make vehicle immovable by NPC bumps
        rb.isKinematic = true;
    }

    // Sets up driver references
    private void SetUpDriver(GameObject driver)
    {
        // Check if driver is null, if so set driver as driverObject
        driverObject = driver;
        driverCharacterController = driverObject.GetComponent<CharacterControllerBase>();
        driverFacingController = driverObject.GetComponent<FacingDirectionBase>();

        driverQuad = null;
        
        foreach (var t in driverObject.transform.GetComponentsInChildren<Transform>(true))
        {
            if (t.CompareTag("CharacterQuad"))
            {
                driverQuad = t;
                break;
            }
        }

        if (driverQuad == null)
            Debug.LogWarning("No child tagged 'CharacterQuad' found under " + driverObject.name);
    }

    // releases driver references
    private void UnsetDriver()
    {
        driverObject = null;
        driverCharacterController = null;
        driverFacingController = null;
        driverQuad = null;
    }
}
