using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class VehicleController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Your existing PlayerInputHandler component")]
    [SerializeField] private PlayerInputHandler inputHandler;
    [Tooltip("All 4 wheel colliders: FL, FR, RL, RR")]
    [SerializeField] private WheelCollider[] wheelColliders = new WheelCollider[4];

    [Header("Tuning")]
    [SerializeField] private float maxMotorTorque = 1500f;
    [SerializeField] private float maxSteerAngle   = 30f;
    [SerializeField] private float brakeTorque     = 3000f;

    private Rigidbody rb;

    void Awake()
    {
        // auto-grab if you forgot to hook it up in the Inspector
        if (inputHandler == null)
            inputHandler = GetComponent<PlayerInputHandler>();

        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        ApplySteering();
        ApplyMotor();
    }

    private void ApplySteering()
    {
        // assume inputHandler.Steer returns a float in [-1,1]
        float steer = maxSteerAngle * inputHandler.SteerInput;
        // front-left = [0], front-right = [1]
        wheelColliders[0].steerAngle = steer;
        wheelColliders[1].steerAngle = steer;
    }

    private void ApplyMotor()
    {
        // inputHandler.ThrottleInput in [0,1], inputHandler.BrakePressed bool
        float motor = maxMotorTorque * inputHandler.ThrottleInput;
        // rear-axle drive: RL [2], RR [3]
        wheelColliders[2].motorTorque = motor;
        wheelColliders[3].motorTorque = motor;

        if (inputHandler.BrakePressed)
        {
            for (int i = 0; i < 4; i++)
                wheelColliders[i].brakeTorque = brakeTorque;
        }
        else
        {
            for (int i = 0; i < 4; i++)
                wheelColliders[i].brakeTorque = 0f;
        }
    }
}
