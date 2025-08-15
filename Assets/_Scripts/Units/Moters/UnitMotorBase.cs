using UnityEngine;

public abstract class UnitMotorBase : MonoBehaviour, IUnitMotor
{
    public virtual bool IsGrounded => true;
    public virtual Vector3 MovementVector { get; protected set; } = Vector3.zero;

    public virtual void Build(UnitMovementData movementData) { /* optional */ }
    public virtual void SetMoveDirection(Vector3 worldDirectionXZ) { /* optional */ }
    public virtual void SetDestination(Vector3 worldPosition) { /* optional */ }

    public abstract void Tick(float dt);
    public abstract void Halt();
}


