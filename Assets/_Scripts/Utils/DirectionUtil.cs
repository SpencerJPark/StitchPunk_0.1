using UnityEngine;

public static class DirectionUtil
{
    public static Direction GetAnimationDirection(Vector2 dir, AnimationDirections animationDirections)
    {
        switch (animationDirections)
        {
            case AnimationDirections.One:
                return Direction.South;
            case AnimationDirections.Two:
                return Get2Direction(dir);
            case AnimationDirections.Four:
                return Get4Direction(dir);
            case AnimationDirections.Six:
                return Get6Direction(dir);
            case AnimationDirections.Eight:
                return Get8Direction(dir);
            default:
                Debug.LogWarning("Unknown direction type. Defaulting to South.");
                return Direction.South;
        }
    }
    
    public static Direction Get2Direction(Vector2 dir)
    {
        return dir.x >= 0 ? Direction.SouthEast : Direction.SouthWest;
    }

    public static Direction Get4Direction(Vector2 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        angle = (angle + 360f + 45f) % 360f;

        if (angle < 90) return Direction.NorthEast;
        if (angle < 180) return Direction.NorthWest;
        if (angle < 270) return Direction.SouthWest;
        return Direction.SouthEast;
    }
    
    public static Direction Get6Direction(Vector2 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        angle = (angle + 360f + 22.5f) % 360f;
        
        if (angle < 90) return Direction.NorthEast;
        if (angle < 135) return Direction.North;
        if (angle < 180) return Direction.NorthWest;
        if (angle < 270) return Direction.SouthWest;
        if (angle < 315) return Direction.South;

        return Direction.SouthEast;
    }

    public static Direction Get8Direction(Vector2 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        angle = (angle + 360f + 22.5f) % 360f;

        if (angle < 45) return Direction.East;
        if (angle < 90) return Direction.NorthEast;
        if (angle < 135) return Direction.North;
        if (angle < 180) return Direction.NorthWest;
        if (angle < 225) return Direction.West;
        if (angle < 270) return Direction.SouthWest;
        if (angle < 315) return Direction.South;

        return Direction.SouthEast;
    }

    

    /// <summary>
    /// Uses global world orientation instead of camera to determine movement direction.
    /// </summary>
    // public static Direction GetWorldRelativeDirection(
    //     Vector3 movementVector,
    //     AnimationDirectionType directionType)
    // {
    //     if (Orientation == null)
    //     {
    //         Debug.LogWarning("WorldOrientationSO not assigned to DirectionUtil. Returning South.");
    //         return Direction.South;
    //     }
    //
    //     Vector3 forward = Orientation.WorldForward.normalized;
    //     Vector3 right = Orientation.WorldRight.normalized;
    //
    //     Vector2 flatMove = new Vector2(
    //         Vector3.Dot(movementVector, right),
    //         Vector3.Dot(movementVector, forward)
    //     ).normalized;
    //
    //     return GetDirection(flatMove, directionType);
    // }
}
