// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Custom inspector for <see cref="ActorAuthoring"/>: a profile field with a derived rig/layer summary, plus the escape hatch to the Clip Editor.</summary>
    [CustomEditor(typeof(ActorAuthoring))]
    public sealed class ActorAuthoringEditor : UnityEditor.Editor
    {
        private SerializedProperty profileProperty;
        private SerializedProperty sampleOverrideProperty;
        private SerializedProperty addDistanceLodProperty;
        private SerializedProperty billboardModeProperty;
        private SerializedProperty frozenYawDegreesProperty;

        private VisualElement rigInfoSection;
        private PropertyField frozenYawField;

        // Rebuild trigger: only tear down and rebuild the rig-info section when the profile
        // reference itself changed, never on every keystroke inside an unrelated field.
        private ActorProfileAsset builtProfile;

        /// <inheritdoc />
        public override VisualElement CreateInspectorGUI()
        {
            profileProperty = serializedObject.FindProperty("profile");
            sampleOverrideProperty = serializedObject.FindProperty("sampleOverride");
            addDistanceLodProperty = serializedObject.FindProperty("addDistanceLod");
            billboardModeProperty = serializedObject.FindProperty("billboardMode");
            frozenYawDegreesProperty = serializedObject.FindProperty("frozenYawDegrees");

            VisualElement inspectorRoot = new VisualElement();
            inspectorRoot.style.paddingTop = 4f;

            inspectorRoot.Add(BuildSectionHeading("Profile"));
            if (profileProperty != null)
            {
                PropertyField profileField = new PropertyField(profileProperty, "Profile");
                profileField.tooltip =
                    "The profile that names this actor's rig, clip sets and layers.";
                inspectorRoot.Add(profileField);
            }

            rigInfoSection = new VisualElement();
            inspectorRoot.Add(rigInfoSection);

            inspectorRoot.Add(BuildSectionHeading("Sampling & LOD"));
            inspectorRoot.Add(BuildSampleRateField());
            if (addDistanceLodProperty != null)
            {
                inspectorRoot.Add(new PropertyField(addDistanceLodProperty, "Add Distance Lod"));
            }

            inspectorRoot.Add(BuildSectionHeading("Billboard"));
            if (billboardModeProperty != null)
            {
                PropertyField billboardModeField = new PropertyField(billboardModeProperty, "Billboard Mode");
                billboardModeField.RegisterValueChangeCallback(changeEvent => RefreshFrozenYawVisibility());
                inspectorRoot.Add(billboardModeField);
            }
            if (frozenYawDegreesProperty != null)
            {
                frozenYawField = new PropertyField(frozenYawDegreesProperty, "Frozen Yaw Degrees");
                inspectorRoot.Add(frozenYawField);
            }

            inspectorRoot.Add(BuildSectionHeading("Clip Editor"));
            Button openClipEditorButton = new Button(ClipEditorWindow.ShowWindow) { text = "Open in Clip Editor" };
            openClipEditorButton.tooltip =
                "Opens the Clip Editor window. It opens blank — assign one of this actor's clip "
                + "sets inside the window to edit its clips.";
            inspectorRoot.Add(openClipEditorButton);

            RebuildRigInfo();

            // One tracked callback for the whole asset: the rig-info section depends only on which
            // profile is assigned, so a targeted per-field callback would buy nothing here.
            inspectorRoot.TrackSerializedObjectValue(serializedObject, OnSerializedObjectChanged);

            inspectorRoot.Bind(serializedObject);

            RefreshFrozenYawVisibility();

            return inspectorRoot;
        }

        // -----------------------------------------------------------------------------------
        // Static chrome.
        // -----------------------------------------------------------------------------------

        private static Label BuildSectionHeading(string text)
        {
            Label heading = new Label(text);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginTop = 10f;
            heading.style.marginBottom = 2f;
            return heading;
        }

        // Binds only sampleOverride.rateHz, never the whole SampleSettings struct — phase01 is
        // derived at bake and re-derived at spawn, so an authored value would be silently discarded.
        private VisualElement BuildSampleRateField()
        {
            if (sampleOverrideProperty == null)
            {
                return new VisualElement();
            }
            SerializedProperty rateHzProperty = sampleOverrideProperty.FindPropertyRelative("rateHz");
            if (rateHzProperty == null)
            {
                return new VisualElement();
            }
            PropertyField sampleRateField = new PropertyField(rateHzProperty, "Sample Rate (Hz)");
            sampleRateField.tooltip =
                "0 falls back to AnimationToolkitConfig.defaultSampleRateHz. The override's phase01 "
                + "is not shown here — it is derived per instance at bake and re-derived at spawn, "
                + "never authored.";
            return sampleRateField;
        }

        // -----------------------------------------------------------------------------------
        // Billboard.
        // -----------------------------------------------------------------------------------

        private void RefreshFrozenYawVisibility()
        {
            if (frozenYawField == null || billboardModeProperty == null)
            {
                return;
            }
            bool isFrozenYaw = billboardModeProperty.enumValueIndex == (int)BillboardMode.FrozenYaw;
            frozenYawField.style.display = isFrozenYaw ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // -----------------------------------------------------------------------------------
        // Rig info.
        // -----------------------------------------------------------------------------------

        private void RebuildRigInfo()
        {
            rigInfoSection.Clear();
            builtProfile = profileProperty != null ? profileProperty.objectReferenceValue as ActorProfileAsset : null;

            if (builtProfile == null)
            {
                rigInfoSection.Add(new HelpBox(
                    "No profile assigned. This actor bakes to nothing until one is set — "
                    + "ActorBaker logs an error naming this GameObject.",
                    HelpBoxMessageType.Warning));
                return;
            }

            RigAsset rig = builtProfile.rig;
            if (rig == null)
            {
                rigInfoSection.Add(new HelpBox(
                    "Profile '" + builtProfile.name + "' has no rig assigned. Assign one before "
                    + "this actor can bake.",
                    HelpBoxMessageType.Warning));
                return;
            }

            Label rigLabel = new Label("Rig:  " + rig.name);
            rigLabel.style.marginBottom = 2f;
            rigInfoSection.Add(rigLabel);

            Label layerListNote = new Label(
                "Layer identity is list position, not a name — index 0 is Base, the last is "
                + "Override, and a higher index composites later and wins.");
            layerListNote.style.whiteSpace = WhiteSpace.Normal;
            layerListNote.style.opacity = 0.7f;
            layerListNote.style.marginBottom = 4f;
            rigInfoSection.Add(layerListNote);

            List<ActorLayerDefinition> layers = builtProfile.layers;
            if (layers == null || layers.Count == 0)
            {
                rigInfoSection.Add(new Label("This profile defines no layers."));
                return;
            }
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                ActorLayerDefinition layerDefinition = layers[layerIndex];
                string displayName = layerDefinition != null && !string.IsNullOrEmpty(layerDefinition.displayName)
                    ? layerDefinition.displayName
                    : "(unnamed)";
                bool defaultActive = layerDefinition != null && layerDefinition.defaultActive;
                rigInfoSection.Add(new Label(
                    layerIndex.ToString() + ":  " + displayName
                    + (defaultActive ? "   (active by default)" : string.Empty)));
            }
        }

        private void OnSerializedObjectChanged(SerializedObject changedSerializedObject)
        {
            ActorProfileAsset currentProfile =
                profileProperty != null ? profileProperty.objectReferenceValue as ActorProfileAsset : null;
            if (currentProfile != builtProfile)
            {
                RebuildRigInfo();
            }
        }
    }
}
