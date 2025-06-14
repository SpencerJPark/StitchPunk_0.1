using UnityEngine;
using System;

[RequireComponent(typeof(Rigidbody))]
public class VehicleControllerBase : MonoBehaviour, IFixedUpdateObserver
{
    [Header("Dependencies")]
    [SerializeField] protected IInputProvider input;
    [SerializeField] protected Rigidbody rb;
    //[SerializeField] protected RiveAnimator animator;
    //[SerializeField] private MonoBehaviour HorseFacingComponent;
    //[SerializeField] private MonoBehaviour DriverFacingComponent;
    //private IFacingController facingController;


    [Header("Vehicle Profile")]
    [SerializeField] private float moveSpeed = 5f; // Forward movement speed
    [SerializeField] private float turnSpeed = 50f; // Turning speed


    [Header("Starting State")]
    [SerializeField] private bool active = true;
    private float forward = 0;
    private float steer = 0; 

     [Header("Wheels")]
    [SerializeField]
    private WheelData[] wheels;   // THIS is what you’ll see in the Inspector

    /// <summary>
    /// Read-only access to wheel data for visuals.
    /// </summary>
    public WheelData[] Wheels => wheels;

    [Serializable]
    public struct WheelData
    {
        public Transform mesh;        // visible
        public bool      isLeftSide;  // visible
        public float     radius;      // visible

        [HideInInspector]
        public float targetRPM;       // hidden
    }

    void OnEnable()  => FixedUpdateManager.RegisterObserver(this);
    void OnDisable() => FixedUpdateManager.UnregisterObserver(this);


    public void ObservedFixedUpdate()
    {
        if (active)
        {
            UpdateInputs();
            HandleMovement();
            ComputeWheelSpinTargets();

            // Needed to make sure billboard aline correctly
            UpdateHorsePosition(); // Horse
            UpdateDriverPosition(); // Driver

            // Changes which way they are facing animation wise
            HandleFacing(); // Horse
            HandleFacing(); // Driver
        }

        UpdateHorseAnimation();
    }

// Public Methods
    // Called when driver enters
    public virtual void ActivateVehicle()
    {
        active = true;
    }

    // Called when driver exits
    public virtual void DeactivateVehicle()
    {
        active = false;
    }

// Private Methods
    private void UpdateInputs()
    {
        forward = input.MoveInput.y;
        steer = input.MoveInput.x;
    }

    // Updates the movement of the vehicle
    protected virtual void HandleMovement()
    {
        // Forward movement (only when pressing forward input)
        if (forward > 0)
        {
            Vector3 forwardMovement = transform.forward * forward * moveSpeed * Time.fixedDeltaTime;
            rb.MovePosition(rb.position + forwardMovement);
        }

        // Turning (independent of forward input)
        if (Mathf.Abs(steer) > 0)
        {
            float turnAmount = steer * turnSpeed * Time.fixedDeltaTime;
            Quaternion turnRotation = Quaternion.Euler(0, turnAmount, 0);
            rb.MoveRotation(rb.rotation * turnRotation);
        }
    }
    
    private void ComputeWheelSpinTargets()
    {
        bool usingForward = Mathf.Abs(forward) > 0.01f;
        bool usingSteer   = !usingForward && Mathf.Abs(steer) > 0.01f;

        for (int i = 0; i < wheels.Length; i++)
        {
            float rpm = 0f;
            var  w   = wheels[i];  // local copy for easy reading

            if (usingForward)
            {
                // convert linear speed → RPM
                float linearSpeed = forward * moveSpeed; // m/s
                rpm = (linearSpeed / (2f * Mathf.PI * w.radius)) * 60f;
            }
            else if (usingSteer)
            {
                float spinSign = w.isLeftSide ? +1f : -1f;
                rpm = spinSign * turnSpeed; // you decide what “in-place RPM” is
            }

            wheels[i].targetRPM = rpm;  // write back into the array
        }
    }


// 2d Visual Updates
    // Moves the horse plane depending on the direction
    protected virtual void UpdateHorsePosition()
    {
        Debug.Log("Updated Driver Location");
    }

    // Moves the Driver plane depending on the direction
    protected virtual void UpdateDriverPosition()
    {
        Debug.Log("Updated Driver Location");
    }

    // Changes the Horse/driver plane Facing depending on the direction
    protected virtual void HandleFacing()
    {
        // if (!isMoving || facingController == null)
        //     return;

        // facingController.UpdateFacing(moveDirection);
        Debug.Log("Updated Facing");
    }

    protected virtual void UpdateHorseAnimation()
    {
        Debug.Log("Updated Horse");
    }
}