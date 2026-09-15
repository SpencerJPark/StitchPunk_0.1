// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Ragdoll tab's right column: the selected body's collider, mass, damping and
    /// limits, then the rig-wide ragdoll settings.</summary>
    public sealed class RagdollInspectorColumn : VisualElement
    {
        public event Action BodyEdited;

        private delegate void RagdollRigSettingsMutator(ref RagdollRigSettings settings);

        private readonly Label titleLabel;
        private readonly Label selectBodyHintLabel;
        private readonly VisualElement bodySection;
        private readonly VisualElement rigSettingsSection;

        private readonly EnumField spaceField;
        private readonly FloatField gravityScaleField;
        private readonly FloatField defaultLinearDampingField;
        private readonly FloatField defaultAngularDampingField;
        private readonly FloatField jointStiffnessField;
        private readonly FloatField jointDampingField;
        private readonly IntegerField solverIterationsField;
        private readonly FloatField substepHzField;

        private RigAsset boundRig;
        private uint selectedBodyId;
        private bool isRefreshing;

        public RagdollInspectorColumn()
        {
            name = "ragdoll-inspector-column";
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            VisualElement headerRow = new VisualElement { name = "ragdoll-inspector-header" };
            headerRow.AddToClassList("toolkit-pane-header");
            titleLabel = new Label("Inspector") { name = "ragdoll-inspector-title" };
            titleLabel.AddToClassList("toolkit-pane-title");
            headerRow.Add(titleLabel);
            Add(headerRow);

            ScrollView scrollView = new ScrollView { name = "ragdoll-inspector-scroll" };
            scrollView.style.flexGrow = 1f;
            Add(scrollView);

            selectBodyHintLabel = new Label("Select a body.") { name = "ragdoll-inspector-hint" };
            selectBodyHintLabel.AddToClassList("clip-editor__hint");
            scrollView.Add(selectBodyHintLabel);

            bodySection = new VisualElement { name = "ragdoll-inspector-body-section" };
            scrollView.Add(bodySection);

            VisualElement divider = new VisualElement { name = "ragdoll-inspector-divider" };
            divider.style.height = 1f;
            divider.style.marginTop = 8f;
            divider.style.marginBottom = 8f;
            divider.style.backgroundColor = ToolkitPalette.BoxBorder;
            scrollView.Add(divider);

            rigSettingsSection = new VisualElement { name = "ragdoll-inspector-rig-settings" };
            Label rigSettingsTitleLabel = new Label("Rig settings") { name = "ragdoll-inspector-rig-settings-title" };
            rigSettingsTitleLabel.AddToClassList("toolkit-pane-title");
            rigSettingsSection.Add(rigSettingsTitleLabel);

            spaceField = new EnumField("Space", RagdollSpace.Planar2D) { name = "ragdoll-inspector-space" };
            spaceField.RegisterValueChangedCallback(changeEvent =>
            {
                if (isRefreshing)
                {
                    return;
                }

                CommitRigSettings((ref RagdollRigSettings settings) => settings.space = (RagdollSpace)spaceField.value);
            });
            rigSettingsSection.Add(spaceField);

            gravityScaleField = new FloatField("Gravity Scale") { name = "ragdoll-inspector-gravity-scale" };
            RegisterCommit(gravityScaleField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.gravityScale = gravityScaleField.value));
            rigSettingsSection.Add(gravityScaleField);

            defaultLinearDampingField = new FloatField("Default Linear Damping") { name = "ragdoll-inspector-default-linear-damping" };
            RegisterCommit(defaultLinearDampingField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.defaultLinearDamping = defaultLinearDampingField.value));
            rigSettingsSection.Add(defaultLinearDampingField);

            defaultAngularDampingField = new FloatField("Default Angular Damping") { name = "ragdoll-inspector-default-angular-damping" };
            RegisterCommit(defaultAngularDampingField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.defaultAngularDamping = defaultAngularDampingField.value));
            rigSettingsSection.Add(defaultAngularDampingField);

            jointStiffnessField = new FloatField("Joint Stiffness") { name = "ragdoll-inspector-joint-stiffness" };
            RegisterCommit(jointStiffnessField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.jointStiffness = jointStiffnessField.value));
            rigSettingsSection.Add(jointStiffnessField);

            jointDampingField = new FloatField("Joint Damping") { name = "ragdoll-inspector-joint-damping" };
            RegisterCommit(jointDampingField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.jointDamping = jointDampingField.value));
            rigSettingsSection.Add(jointDampingField);

            solverIterationsField = new IntegerField("Solver Iterations") { name = "ragdoll-inspector-solver-iterations" };
            RegisterCommit(solverIterationsField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.solverIterations = (byte)Mathf.Clamp(solverIterationsField.value, 1, 32)));
            rigSettingsSection.Add(solverIterationsField);

            substepHzField = new FloatField("Substep Hz") { name = "ragdoll-inspector-substep-hz" };
            RegisterCommit(substepHzField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.substepHz = substepHzField.value));
            rigSettingsSection.Add(substepHzField);

            scrollView.Add(rigSettingsSection);
        }

        // Both setters redraw: the panel pushes a rig and a body id and never calls Refresh itself,
        // so a column that only stored them would stay empty until an edit happened to land.
        public void SetRig(RigAsset rig)
        {
            boundRig = rig;
            Refresh();
        }

        public void SetSelectedBodyId(uint bodyId)
        {
            selectedBodyId = bodyId;
            Refresh();
        }

        public void Refresh()
        {
            isRefreshing = true;
            try
            {
                RefreshInternal();
            }
            finally
            {
                isRefreshing = false;
            }
        }

        private void RefreshInternal()
        {
            if (boundRig == null)
            {
                titleLabel.text = "Inspector";
                ShowHintOnly();
                rigSettingsSection.style.display = DisplayStyle.None;
                return;
            }

            rigSettingsSection.style.display = DisplayStyle.Flex;
            RefreshRigSettingsFields();

            RagdollBodyDefinition selectedBodyDefinition = RagdollBodyEditing.FindBodyById(boundRig, selectedBodyId);
            if (selectedBodyDefinition == null)
            {
                titleLabel.text = "Inspector";
                ShowHintOnly();
                return;
            }

            titleLabel.text = "Inspector: " + selectedBodyDefinition.displayName;
            selectBodyHintLabel.style.display = DisplayStyle.None;
            bodySection.style.display = DisplayStyle.Flex;
            RebuildBodySection(selectedBodyDefinition);
        }

        private void ShowHintOnly()
        {
            selectBodyHintLabel.style.display = DisplayStyle.Flex;
            bodySection.style.display = DisplayStyle.None;
            bodySection.Clear();
        }

        private void RefreshRigSettingsFields()
        {
            RagdollRigSettings settings = boundRig.ragdollSettings;

            spaceField.value = settings.space;
            gravityScaleField.value = settings.gravityScale;
            defaultLinearDampingField.value = settings.defaultLinearDamping;
            defaultAngularDampingField.value = settings.defaultAngularDamping;
            jointStiffnessField.value = settings.jointStiffness;
            jointDampingField.value = settings.jointDamping;
            solverIterationsField.value = settings.solverIterations;
            substepHzField.value = settings.substepHz;
        }

        private void RebuildBodySection(RagdollBodyDefinition bodyDefinition)
        {
            bodySection.Clear();

            Label nodeLabel = new Label(DescribeAddress(bodyDefinition.address)) { name = "ragdoll-inspector-node" };
            bodySection.Add(nodeLabel);

            TextField nameField = new TextField("Name") { value = bodyDefinition.displayName, name = "ragdoll-inspector-name" };
            RegisterCommit(nameField, () => CommitBodyField(body => body.displayName = nameField.value));
            bodySection.Add(nameField);

            Vector3Field sizeField = new Vector3Field("Size") { value = ToVector3(bodyDefinition.boxSize), name = "ragdoll-inspector-size" };
            RegisterCommit(sizeField, () => CommitBodyField(body => body.boxSize = ToFloat3(sizeField.value)));
            bodySection.Add(sizeField);

            Vector3Field centerField = new Vector3Field("Center") { value = ToVector3(bodyDefinition.boxCenter), name = "ragdoll-inspector-center" };
            RegisterCommit(centerField, () => CommitBodyField(body => body.boxCenter = ToFloat3(centerField.value)));
            bodySection.Add(centerField);

            Vector3Field rotationField = new Vector3Field("Rotation") { value = ToVector3(bodyDefinition.boxEulerAngles), name = "ragdoll-inspector-rotation" };
            RegisterCommit(rotationField, () => CommitBodyField(body => body.boxEulerAngles = ToFloat3(rotationField.value)));
            bodySection.Add(rotationField);

            FloatField massField = new FloatField("Mass") { value = bodyDefinition.mass, name = "ragdoll-inspector-mass" };
            RegisterCommit(massField, () => CommitBodyField(body => body.mass = massField.value));
            bodySection.Add(massField);

            FloatField linearDampingField = new FloatField("Linear Damping")
            {
                value = bodyDefinition.linearDamping,
                name = "ragdoll-inspector-linear-damping",
                tooltip = "-1 inherits the rig default below."
            };
            RegisterCommit(linearDampingField, () => CommitBodyField(body => body.linearDamping = linearDampingField.value));
            bodySection.Add(linearDampingField);

            FloatField angularDampingField = new FloatField("Angular Damping")
            {
                value = bodyDefinition.angularDamping,
                name = "ragdoll-inspector-angular-damping",
                tooltip = "-1 inherits the rig default below."
            };
            RegisterCommit(angularDampingField, () => CommitBodyField(body => body.angularDamping = angularDampingField.value));
            bodySection.Add(angularDampingField);

            FloatField restitutionField = new FloatField("Restitution") { value = bodyDefinition.restitution, name = "ragdoll-inspector-restitution" };
            RegisterCommit(restitutionField, () => CommitBodyField(body => body.restitution = restitutionField.value));
            bodySection.Add(restitutionField);

            FloatField frictionField = new FloatField("Friction") { value = bodyDefinition.friction, name = "ragdoll-inspector-friction" };
            RegisterCommit(frictionField, () => CommitBodyField(body => body.friction = frictionField.value));
            bodySection.Add(frictionField);

            FloatField hingeMinField = new FloatField("Hinge Min") { value = bodyDefinition.limitMinDegrees, name = "ragdoll-inspector-hinge-min" };
            FloatField hingeMaxField = new FloatField("Hinge Max") { value = bodyDefinition.limitMaxDegrees, name = "ragdoll-inspector-hinge-max" };
            RegisterCommit(hingeMinField, () => CommitHingeLimit(hingeMinField.value, hingeMaxField.value));
            RegisterCommit(hingeMaxField, () => CommitHingeLimit(hingeMinField.value, hingeMaxField.value));
            bodySection.Add(hingeMinField);
            bodySection.Add(hingeMaxField);

            FloatField swingField = new FloatField("Swing") { value = bodyDefinition.swingLimitDegrees, name = "ragdoll-inspector-swing" };
            FloatField twistField = new FloatField("Twist") { value = bodyDefinition.twistLimitDegrees, name = "ragdoll-inspector-twist" };
            RegisterCommit(swingField, () => CommitConeLimit(swingField.value, twistField.value));
            RegisterCommit(twistField, () => CommitConeLimit(swingField.value, twistField.value));
            bodySection.Add(swingField);
            bodySection.Add(twistField);

            IntegerField selfGroupField = new IntegerField("Self Group") { value = bodyDefinition.selfGroup, name = "ragdoll-inspector-self-group" };
            RegisterCommit(selfGroupField, () => CommitBodyField(body => body.selfGroup = (byte)Mathf.Clamp(selfGroupField.value, 0, 7)));
            bodySection.Add(selfGroupField);

            IntegerField selfCollidesWithField = new IntegerField("Self Collides With") { value = bodyDefinition.selfCollidesWith, name = "ragdoll-inspector-self-collides-with" };
            RegisterCommit(selfCollidesWithField, () => CommitBodyField(body => body.selfCollidesWith = (byte)Mathf.Clamp(selfCollidesWithField.value, 0, 255)));
            bodySection.Add(selfCollidesWithField);

            Toggle collidesWithWorldToggle = new Toggle("Collides With World") { value = bodyDefinition.collidesWithWorld, name = "ragdoll-inspector-collides-with-world" };
            collidesWithWorldToggle.RegisterValueChangedCallback(changeEvent =>
            {
                if (isRefreshing)
                {
                    return;
                }

                CommitBodyField(body => body.collidesWithWorld = changeEvent.newValue);
            });
            bodySection.Add(collidesWithWorldToggle);
        }

        private void RegisterCommit(VisualElement field, Action commit)
        {
            field.RegisterCallback<BlurEvent>(evt =>
            {
                if (!isRefreshing)
                {
                    commit();
                }
            });

            field.RegisterCallback<KeyDownEvent>(keyDownEvent =>
            {
                if (isRefreshing)
                {
                    return;
                }

                if (keyDownEvent.keyCode == KeyCode.Return || keyDownEvent.keyCode == KeyCode.KeypadEnter)
                {
                    commit();
                }
            });
        }

        private void CommitBodyField(Action<RagdollBodyDefinition> mutate)
        {
            if (isRefreshing || boundRig == null)
            {
                return;
            }

            RagdollBodyDefinition targetBodyDefinition = RagdollBodyEditing.FindBodyById(boundRig, selectedBodyId);
            if (targetBodyDefinition == null)
            {
                return;
            }

            RagdollBodyEditing.ApplyBodyEdit(boundRig, "Edit Ragdoll Body");
            mutate(targetBodyDefinition);
            BodyEdited?.Invoke();
        }

        private void CommitHingeLimit(float minimumDegrees, float maximumDegrees)
        {
            if (isRefreshing || boundRig == null)
            {
                return;
            }

            RagdollBodyEditing.SetHingeLimit(boundRig, selectedBodyId, minimumDegrees, maximumDegrees);
            BodyEdited?.Invoke();
        }

        private void CommitConeLimit(float swingDegrees, float twistDegrees)
        {
            if (isRefreshing || boundRig == null)
            {
                return;
            }

            RagdollBodyEditing.SetConeLimit(boundRig, selectedBodyId, swingDegrees, twistDegrees);
            BodyEdited?.Invoke();
        }

        private void CommitRigSettings(RagdollRigSettingsMutator mutate)
        {
            if (isRefreshing || boundRig == null)
            {
                return;
            }

            RagdollRigSettings updatedSettings = boundRig.ragdollSettings;
            mutate(ref updatedSettings);
            RagdollBodyEditing.ApplyRigSettingsEdit(boundRig, updatedSettings, "Edit Ragdoll Rig Settings");
            BodyEdited?.Invoke();
        }

        private static string DescribeAddress(RigNodeAddress address)
        {
            switch (address.kind)
            {
                case RigNodeAddressKind.RigTarget:
                    return "rig target " + address.targetId;
                case RigNodeAddressKind.HierarchyPath:
                    return address.hierarchyPath;
                case RigNodeAddressKind.Bone:
                    return address.boneName;
                default:
                    return string.Empty;
            }
        }

        private static Vector3 ToVector3(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static float3 ToFloat3(Vector3 value)
        {
            return new float3(value.x, value.y, value.z);
        }
    }
}
