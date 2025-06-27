using UnityEngine;
using System;

[RequireComponent(typeof(Rigidbody))]
public class VehicleControllerBase : MonoBehaviour, IFixedUpdateObserver
{
    [Header("Seating")]
    [Tooltip("Empty transform marking where the driver should sit")]
    [SerializeField] private Transform driverSeatAnchor;
    [Tooltip("2D offset profile for driver quad (if you still draw them)")]
    [SerializeField] private FacingOffsetProfile driverOffsetProfile;
    [Tooltip("Facing controller on the driver GameObject (if you still use 2D facing)")]
    [SerializeField] private FacingDirectionBase driverFacingController;

    [Header("Dependencies")]
    [Tooltip("Will be set dynamically when someone enters")]
    protected InputProviderBase input;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private RiveAnimator animator;
    [SerializeField] private FacingDirectionBase horseFacingController;

    [Header("Vehicle Profile")]
    [SerializeField] private VehicleProfiles vehicleProfiles;


    [Header("2D Visuals")]
    [SerializeField] private Transform            horseQuad;
    [SerializeField] private FacingOffsetProfile  horseOffsetProfile;
    [SerializeField] private FacingDirectionBase  horseFacing;


    [Header("Wheels")]
    [SerializeField] private WheelData[] wheels;
    public WheelData[] Wheels => wheels;

    [Serializable]
    public struct WheelData
    {
        public Transform mesh;
        public bool      isLeftSide;
        public float     radius;
        [HideInInspector]
        public float     targetRPM;
    }

    // runtime state
    private Transform           currentDriver;
    private bool                active = false;
    private Vector2             Input;
    private float               currentSpeed;
    private Vector3             currentDirection = Vector3.forward;
    private float               turnVelocity;
    private FacingDirectionBase driverFacingDynamic;
    private bool _exitTriggeredThisPress = false;

    void OnEnable()  => FixedUpdateManager.RegisterObserver(this);
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
        Input = input?.SteerInput ?? Vector2.zero;

        // drive physics
        HandleMovement();
        ComputeWheelSpinTargets();

        // 2D visuals for horse + driver
        Handle2D();
    }

    void Start()
    {
        // 1) Lower the COM so the pivot is closer to the ground:
        rb.centerOfMass = new Vector3(0, -1.0f, 0);  // tweak Y until it feels stable
        
        // 2) Increase angular drag so flips damp out instantly:
        rb.angularDamping = 10f;                        // higher = more resistance to spinning
        
        // (Optional) Manually boost inertia on X/Z so it “weighs” more against tipping:
        rb.inertiaTensor = new Vector3(1000f, 2f, 1000f);
        rb.inertiaTensorRotation = Quaternion.identity;
    }


    private void ReadInput()
    {
        Input = input.SteerInput;
    }

    protected virtual void HandleMovement()
    {
        Vector3 desiredVelocity = ComputeDesiredVelocity();
        float   targetSpeed     = desiredVelocity.magnitude;
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
        float targetAngle  = Mathf.Atan2(targetDirection.x,  targetDirection.z)  * Mathf.Rad2Deg;

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
        if (driverFacingDynamic != null)
        {
            driverFacingDynamic.UpdateFacing(currentDirection);

            // if you still use an offset profile:
            var dir = driverFacingDynamic.CurrentDirection;
            driverSeatAnchor.localPosition = driverOffsetProfile.GetOffset(dir);
        }

        // optionally horse animation
        UpdateHorseAnimation();
    }

    protected virtual void UpdateHorsePosition()
    {
        var dir = horseFacing.CurrentDirection;
        horseQuad.localPosition = horseOffsetProfile.GetOffset(dir);
    }

    // protected virtual void UpdateDriverPosition()
    // {
    //     var dir = driverFacing.CurrentDirection;
    //     driverQuad.localPosition = driverOffsetProfile.GetOffset(dir);
    // }

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
    public void EnableVehicle(Transform driver, InputProviderBase driverInput)
    {
        // 1) Vehicle becomes kinematic/dynamic as desired
        rb.isKinematic = false;
        active = true;

        // 2) Capture which input to read
        input = driverInput;

        // 3) Parent the driver to the seat anchor
        currentDriver = driver;
        driver.SetParent(driverSeatAnchor, worldPositionStays: false);
        driver.localPosition = Vector3.zero;
        driver.localRotation = Quaternion.identity;

        // 4) Hook up facing if you need it
        driverFacingDynamic = driverFacingController
            ? driver.GetComponent<FacingDirectionBase>()
            : null;
    }

    /// <summary>
    /// Called to drop the driver back out.
    /// </summary>
    public void DisableVehicle()
    {
        // 0) Kill drive state immediately
        active = false;
        currentSpeed  = 0f;
        turnVelocity  = 0f;
        Input         = Vector2.zero;

        // 1) Stop all wheel spins
        for (int i = 0; i < wheels.Length; i++)
            wheels[i].targetRPM = 0f;

        // 2) Reset animator to idle
        if (animator != null && vehicleProfiles != null)
            animator.SetEnum("Actions", Actions.Idle.ToString());

        // 3) Unparent the driver
        if (currentDriver != null)
        {
            currentDriver.SetParent(null, worldPositionStays: true);
            currentDriver = null;
        }

        // 4) Stop reading input & facing
        input = null;
        driverFacingDynamic = null;

        // 5) Make vehicle immovable by NPC bumps
        rb.isKinematic = true;
    }
}
