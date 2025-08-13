public interface IUnitMotor
{
    Vector3 Velocity { get; }       // world-space current velocity
    Vector3 Desired { get; }        // optional: where it wants to go
    bool    IsGrounded { get; }     // CC motor fills this; Agent motor can return true
    void    Tick(float dt);         // advance movement this frame
    void    Halt();                 // stop now
}
