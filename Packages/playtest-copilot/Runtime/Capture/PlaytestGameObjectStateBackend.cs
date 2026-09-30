using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PlaytestCopilot
{
    /// Samples the GameObject/MonoBehaviour world at a fixed interval: scene, camera, the player
    /// transform/Rigidbody, and every object within NearbyRadius carrying [PlaytestTrack] members.
    /// This is the Milestone 1 state backend; the Entities backend is separate (Milestone 1b).
    public sealed class PlaytestGameObjectStateBackend : MonoBehaviour
    {
        public float SnapshotIntervalSeconds { get; set; } = 0.25f;
        public float NearbyRadius { get; set; } = 20f;
        public int MaxNearbyObjects { get; set; } = 24;
        public Transform PlayerTransform { get; set; }

        private float snapshotTimerSeconds;

        private const BindingFlags TrackedMemberBindingFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Keyed by Component's runtime Type so two dozen nearby objects at 4 Hz do not re-walk
        // reflection every tick. Never invalidated: a type's [PlaytestTrack] members are fixed
        // for the process lifetime, so a stale entry cannot happen.
        private static readonly Dictionary<Type, List<TrackedMemberInfo>> TrackedMembersCacheByType =
            new Dictionary<Type, List<TrackedMemberInfo>>();

        public void TickCapture(float unscaledDeltaTime)
        {
            snapshotTimerSeconds += unscaledDeltaTime;
            if (snapshotTimerSeconds < SnapshotIntervalSeconds)
            {
                return;
            }

            // Subtract rather than reset to zero so a frame hitch does not permanently skew the cadence.
            snapshotTimerSeconds = SnapshotIntervalSeconds > 0f
                ? snapshotTimerSeconds - SnapshotIntervalSeconds
                : 0f;

            PlaytestStateSample sample = new PlaytestStateSample();
            sample.Time = PlaytestSessionClock.Now;
            sample.Kind = "snapshot";
            sample.Fields = CaptureSnapshotFields();
            PlaytestCaptureBus.RaiseStateSampleRecorded(sample);
        }

        public List<PlaytestStateField> CaptureSnapshotFields()
        {
            List<PlaytestStateField> fields = new List<PlaytestStateField>();

            Scene activeScene = SceneManager.GetActiveScene();
            fields.Add(new PlaytestStateField("scene.active", activeScene.name));

            Vector3 nearbySearchOrigin = Vector3.zero;
            bool hasNearbySearchOrigin = false;

            Camera captureCamera = Camera.main;
            if (captureCamera != null)
            {
                Transform cameraTransform = captureCamera.transform;
                Vector3 cameraPosition = cameraTransform.position;
                Vector3 cameraEulerAngles = cameraTransform.eulerAngles;
                fields.Add(new PlaytestStateField("camera.position", FormatVector(cameraPosition.x, cameraPosition.y, cameraPosition.z)));
                fields.Add(new PlaytestStateField("camera.rotation", FormatVector(cameraEulerAngles.x, cameraEulerAngles.y, cameraEulerAngles.z)));

                // Falls back to the camera when there is no player transform, per contract.
                nearbySearchOrigin = cameraPosition;
                hasNearbySearchOrigin = true;
            }

            if (PlayerTransform != null)
            {
                Vector3 playerPosition = PlayerTransform.position;
                fields.Add(new PlaytestStateField("player.position", FormatVector(playerPosition.x, playerPosition.y, playerPosition.z)));

                Rigidbody playerRigidbody3D = PlayerTransform.GetComponent<Rigidbody>();
                Rigidbody2D playerRigidbody2D = PlayerTransform.GetComponent<Rigidbody2D>();
                if (playerRigidbody3D != null)
                {
                    Vector3 playerVelocity3D = playerRigidbody3D.linearVelocity;
                    fields.Add(new PlaytestStateField("player.velocity", FormatVector(playerVelocity3D.x, playerVelocity3D.y, playerVelocity3D.z)));
                }
                else if (playerRigidbody2D != null)
                {
                    Vector2 playerVelocity2D = playerRigidbody2D.linearVelocity;
                    fields.Add(new PlaytestStateField("player.velocity", FormatVector(playerVelocity2D.x, playerVelocity2D.y)));
                }

                // "grounded" is never computed here (no raycast) - it is only reported when the
                // game's own code opts in via [PlaytestTrack], per contract.
                string groundedValue = FindGroundedValueFromTrackedMembers(PlayerTransform.gameObject);
                if (groundedValue != null)
                {
                    fields.Add(new PlaytestStateField("player.grounded", groundedValue));
                }

                nearbySearchOrigin = playerPosition;
                hasNearbySearchOrigin = true;
            }

            if (hasNearbySearchOrigin)
            {
                AppendNearbyObjectFields(fields, nearbySearchOrigin);
            }

            return fields;
        }

        private void AppendNearbyObjectFields(List<PlaytestStateField> fields, Vector3 searchOrigin)
        {
            List<GameObject> nearbyGameObjects = FindNearbyGameObjects(searchOrigin);

            for (int nearbyIndex = 0; nearbyIndex < nearbyGameObjects.Count; nearbyIndex++)
            {
                GameObject nearbyGameObject = nearbyGameObjects[nearbyIndex];
                string fieldPrefix = "nearby[" + nearbyIndex.ToString(CultureInfo.InvariantCulture) + "].";

                fields.Add(new PlaytestStateField(fieldPrefix + "name", nearbyGameObject.name));
                fields.Add(new PlaytestStateField(fieldPrefix + "path", BuildHierarchyPath(nearbyGameObject)));

#if UNITY_EDITOR
                string prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(nearbyGameObject);
                if (!string.IsNullOrEmpty(prefabAssetPath))
                {
                    fields.Add(new PlaytestStateField(fieldPrefix + "prefab", prefabAssetPath));
                }
#endif

                Component[] componentsOnNearbyObject = nearbyGameObject.GetComponents<Component>();
                for (int componentIndex = 0; componentIndex < componentsOnNearbyObject.Length; componentIndex++)
                {
                    Component component = componentsOnNearbyObject[componentIndex];
                    if (component == null)
                    {
                        continue;
                    }

                    List<PlaytestStateField> trackedFields = ReadTrackedMembers(component);
                    for (int trackedIndex = 0; trackedIndex < trackedFields.Count; trackedIndex++)
                    {
                        PlaytestStateField trackedField = trackedFields[trackedIndex];
                        fields.Add(new PlaytestStateField(fieldPrefix + trackedField.Name, trackedField.Value));
                    }
                }
            }
        }

        private List<GameObject> FindNearbyGameObjects(Vector3 searchOrigin)
        {
            Dictionary<GameObject, float> nearestDistanceByGameObject = new Dictionary<GameObject, float>();

            Collider[] nearbyColliders3D = Physics.OverlapSphere(searchOrigin, NearbyRadius);
            for (int colliderIndex = 0; colliderIndex < nearbyColliders3D.Length; colliderIndex++)
            {
                Collider collider = nearbyColliders3D[colliderIndex];
                if (collider == null)
                {
                    continue;
                }

                GameObject candidateGameObject = collider.attachedRigidbody != null
                    ? collider.attachedRigidbody.gameObject
                    : collider.gameObject;
                RegisterNearbyCandidate(nearestDistanceByGameObject, candidateGameObject, searchOrigin);
            }

            Collider2D[] nearbyColliders2D = Physics2D.OverlapCircleAll(searchOrigin, NearbyRadius);
            for (int colliderIndex = 0; colliderIndex < nearbyColliders2D.Length; colliderIndex++)
            {
                Collider2D collider2D = nearbyColliders2D[colliderIndex];
                if (collider2D == null)
                {
                    continue;
                }

                GameObject candidateGameObject = collider2D.attachedRigidbody != null
                    ? collider2D.attachedRigidbody.gameObject
                    : collider2D.gameObject;
                RegisterNearbyCandidate(nearestDistanceByGameObject, candidateGameObject, searchOrigin);
            }

            List<GameObject> sortedNearbyGameObjects = new List<GameObject>(nearestDistanceByGameObject.Keys);
            sortedNearbyGameObjects.Sort((firstGameObject, secondGameObject) =>
                nearestDistanceByGameObject[firstGameObject].CompareTo(nearestDistanceByGameObject[secondGameObject]));

            if (sortedNearbyGameObjects.Count > MaxNearbyObjects)
            {
                sortedNearbyGameObjects.RemoveRange(MaxNearbyObjects, sortedNearbyGameObjects.Count - MaxNearbyObjects);
            }

            return sortedNearbyGameObjects;
        }

        private void RegisterNearbyCandidate(Dictionary<GameObject, float> nearestDistanceByGameObject, GameObject candidateGameObject, Vector3 searchOrigin)
        {
            if (candidateGameObject == null)
            {
                return;
            }

            // The player is already reported under player.* - do not also count it as its own neighbour.
            if (PlayerTransform != null && candidateGameObject == PlayerTransform.gameObject)
            {
                return;
            }

            float distanceToCandidate = Vector3.Distance(searchOrigin, candidateGameObject.transform.position);
            float existingDistance;
            if (!nearestDistanceByGameObject.TryGetValue(candidateGameObject, out existingDistance) || distanceToCandidate < existingDistance)
            {
                nearestDistanceByGameObject[candidateGameObject] = distanceToCandidate;
            }
        }

        // Looks for a [PlaytestTrack] member whose output name ends in "grounded" (case-insensitive),
        // e.g. a DisplayName of "grounded" or a member literally named Grounded/IsGrounded via DisplayName.
        private static string FindGroundedValueFromTrackedMembers(GameObject playerGameObject)
        {
            Component[] componentsOnPlayer = playerGameObject.GetComponents<Component>();
            for (int componentIndex = 0; componentIndex < componentsOnPlayer.Length; componentIndex++)
            {
                Component component = componentsOnPlayer[componentIndex];
                if (component == null)
                {
                    continue;
                }

                List<PlaytestStateField> trackedFields = ReadTrackedMembers(component);
                for (int trackedIndex = 0; trackedIndex < trackedFields.Count; trackedIndex++)
                {
                    PlaytestStateField trackedField = trackedFields[trackedIndex];
                    string trailingMemberName = trackedField.Name;
                    int lastDotIndex = trailingMemberName.LastIndexOf('.');
                    if (lastDotIndex >= 0)
                    {
                        trailingMemberName = trailingMemberName.Substring(lastDotIndex + 1);
                    }

                    if (string.Equals(trailingMemberName, "grounded", StringComparison.OrdinalIgnoreCase))
                    {
                        return trackedField.Value;
                    }
                }
            }

            return null;
        }

        public static List<PlaytestStateField> ReadTrackedMembers(Component component)
        {
            List<PlaytestStateField> resultFields = new List<PlaytestStateField>();
            if (component == null)
            {
                return resultFields;
            }

            Type componentType = component.GetType();
            List<TrackedMemberInfo> trackedMembers;
            if (!TrackedMembersCacheByType.TryGetValue(componentType, out trackedMembers))
            {
                trackedMembers = BuildTrackedMembersForType(componentType);
                TrackedMembersCacheByType[componentType] = trackedMembers;
            }

            for (int memberIndex = 0; memberIndex < trackedMembers.Count; memberIndex++)
            {
                TrackedMemberInfo trackedMember = trackedMembers[memberIndex];
                try
                {
                    object rawValue = trackedMember.Field != null
                        ? trackedMember.Field.GetValue(component)
                        : trackedMember.Property.GetValue(component, null);
                    string valueAsString = ConvertValueToInvariantString(rawValue);
                    resultFields.Add(new PlaytestStateField(trackedMember.OutputName, valueAsString));
                }
                catch (Exception)
                {
                    // A property getter can throw (e.g. only valid mid-state); skip that one
                    // member rather than losing the rest of the snapshot.
                }
            }

            return resultFields;
        }

        private static List<TrackedMemberInfo> BuildTrackedMembersForType(Type componentType)
        {
            List<TrackedMemberInfo> trackedMembers = new List<TrackedMemberInfo>();

            // No BindingFlags.Static above, so static members are already excluded.
            FieldInfo[] declaredFields = componentType.GetFields(TrackedMemberBindingFlags);
            for (int fieldIndex = 0; fieldIndex < declaredFields.Length; fieldIndex++)
            {
                FieldInfo field = declaredFields[fieldIndex];
                PlaytestTrackAttribute trackAttribute =
                    (PlaytestTrackAttribute)Attribute.GetCustomAttribute(field, typeof(PlaytestTrackAttribute), true);
                if (trackAttribute == null)
                {
                    continue;
                }

                string outputName = string.IsNullOrEmpty(trackAttribute.DisplayName)
                    ? componentType.Name + "." + field.Name
                    : trackAttribute.DisplayName;
                trackedMembers.Add(new TrackedMemberInfo(field, null, outputName));
            }

            PropertyInfo[] declaredProperties = componentType.GetProperties(TrackedMemberBindingFlags);
            for (int propertyIndex = 0; propertyIndex < declaredProperties.Length; propertyIndex++)
            {
                PropertyInfo property = declaredProperties[propertyIndex];
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                PlaytestTrackAttribute trackAttribute =
                    (PlaytestTrackAttribute)Attribute.GetCustomAttribute(property, typeof(PlaytestTrackAttribute), true);
                if (trackAttribute == null)
                {
                    continue;
                }

                string outputName = string.IsNullOrEmpty(trackAttribute.DisplayName)
                    ? componentType.Name + "." + property.Name
                    : trackAttribute.DisplayName;
                trackedMembers.Add(new TrackedMemberInfo(null, property, outputName));
            }

            return trackedMembers;
        }

        private static string ConvertValueToInvariantString(object rawValue)
        {
            if (rawValue == null)
            {
                return "null";
            }

            switch (rawValue)
            {
                case Vector2 vector2Value:
                    return FormatVector(vector2Value.x, vector2Value.y);
                case Vector3 vector3Value:
                    return FormatVector(vector3Value.x, vector3Value.y, vector3Value.z);
                case Vector4 vector4Value:
                    return FormatVector(vector4Value.x, vector4Value.y, vector4Value.z, vector4Value.w);
                case Quaternion quaternionValue:
                    return FormatVector(quaternionValue.x, quaternionValue.y, quaternionValue.z, quaternionValue.w);
                case Color colorValue:
                    return FormatVector(colorValue.r, colorValue.g, colorValue.b, colorValue.a);
                case IFormattable formattableValue:
                    return formattableValue.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return rawValue.ToString();
            }
        }

        private static string FormatVector(params float[] componentValues)
        {
            string[] componentsAsStrings = new string[componentValues.Length];
            for (int componentIndex = 0; componentIndex < componentValues.Length; componentIndex++)
            {
                componentsAsStrings[componentIndex] = componentValues[componentIndex].ToString(CultureInfo.InvariantCulture);
            }

            return string.Join(",", componentsAsStrings);
        }

        private static string BuildHierarchyPath(GameObject targetGameObject)
        {
            if (targetGameObject == null)
            {
                return string.Empty;
            }

            List<string> pathSegments = new List<string>();
            Transform currentTransform = targetGameObject.transform;
            while (currentTransform != null)
            {
                pathSegments.Insert(0, currentTransform.name);
                currentTransform = currentTransform.parent;
            }

            return string.Join("/", pathSegments);
        }

        private sealed class TrackedMemberInfo
        {
            public readonly FieldInfo Field;
            public readonly PropertyInfo Property;
            public readonly string OutputName;

            public TrackedMemberInfo(FieldInfo field, PropertyInfo property, string outputName)
            {
                Field = field;
                Property = property;
                OutputName = outputName;
            }
        }
    }
}
