using UnityEngine;

public interface IUnitMotor
{
    bool IsGrounded { get; }
    /// Normalized planar direction (y=0) for anim; zero if idle
    Vector3 MovementVector { get; }

    // Directional intent (world XZ). Used by CCMotor/Rigidbody motors.
    void SetMoveDirection(Vector3 worldDirectionXZ);

    // Goal intent (world point). Used by NavMesh motors.
    void SetDestination(Vector3 worldPosition);

    // Advance motor (or grab Time.deltaTime internally if you prefer)
    void Tick(float dt);
    void Halt();
}

