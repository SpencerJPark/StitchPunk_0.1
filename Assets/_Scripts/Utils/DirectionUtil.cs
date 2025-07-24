using UnityEngine;

public static class DirectionUtil
{
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

    public static Direction GetDirection(Vector2 dir, AnimationDirectionType directionType)
    {
        switch (directionType)
        {
            case AnimationDirectionType.TwoDirections:
                return Get2Direction(dir);
            case AnimationDirectionType.FourDirections:
                return Get4Direction(dir);
            case AnimationDirectionType.EightDirections:
                return Get8Direction(dir);
            default:
                Debug.LogWarning("Unknown direction type. Defaulting to South.");
                return Direction.South;
        }
    }

    /// <summary>
    /// Calculates camera-relative facing direction based on movement vector and camera rotation.
    /// </summary>
    public static Direction GetCameraRelativeDirection(
        Camera camera,
        Vector3 movementVector,
        AnimationDirectionType directionType)
    {
        if (camera == null)
        {
            Debug.LogWarning("Camera is null in GetCameraRelativeDirection.");
            return Direction.South;
        }

        Vector3 camForward = camera.transform.forward;
        camForward.y = 0f;

        Quaternion camRot = Quaternion.LookRotation(camForward);
        Vector3 camRelativeMove = camRot * movementVector;

        Vector2 flatDirection = new Vector2(camRelativeMove.x, camRelativeMove.z).normalized;
        return GetDirection(flatDirection, directionType);
    }
}
