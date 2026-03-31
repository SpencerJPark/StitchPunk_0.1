using UnityEngine;
using UnityEngine.InputSystem;

public class MouseWorldPosition : PersistentSingleton<MouseWorldPosition> {

    public Vector3 GetPosition() {
        Vector2 screenPos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        Ray mouseCameraRay = Camera.main.ScreenPointToRay(screenPos);

        Plane plane = new Plane(Vector3.up, Vector3.zero);

        if (plane.Raycast(mouseCameraRay, out float distance)) {
            return mouseCameraRay.GetPoint(distance);
        } else {
            return Vector3.zero;
        }
    }
}