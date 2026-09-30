using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PlaytestCopilot
{
    /// Tracks two of the six reference-resolver signals: what is under the mouse and what is at
    /// screen centre. Raises a "pointer" state sample only when one of the tracked objects changes,
    /// since polling every frame would flood the session log with duplicate samples.
    public sealed class PlaytestPointerTracker : MonoBehaviour
    {
        public Camera CaptureCamera { get; set; }

        public GameObject ObjectUnderCursor { get; private set; }
        public GameObject ObjectAtCameraCenter { get; private set; }
        public GameObject LastInteractedObject { get; private set; }

        // Legacy Input Manager can be disabled in an Input System-only project; UnityEngine.Input
        // then throws InvalidOperationException on every property access. Once that happens we stop
        // touching Input.mousePosition for the rest of the session and warn exactly once.
        private bool legacyMouseInputUnavailable;

        public void TickCapture()
        {
            Camera cameraToUse = CaptureCamera != null ? CaptureCamera : Camera.main;
            if (cameraToUse == null)
            {
                return;
            }

            GameObject newObjectUnderCursor = ObjectUnderCursor;
            if (!legacyMouseInputUnavailable)
            {
                Vector3 mousePosition;
                try
                {
                    mousePosition = Input.mousePosition;
                }
                catch (InvalidOperationException)
                {
                    legacyMouseInputUnavailable = true;
                    Debug.LogWarning("PlaytestPointerTracker: legacy Input.mousePosition is unavailable " +
                        "(Input System-only project); pointer-under-cursor tracking is disabled for this session.");
                    mousePosition = default;
                }

                if (!legacyMouseInputUnavailable)
                {
                    newObjectUnderCursor = RaycastFromScreenPoint(cameraToUse, mousePosition);
                }
            }

            Vector3 screenCenter = new Vector3(cameraToUse.pixelWidth * 0.5f, cameraToUse.pixelHeight * 0.5f, 0f);
            GameObject newObjectAtCameraCenter = RaycastFromScreenPoint(cameraToUse, screenCenter);

            bool objectUnderCursorChanged = newObjectUnderCursor != ObjectUnderCursor;
            bool objectAtCameraCenterChanged = newObjectAtCameraCenter != ObjectAtCameraCenter;

            ObjectUnderCursor = newObjectUnderCursor;
            ObjectAtCameraCenter = newObjectAtCameraCenter;

            if (objectUnderCursorChanged || objectAtCameraCenterChanged)
            {
                RaisePointerStateSample();
            }
        }

        public void NotifyInteraction(GameObject interacted)
        {
            LastInteractedObject = interacted;
        }

        private void RaisePointerStateSample()
        {
            List<PlaytestStateField> fields = new List<PlaytestStateField>();

            fields.Add(new PlaytestStateField("pointer.object",
                ObjectUnderCursor != null ? ObjectUnderCursor.name : string.Empty));
            fields.Add(new PlaytestStateField("pointer.path",
                ObjectUnderCursor != null ? BuildHierarchyPath(ObjectUnderCursor) : string.Empty));
            fields.Add(new PlaytestStateField("center.object",
                ObjectAtCameraCenter != null ? ObjectAtCameraCenter.name : string.Empty));
            fields.Add(new PlaytestStateField("center.path",
                ObjectAtCameraCenter != null ? BuildHierarchyPath(ObjectAtCameraCenter) : string.Empty));

            if (LastInteractedObject != null)
            {
                fields.Add(new PlaytestStateField("interaction.object", LastInteractedObject.name));
            }

            PlaytestStateSample sample = new PlaytestStateSample
            {
                Time = PlaytestSessionClock.Now,
                Kind = "pointer",
                Fields = fields,
            };

            PlaytestCaptureBus.RaiseStateSampleRecorded(sample);
        }

        // Tries a 3D physics raycast first, then a 2D one, and keeps whichever hit is nearer the
        // camera — a 2.5D project can have colliders of both kinds along the same ray.
        private static GameObject RaycastFromScreenPoint(Camera cameraToUse, Vector3 screenPoint)
        {
            Ray ray = cameraToUse.ScreenPointToRay(screenPoint);

            GameObject nearestHitObject = null;
            float nearestHitDistance = float.MaxValue;

            if (Physics.Raycast(ray, out RaycastHit hit3D))
            {
                nearestHitObject = hit3D.collider.gameObject;
                nearestHitDistance = hit3D.distance;
            }

            RaycastHit2D hit2D = Physics2D.GetRayIntersection(ray, float.PositiveInfinity);
            if (hit2D.collider != null && hit2D.distance < nearestHitDistance)
            {
                nearestHitObject = hit2D.collider.gameObject;
            }

            return nearestHitObject;
        }

        private static string BuildHierarchyPath(GameObject gameObject)
        {
            StringBuilder pathBuilder = new StringBuilder(gameObject.name);
            Transform currentParent = gameObject.transform.parent;
            while (currentParent != null)
            {
                pathBuilder.Insert(0, currentParent.name + "/");
                currentParent = currentParent.parent;
            }

            return pathBuilder.ToString();
        }
    }
}
