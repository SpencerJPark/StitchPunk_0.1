// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Undo-recorded edits to a rig's ragdoll bodies and rig-wide ragdoll settings.</summary>
    public static class RagdollBodyEditing
    {
        public static RagdollBodyDefinition AddBody(RigAsset rig, in RigNodeAddress address, string displayName)
        {
            if (rig == null)
            {
                return null;
            }

            Undo.RecordObject(rig, "Add Ragdoll Body");

            if (rig.ragdollBodies == null)
            {
                rig.ragdollBodies = new List<RagdollBodyDefinition>();
            }

            RagdollBodyDefinition addedBodyDefinition = new RagdollBodyDefinition
            {
                displayName = displayName,
                address = address,
            };

            rig.ragdollBodies.Add(addedBodyDefinition);
            rig.EnsureStableIds();
            EditorUtility.SetDirty(rig);

            return addedBodyDefinition;
        }

        public static bool RemoveBody(RigAsset rig, uint bodyId)
        {
            if (rig == null || rig.ragdollBodies == null)
            {
                return false;
            }

            Undo.RecordObject(rig, "Remove Ragdoll Body");

            RagdollBodyDefinition matchingBodyDefinition = FindBodyById(rig, bodyId);
            if (matchingBodyDefinition == null)
            {
                return false;
            }

            bool bodyWasRemoved = rig.ragdollBodies.Remove(matchingBodyDefinition);
            EditorUtility.SetDirty(rig);

            return bodyWasRemoved;
        }

        public static void SetHingeLimit(RigAsset rig, uint bodyId, float minimumDegrees, float maximumDegrees)
        {
            RagdollBodyDefinition targetBodyDefinition = FindBodyById(rig, bodyId);
            if (targetBodyDefinition == null)
            {
                return;
            }

            Undo.RecordObject(rig, "Edit Ragdoll Limit");

            float clampedMaximumDegrees = Mathf.Clamp(maximumDegrees, -180f, 180f);
            float clampedMinimumDegrees = Mathf.Clamp(minimumDegrees, -180f, clampedMaximumDegrees);

            targetBodyDefinition.limitMinDegrees = clampedMinimumDegrees;
            targetBodyDefinition.limitMaxDegrees = clampedMaximumDegrees;

            EditorUtility.SetDirty(rig);
        }

        public static void SetConeLimit(RigAsset rig, uint bodyId, float swingDegrees, float twistDegrees)
        {
            RagdollBodyDefinition targetBodyDefinition = FindBodyById(rig, bodyId);
            if (targetBodyDefinition == null)
            {
                return;
            }

            Undo.RecordObject(rig, "Edit Ragdoll Limit");

            targetBodyDefinition.swingLimitDegrees = Mathf.Clamp(swingDegrees, 0f, 180f);
            targetBodyDefinition.twistLimitDegrees = Mathf.Clamp(twistDegrees, 0f, 180f);

            EditorUtility.SetDirty(rig);
        }

        public static void ApplyBodyEdit(RigAsset rig, string undoLabel)
        {
            if (rig == null)
            {
                return;
            }

            Undo.RecordObject(rig, undoLabel);
            EditorUtility.SetDirty(rig);
        }

        public static void ApplyRigSettingsEdit(RigAsset rig, in RagdollRigSettings settings, string undoLabel)
        {
            if (rig == null)
            {
                return;
            }

            Undo.RecordObject(rig, undoLabel);
            rig.ragdollSettings = settings;
            EditorUtility.SetDirty(rig);
        }

        public static RagdollBodyDefinition FindBodyById(RigAsset rig, uint bodyId)
        {
            if (rig == null || rig.ragdollBodies == null || bodyId == 0)
            {
                return null;
            }

            foreach (RagdollBodyDefinition candidateBodyDefinition in rig.ragdollBodies)
            {
                if (candidateBodyDefinition != null && candidateBodyDefinition.Id.Value == bodyId)
                {
                    return candidateBodyDefinition;
                }
            }

            return null;
        }
    }
}
