// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The Project Settings page for <see cref="RagdollPreviewScenery"/> (Phase D6, spec §8.6): the
    /// only place a developer authors drop-in test props, since none of them belong on a rig asset.
    /// </summary>
    /// <remarks>
    /// UI Toolkit throughout — <c>Conformance_E</c> bans IMGUI in package editor sources, and a
    /// <see cref="SettingsProvider"/> can be built entirely on the <c>rootElement</c> its activate
    /// handler receives instead of an <c>OnGUI</c> callback, exactly as every other panel in this
    /// package is built.
    /// </remarks>
    internal static class RagdollPreviewSceneryProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            SettingsProvider provider = new SettingsProvider(
                "Project/DOTS Animation Toolkit/Ragdoll Preview Scenery", SettingsScope.Project)
            {
                label = "Ragdoll Preview Scenery",
                activateHandler = (searchContext, rootElement) => BuildUI(rootElement),
                keywords = new HashSet<string>(new string[] { "ragdoll", "physics", "preview", "prop", "scenery" })
            };
            return provider;
        }

        private static void BuildUI(VisualElement rootElement)
        {
            rootElement.style.paddingLeft = 8;
            rootElement.style.paddingTop = 8;
            rootElement.style.paddingRight = 8;

            Label heading = new Label("Ragdoll Preview Scenery");
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.fontSize = 14;
            rootElement.Add(heading);

            rootElement.Add(new Label(
                "What the Clip Editor's Ragdoll toggle drops a previewed rig onto, beyond the "
                    + "always-present ground plane at y = 0. Editor-only and project-wide — never "
                    + "part of a rig asset, so a shipped rig never carries a test box."));

            VisualElement propsContainer = new VisualElement();
            rootElement.Add(propsContainer);

            Button addButton = new Button(() =>
            {
                RagdollPreviewScenery.instance.AddBoxProp();
                RebuildPropsList(propsContainer);
            })
            {
                text = "Add Box Prop"
            };
            rootElement.Add(addButton);

            RebuildPropsList(propsContainer);
        }

        private static void RebuildPropsList(VisualElement propsContainer)
        {
            propsContainer.Clear();

            List<RagdollPreviewPropDefinition> props = RagdollPreviewScenery.instance.Props;
            if (props.Count == 0)
            {
                propsContainer.Add(new Label("No props. The rig falls onto the ground plane alone."));
                return;
            }

            for (int index = 0; index < props.Count; index++)
            {
                propsContainer.Add(BuildPropRow(props[index]));
            }
        }

        private static VisualElement BuildPropRow(RagdollPreviewPropDefinition prop)
        {
            VisualElement row = new VisualElement();
            row.style.borderTopWidth = 1;
            row.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            row.style.paddingTop = 4;
            row.style.paddingBottom = 4;
            row.style.marginTop = 4;

            VisualElement headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;

            Toggle enabledToggle = new Toggle();
            enabledToggle.SetValueWithoutNotify(prop.enabled);
            enabledToggle.tooltip = "Whether this prop currently takes part in the preview drop.";
            enabledToggle.RegisterValueChangedCallback(changeEvent =>
            {
                prop.enabled = changeEvent.newValue;
                RagdollPreviewScenery.instance.PersistChange();
            });
            headerRow.Add(enabledToggle);

            TextField nameField = new TextField();
            nameField.SetValueWithoutNotify(prop.displayName);
            nameField.style.flexGrow = 1f;
            nameField.RegisterValueChangedCallback(changeEvent =>
            {
                prop.displayName = changeEvent.newValue;
                RagdollPreviewScenery.instance.PersistChange();
            });
            headerRow.Add(nameField);

            Button removeButton = new Button(() =>
            {
                RagdollPreviewScenery.instance.RemoveProp(prop);
                RebuildPropsList((VisualElement)row.parent);
            })
            {
                text = "Remove"
            };
            headerRow.Add(removeButton);
            row.Add(headerRow);

            EnumField shapeField = new EnumField("Shape", prop.shape);
            shapeField.tooltip =
                "Box: a flat platform. Ramp: the same single contact plane, oriented by Rotation "
                    + "rather than assumed horizontal.";
            shapeField.RegisterValueChangedCallback(changeEvent =>
            {
                prop.shape = (RagdollPreviewPropShape)changeEvent.newValue;
                RagdollPreviewScenery.instance.PersistChange();
            });
            row.Add(shapeField);

            Vector3Field positionField = new Vector3Field("Position");
            positionField.SetValueWithoutNotify(new Vector3(prop.position.x, prop.position.y, prop.position.z));
            positionField.RegisterValueChangedCallback(changeEvent =>
            {
                prop.position = new Unity.Mathematics.float3(
                    changeEvent.newValue.x, changeEvent.newValue.y, changeEvent.newValue.z);
                RagdollPreviewScenery.instance.PersistChange();
            });
            row.Add(positionField);

            Vector3Field sizeField = new Vector3Field("Size");
            sizeField.SetValueWithoutNotify(new Vector3(prop.size.x, prop.size.y, prop.size.z));
            sizeField.RegisterValueChangedCallback(changeEvent =>
            {
                Vector3 clamped = new Vector3(
                    Mathf.Max(0.01f, changeEvent.newValue.x),
                    Mathf.Max(0.01f, changeEvent.newValue.y),
                    Mathf.Max(0.01f, changeEvent.newValue.z));
                prop.size = new Unity.Mathematics.float3(clamped.x, clamped.y, clamped.z);
                RagdollPreviewScenery.instance.PersistChange();
            });
            row.Add(sizeField);

            Vector3Field rotationField = new Vector3Field("Rotation");
            rotationField.SetValueWithoutNotify(
                new Vector3(prop.eulerAngles.x, prop.eulerAngles.y, prop.eulerAngles.z));
            rotationField.RegisterValueChangedCallback(changeEvent =>
            {
                prop.eulerAngles = new Unity.Mathematics.float3(
                    changeEvent.newValue.x, changeEvent.newValue.y, changeEvent.newValue.z);
                RagdollPreviewScenery.instance.PersistChange();
            });
            row.Add(rotationField);

            return row;
        }
    }
}
