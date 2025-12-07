using Unity.Mathematics;
using Unity.Physics;

public static class MovementCollisionUtils
{
    /// <summary>
    /// Given a desired movement vector for this frame, returns an adjusted movement
    /// that respects collisions and slides along walls. Ramps / ground are ignored
    /// as blockers based on minGroundNormalY.
    /// 
    /// desiredMove: world-space displacement you *would* apply this frame.
    /// probeSkin:   small inset so we do not interpenetrate the surface.
    /// minGroundNormalY: e.g. 0.6f → anything with normal.y >= 0.6 is treated as floor/ramp.
    /// </summary>
    public static float3 ComputeAdjustedMovement(
        ref CollisionWorld collisionWorld,
        float3 origin,
        float3 desiredMove,
        float probeSkin,
        CollisionFilter collisionFilter,
        float minGroundNormalY)
    {
        float moveLengthSq = math.lengthsq(desiredMove);
        if (moveLengthSq < 1e-6f)
        {
            return float3.zero;
        }

        float moveLength = math.sqrt(moveLengthSq);
        float3 moveDirection = desiredMove / moveLength;

        float castDistance = moveLength + probeSkin;
        float3 castEnd = origin + moveDirection * castDistance;

        RaycastInput raycastInput = new RaycastInput
        {
            Start  = origin,
            End    = castEnd,
            Filter = collisionFilter
        };

        Unity.Physics.RaycastHit hit;
        if (!collisionWorld.CastRay(raycastInput, out hit))
        {
            // No hit → full movement allowed.
            return desiredMove;
        }

        float3 hitNormal = hit.SurfaceNormal;

        // If it is ground / ramp, treat as non-blocking (vertical movement handled by gravity system).
        if (hitNormal.y >= minGroundNormalY)
        {
            return desiredMove;
        }

        // --- Blocked by a wall-like surface ---

        float hitDistance = math.distance(origin, hit.Position);
        float travelToContact = math.max(0f, hitDistance - probeSkin);

        // Movement up to the wall
        float3 moveToContact = moveDirection * math.min(travelToContact, moveLength);

        float remainingDistance = moveLength - travelToContact;
        if (remainingDistance <= 1e-4f)
        {
            // We are basically right at the wall, no more movement this frame.
            return moveToContact;
        }

        // Project the desired move on the plane tangent to the wall → sliding.
        float3 projected = desiredMove - math.dot(desiredMove, hitNormal) * hitNormal;
        float projectedLenSq = math.lengthsq(projected);
        if (projectedLenSq < 1e-6f)
        {
            // Movement is basically straight into the wall, nothing to slide with.
            return moveToContact;
        }

        float projectedLen = math.sqrt(projectedLenSq);
        float3 slideDirection = projected / projectedLen;

        // We want to use whatever "time" is left for sliding.
        float3 slideMoveDesired = slideDirection * remainingDistance;

        // Optional extra safety: cast the slide to avoid sliding into another obstacle.
        float3 slideStart = hit.Position;
        float3 slideEnd = slideStart + slideDirection * (remainingDistance + probeSkin);

        RaycastInput slideInput = new RaycastInput
        {
            Start  = slideStart,
            End    = slideEnd,
            Filter = collisionFilter
        };

        Unity.Physics.RaycastHit slideHit;
        if (collisionWorld.CastRay(slideInput, out slideHit))
        {
            float slideHitDistance = math.distance(slideStart, slideHit.Position);
            float allowedSlideDistance = math.max(0f, slideHitDistance - probeSkin);
            allowedSlideDistance = math.min(allowedSlideDistance, remainingDistance);
            float3 slideMove = slideDirection * allowedSlideDistance;
            return moveToContact + slideMove;
        }
        else
        {
            // No slide hit → we can slide fully.
            return moveToContact + slideMoveDesired;
        }
    }
}
