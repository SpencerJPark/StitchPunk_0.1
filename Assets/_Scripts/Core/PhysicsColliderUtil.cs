using Unity.Mathematics;
using Unity.Physics;

public static class PhysicsColliderUtil
{
    /// <summary>
    /// Sweeps the given PhysicsCollider from start to end using ColliderCast.
    /// This is the only place we touch Collider* (unsafe).
    /// </summary>
    public static unsafe bool ColliderCast(
        in PhysicsCollider physicsCollider,
        in CollisionWorld world,
        float3 start,
        float3 end,
        quaternion orientation,
        out ColliderCastHit hit)
    {
        hit = default;

        if (!physicsCollider.IsValid)
            return false;

        var input = new ColliderCastInput
        {
            Collider    = physicsCollider.ColliderPtr, // pointer into the collider blob
            Orientation = orientation,
            Start       = start,
            End         = end
        };

        // NOTE: We are NOT touching any Filter here.
        // The collider's own CollisionFilter (set when it was created) will be used.

        return world.CastCollider(input, out hit);
    }
}