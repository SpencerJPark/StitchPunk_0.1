using Unity.Mathematics;
using Unity.Physics;

public static class MovementCollisionUtils
{
    // probeSkin is a small inset so we do not interpenetrate the surface.
    // minGroundNormalY: e.g. 0.6f, anything with normal.y >= 0.6 counts as floor/ramp and never blocks.
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
            return desiredMove;
        }

        float3 hitNormal = hit.SurfaceNormal;

        // Vertical movement is handled by the gravity system.
        if (hitNormal.y >= minGroundNormalY)
        {
            return desiredMove;
        }

        float hitDistance = math.distance(origin, hit.Position);
        float travelToContact = math.max(0f, hitDistance - probeSkin);

        float3 moveToContact = moveDirection * math.min(travelToContact, moveLength);

        float remainingDistance = moveLength - travelToContact;
        if (remainingDistance <= 1e-4f)
        {
            return moveToContact;
        }

        // Slide: project the desired move onto the wall's tangent plane.
        float3 projected = desiredMove - math.dot(desiredMove, hitNormal) * hitNormal;
        float projectedLenSq = math.lengthsq(projected);
        if (projectedLenSq < 1e-6f)
        {
            return moveToContact;
        }

        float projectedLen = math.sqrt(projectedLenSq);
        float3 slideDirection = projected / projectedLen;

        float3 slideMoveDesired = slideDirection * remainingDistance;

        // Cast the slide too so it cannot run into a second obstacle.
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
            return moveToContact + slideMoveDesired;
        }
    }
}
