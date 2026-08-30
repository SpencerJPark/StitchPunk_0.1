// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Non-destructive Scene-view scrub preview and gizmo keying (Phase G, decision G-D1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Poses real scene GameObjects, never a mirror.</strong> Spec §3 makes Unity's own Scene
    /// view the viewport, so unlike the Clip Editor's <c>ClipPreviewController</c> (a
    /// <c>PreviewRenderUtility</c> instance over its own mirrored hierarchy) this writes straight
    /// onto the bound actors — which is also what makes Unity's built-in Move/Rotate/Scale gizmos
    /// work on them for free the moment one is selected; nothing here draws a custom gizmo.
    /// </para>
    /// <para>
    /// <strong>Entering capture, leaving restore, exactly.</strong> <see cref="EnterPreview"/>
    /// snapshots every bound GameObject's local transform, and every bound part's, before this
    /// controller ever writes to them; <see cref="ExitPreview"/> writes every snapshot back
    /// unconditionally. Nothing here uses Undo for the pose write/restore cycle — Undo is for
    /// authored changes (a keyed value), and a scrub is not one.
    /// </para>
    /// <para>
    /// <strong>Only root motion and part-track overrides are posed.</strong> What a clip block would
    /// show at this instant is not evaluated — that needs the baked <c>ClipRegistryBlob</c>/
    /// <c>ClipSampler</c> path (spec §6's "no second animation pipeline"), and there is no blob
    /// until G5. A part with no active override key simply sits at whatever the scene already shows
    /// it doing, which for an unposed rig is its rest pose. Recorded as owed, not silently missing.
    /// </para>
    /// </remarks>
    internal sealed class CutscenePreviewController
    {
        private sealed class TransformSnapshot
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;

            public static TransformSnapshot Capture(Transform transform)
            {
                return new TransformSnapshot
                {
                    localPosition = transform.localPosition,
                    localRotation = transform.localRotation,
                    localScale = transform.localScale
                };
            }

            public void RestoreTo(Transform transform)
            {
                transform.localPosition = localPosition;
                transform.localRotation = localRotation;
                transform.localScale = localScale;
            }
        }

        private readonly Dictionary<uint, GameObject> boundObjects = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, TransformSnapshot> rootSnapshots = new Dictionary<uint, TransformSnapshot>();
        private readonly Dictionary<uint, Dictionary<uint, Transform>> partTransformsBySlot =
            new Dictionary<uint, Dictionary<uint, Transform>>();
        private readonly Dictionary<uint, Dictionary<uint, TransformSnapshot>> partSnapshotsBySlot =
            new Dictionary<uint, Dictionary<uint, TransformSnapshot>>();

        public bool IsActive { get; private set; }

        /// <summary>Captures every bound GameObject's (and bound part's) current transform, then marks preview active.</summary>
        public void EnterPreview(CutsceneAsset cutscene, string sceneGuid)
        {
            if (IsActive || cutscene == null || cutscene.slots == null || string.IsNullOrEmpty(sceneGuid))
            {
                return;
            }

            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                if (slot == null)
                {
                    continue;
                }

                CutsceneSlotBindingEntry binding =
                    CutsceneSceneBindingUtility.FindBinding(cutscene, sceneGuid, slot.SlotId);
                if (binding == null)
                {
                    continue;
                }
                GameObject boundObject = CutsceneSceneBindingUtility.ResolveGameObject(binding.globalObjectId);
                if (boundObject == null)
                {
                    continue;
                }

                boundObjects[slot.SlotId] = boundObject;
                rootSnapshots[slot.SlotId] = TransformSnapshot.Capture(boundObject.transform);

                if (slot.kind != CutsceneSlotKind.Actor)
                {
                    continue;
                }

                Dictionary<uint, Transform> partTransforms = new Dictionary<uint, Transform>();
                Dictionary<uint, TransformSnapshot> partSnapshots = new Dictionary<uint, TransformSnapshot>();
                RigTargetAuthoring[] boundParts = boundObject.GetComponentsInChildren<RigTargetAuthoring>(true);
                for (int partIndex = 0; partIndex < boundParts.Length; partIndex++)
                {
                    RigTargetAuthoring part = boundParts[partIndex];
                    partTransforms[part.targetStableId] = part.transform;
                    partSnapshots[part.targetStableId] = TransformSnapshot.Capture(part.transform);
                }
                partTransformsBySlot[slot.SlotId] = partTransforms;
                partSnapshotsBySlot[slot.SlotId] = partSnapshots;
            }

            IsActive = true;
        }

        /// <summary>Restores every captured transform exactly and clears preview state (G-D1).</summary>
        public void ExitPreview()
        {
            if (!IsActive)
            {
                return;
            }

            foreach (KeyValuePair<uint, GameObject> boundObjectEntry in boundObjects)
            {
                if (boundObjectEntry.Value == null)
                {
                    continue;
                }
                TransformSnapshot rootSnapshot;
                if (rootSnapshots.TryGetValue(boundObjectEntry.Key, out rootSnapshot))
                {
                    rootSnapshot.RestoreTo(boundObjectEntry.Value.transform);
                }

                Dictionary<uint, Transform> partTransforms;
                Dictionary<uint, TransformSnapshot> partSnapshots;
                if (partTransformsBySlot.TryGetValue(boundObjectEntry.Key, out partTransforms) &&
                    partSnapshotsBySlot.TryGetValue(boundObjectEntry.Key, out partSnapshots))
                {
                    foreach (KeyValuePair<uint, Transform> partEntry in partTransforms)
                    {
                        if (partEntry.Value == null)
                        {
                            continue;
                        }
                        TransformSnapshot partSnapshot;
                        if (partSnapshots.TryGetValue(partEntry.Key, out partSnapshot))
                        {
                            partSnapshot.RestoreTo(partEntry.Value);
                        }
                    }
                }
            }

            boundObjects.Clear();
            rootSnapshots.Clear();
            partTransformsBySlot.Clear();
            partSnapshotsBySlot.Clear();
            IsActive = false;
        }

        /// <summary>The live bound GameObject for a slot, or null when unbound or preview is inactive.</summary>
        public GameObject GetBoundObject(uint slotId)
        {
            GameObject boundObject;
            return boundObjects.TryGetValue(slotId, out boundObject) ? boundObject : null;
        }

        /// <summary>The live child transform a part track's tag resolves to, or null when unresolved or preview is inactive.</summary>
        public Transform GetBoundPartTransform(uint slotId, RigAsset rig, uint tagId)
        {
            uint targetStableId;
            if (rig == null || !TryResolveTagToTargetId(rig, tagId, out targetStableId))
            {
                return null;
            }
            Dictionary<uint, Transform> partTransforms;
            Transform partTransform;
            if (partTransformsBySlot.TryGetValue(slotId, out partTransforms) &&
                partTransforms.TryGetValue(targetStableId, out partTransform))
            {
                return partTransform;
            }
            return null;
        }

        /// <summary>Poses every bound actor/prop at <paramref name="timeSeconds"/> — root motion plus any active part-track override.</summary>
        public void ApplyPose(CutsceneAsset cutscene, float timeSeconds)
        {
            if (!IsActive || cutscene == null || cutscene.slots == null)
            {
                return;
            }

            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                GameObject boundObject;
                if (slot == null || !boundObjects.TryGetValue(slot.SlotId, out boundObject) || boundObject == null)
                {
                    continue;
                }

                Vector3 position;
                Quaternion rotation;
                Vector3 scale;
                CutscenePoseSampler.Sample(slot.transformKeys, timeSeconds, out position, out rotation, out scale);
                boundObject.transform.localPosition = position;
                boundObject.transform.localRotation = rotation;
                boundObject.transform.localScale = scale;

                if (slot.kind != CutsceneSlotKind.Actor || slot.rig == null || slot.partTracks == null)
                {
                    continue;
                }

                Dictionary<uint, Transform> partTransforms;
                Dictionary<uint, TransformSnapshot> partSnapshots;
                if (!partTransformsBySlot.TryGetValue(slot.SlotId, out partTransforms) ||
                    !partSnapshotsBySlot.TryGetValue(slot.SlotId, out partSnapshots))
                {
                    continue;
                }

                for (int trackIndex = 0; trackIndex < slot.partTracks.Count; trackIndex++)
                {
                    CutsceneKeyedTrack track = slot.partTracks[trackIndex];
                    if (track == null || track.keys.Count == 0)
                    {
                        continue;
                    }
                    uint targetStableId;
                    if (!TryResolveTagToTargetId(slot.rig, track.tagId, out targetStableId))
                    {
                        continue;
                    }
                    Transform partTransform;
                    TransformSnapshot partRest;
                    if (!partTransforms.TryGetValue(targetStableId, out partTransform) || partTransform == null ||
                        !partSnapshots.TryGetValue(targetStableId, out partRest))
                    {
                        continue;
                    }

                    ApplyMaskedPartPose(track, partTransform, partRest, timeSeconds);
                }
            }
        }

        private static void ApplyMaskedPartPose(
            CutsceneKeyedTrack track, Transform partTransform, TransformSnapshot rest, float timeSeconds)
        {
            Vector3 sampledPosition;
            Quaternion sampledRotation;
            Vector3 sampledScale;
            CutscenePoseSampler.Sample(track.keys, timeSeconds, out sampledPosition, out sampledRotation, out sampledScale);

            Vector3 position = rest.localPosition;
            if ((track.channels & AnimatedChannels.PositionXY) != 0)
            {
                position.x = sampledPosition.x;
                position.y = sampledPosition.y;
            }
            if ((track.channels & AnimatedChannels.PositionZ) != 0)
            {
                position.z = sampledPosition.z;
            }
            partTransform.localPosition = position;

            partTransform.localRotation =
                (track.channels & AnimatedChannels.Rotation) != 0 ? sampledRotation : rest.localRotation;
            partTransform.localScale =
                (track.channels & AnimatedChannels.Scale) != 0 ? sampledScale : rest.localScale;
        }

        private static bool TryResolveTagToTargetId(RigAsset rig, uint tagId, out uint targetStableId)
        {
            targetStableId = 0u;
            if (rig.targets == null)
            {
                return false;
            }
            for (int i = 0; i < rig.targets.Count; i++)
            {
                RigTargetDefinition target = rig.targets[i];
                if (target != null && target.tagId == tagId)
                {
                    targetStableId = target.stableId;
                    return true;
                }
            }
            return false;
        }

        // -----------------------------------------------------------------------------------
        // Gizmo keying (spec §3): move the actor or a part with Unity's own transform tool,
        // then press Key — the same interaction family Rig Edit and Unity Timeline recording use.
        // -----------------------------------------------------------------------------------

        /// <summary>Keys the currently live root pose of <paramref name="slot"/> at <paramref name="timeSeconds"/>.</summary>
        public bool TryKeyRoot(
            SerializedObject serializedObject, SerializedProperty transformKeysProperty,
            CutsceneSlot slot, float timeSeconds)
        {
            GameObject boundObject;
            if (!boundObjects.TryGetValue(slot.SlotId, out boundObject) || boundObject == null)
            {
                return false;
            }
            UpsertTransformKey(
                transformKeysProperty, timeSeconds,
                boundObject.transform.localPosition,
                boundObject.transform.localRotation.eulerAngles,
                boundObject.transform.localScale);
            serializedObject.ApplyModifiedProperties();
            return true;
        }

        /// <summary>Keys the currently live pose of one part-track's bound child at <paramref name="timeSeconds"/>.</summary>
        public bool TryKeyPartTrack(
            SerializedObject serializedObject, SerializedProperty keysProperty,
            CutsceneSlot slot, CutsceneKeyedTrack track, float timeSeconds)
        {
            if (slot.rig == null)
            {
                return false;
            }
            uint targetStableId;
            if (!TryResolveTagToTargetId(slot.rig, track.tagId, out targetStableId))
            {
                return false;
            }
            Dictionary<uint, Transform> partTransforms;
            Transform partTransform;
            if (!partTransformsBySlot.TryGetValue(slot.SlotId, out partTransforms) ||
                !partTransforms.TryGetValue(targetStableId, out partTransform) || partTransform == null)
            {
                return false;
            }

            UpsertTransformKey(
                keysProperty, timeSeconds,
                partTransform.localPosition, partTransform.localRotation.eulerAngles, partTransform.localScale);
            serializedObject.ApplyModifiedProperties();
            return true;
        }

        /// <summary>Time epsilon within which a re-key overwrites an existing key rather than adding a duplicate.</summary>
        private const float KeyTimeEpsilon = 1f / 120f;

        private static void UpsertTransformKey(
            SerializedProperty listProperty, float timeSeconds, Vector3 position, Vector3 rotationEuler, Vector3 scale)
        {
            int existingIndex = -1;
            for (int i = 0; i < listProperty.arraySize; i++)
            {
                if (Mathf.Abs(listProperty.GetArrayElementAtIndex(i).FindPropertyRelative("time").floatValue - timeSeconds)
                    <= KeyTimeEpsilon)
                {
                    existingIndex = i;
                    break;
                }
            }

            int index = existingIndex;
            if (index < 0)
            {
                index = listProperty.arraySize;
                listProperty.InsertArrayElementAtIndex(index);
            }

            SerializedProperty element = listProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("time").floatValue = timeSeconds;
            WriteFloat3(element.FindPropertyRelative("position"), position);
            WriteFloat3(element.FindPropertyRelative("rotation"), rotationEuler);
            WriteFloat3(element.FindPropertyRelative("scale"), scale);
            if (existingIndex < 0)
            {
                element.FindPropertyRelative("interpolation").enumValueIndex = (int)Interpolation.Linear;
                SerializedProperty startHandle = element.FindPropertyRelative("bezierStartHandle");
                startHandle.FindPropertyRelative("x").floatValue = 0f;
                startHandle.FindPropertyRelative("y").floatValue = 0f;
                SerializedProperty endHandle = element.FindPropertyRelative("bezierEndHandle");
                endHandle.FindPropertyRelative("x").floatValue = 0f;
                endHandle.FindPropertyRelative("y").floatValue = 0f;
            }

            if (existingIndex < 0)
            {
                SortByTime(listProperty);
            }
        }

        private static void WriteFloat3(SerializedProperty float3Property, Vector3 value)
        {
            float3Property.FindPropertyRelative("x").floatValue = value.x;
            float3Property.FindPropertyRelative("y").floatValue = value.y;
            float3Property.FindPropertyRelative("z").floatValue = value.z;
        }

        private static void SortByTime(SerializedProperty listProperty)
        {
            int count = listProperty.arraySize;
            for (int i = 1; i < count; i++)
            {
                int j = i;
                while (j > 0 &&
                    listProperty.GetArrayElementAtIndex(j - 1).FindPropertyRelative("time").floatValue >
                    listProperty.GetArrayElementAtIndex(j).FindPropertyRelative("time").floatValue)
                {
                    listProperty.MoveArrayElement(j, j - 1);
                    j--;
                }
            }
        }
    }
}
