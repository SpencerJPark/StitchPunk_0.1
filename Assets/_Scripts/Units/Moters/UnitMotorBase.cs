public abstract class UnitMotorBase : MonoBehaviour, IUnitMotor
{
    public abstract Vector3 Velocity { get; }
    public virtual  Vector3 Desired  => Velocity;
    public virtual  bool IsGrounded  => true;
    public abstract void Tick(float dt);
    public abstract void Halt();
}
