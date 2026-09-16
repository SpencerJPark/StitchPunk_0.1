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
        private readonly VisualElement selectBodyEmptyState;
        private readonly VisualElement bodyCard;
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
            AddToClassList("toolkit-column");
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            VisualElement headerRow = ToolkitChrome.MakePaneHeader("Inspector", out titleLabel, out _);
            headerRow.name = "ragdoll-inspector-header";
            titleLabel.name = "ragdoll-inspector-title";
            Add(headerRow);

            ScrollView scrollView = new ScrollView { name = "ragdoll-inspector-scroll" };
            scrollView.style.flexGrow = 1f;
            Add(scrollView);

            selectBodyEmptyState = ToolkitChrome.MakeEmptyState(
                "ragdoll-inspector-hint",
                "No body selected",
                "A selected body's collider, mass and joint limits appear here.",
                null,
                null);
            scrollView.Add(selectBodyEmptyState);

            bodyCard = ToolkitChrome.MakeCard("ragdoll-body-card", "Body", out bodySection, out _);
            scrollView.Add(bodyCard);

            VisualElement rigSettingsCardBody;
            rigSettingsSection = ToolkitChrome.MakeCard("ragdoll-rig-settings-card", "Rig settings", out rigSettingsCardBody, out _);

            spaceField = new EnumField(RagdollSpace.Planar2D) { name = "ragdoll-inspector-space" };
            spaceField.RegisterValueChangedCallback(changeEvent =>
            {
                if (isRefreshing)
                {
                    return;
                }

                CommitRigSettings((ref RagdollRigSettings settings) => settings.space = (RagdollSpace)spaceField.value);
            });
            rigSettingsCardBody.Add(ToolkitChrome.MakePropertyRow("Space", spaceField, string.Empty));

            gravityScaleField = new FloatField { name = "ragdoll-inspector-gravity-scale" };
            RegisterCommit(gravityScaleField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.gravityScale = gravityScaleField.value));
            rigSettingsCardBody.Add(ToolkitChrome.MakePropertyRow("Gravity Scale", gravityScaleField, string.Empty));

            defaultLinearDampingField = new FloatField { name = "ragdoll-inspector-default-linear-damping" };
            RegisterCommit(defaultLinearDampingField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.defaultLinearDamping = defaultLinearDampingField.value));
            rigSettingsCardBody.Add(ToolkitChrome.MakePropertyRow("Default Linear Damping", defaultLinearDampingField, string.Empty));

            defaultAngularDampingField = new FloatField { name = "ragdoll-inspector-default-angular-damping" };
            RegisterCommit(defaultAngularDampingField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.defaultAngularDamping = defaultAngularDampingField.value));
            rigSettingsCardBody.Add(ToolkitChrome.MakePropertyRow("Default Angular Damping", defaultAngularDampingField, string.Empty));

            jointStiffnessField = new FloatField { name = "ragdoll-inspector-joint-stiffness" };
            RegisterCommit(jointStiffnessField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.jointStiffness = jointStiffnessField.value));
            rigSettingsCardBody.Add(ToolkitChrome.MakePropertyRow("Joint Stiffness", jointStiffnessField, string.Empty));

            jointDampingField = new FloatField { name = "ragdoll-inspector-joint-damping" };
            RegisterCommit(jointDampingField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.jointDamping = jointDampingField.value));
            rigSettingsCardBody.Add(ToolkitChrome.MakePropertyRow("Joint Damping", jointDampingField, string.Empty));

            solverIterationsField = new IntegerField { name = "ragdoll-inspector-solver-iterations" };
            RegisterCommit(solverIterationsField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.solverIterations = (byte)Mathf.Clamp(solverIterationsField.value, 1, 32)));
            rigSettingsCardBody.Add(ToolkitChrome.MakePropertyRow("Solver Iterations", solverIterationsField, string.Empty));

            substepHzField = new FloatField { name = "ragdoll-inspector-substep-hz" };
            RegisterCommit(substepHzField, () => CommitRigSettings((ref RagdollRigSettings settings) => settings.substepHz = substepHzField.value));
            rigSettingsCardBody.Add(ToolkitChrome.MakePropertyRow("Substep Hz", substepHzField, string.Empty));

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
            selectBodyEmptyState.style.display = DisplayStyle.None;
            bodyCard.style.display = DisplayStyle.Flex;
            RebuildBodySection(selectedBodyDefinition);
        }

        private void ShowHintOnly()
        {
            selectBodyEmptyState.style.display = DisplayStyle.Flex;
            bodyCard.style.display = DisplayStyle.None;
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

            TextField nameField = new TextField { value = bodyDefinition.displayName, name = "ragdoll-inspector-name" };
            RegisterCommit(nameField, () => CommitBodyField(body => body.displayName = nameField.value));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Name", nameField, string.Empty));

            Vector3Field sizeField = new Vector3Field { value = ToVector3(bodyDefinition.boxSize), name = "ragdoll-inspector-size" };
            RegisterCommit(sizeField, () => CommitBodyField(body => body.boxSize = ToFloat3(sizeField.value)));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Size", sizeField, string.Empty));

            Vector3Field centerField = new Vector3Field { value = ToVector3(bodyDefinition.boxCenter), name = "ragdoll-inspector-center" };
            RegisterCommit(centerField, () => CommitBodyField(body => body.boxCenter = ToFloat3(centerField.value)));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Center", centerField, string.Empty));

            Vector3Field rotationField = new Vector3Field { value = ToVector3(bodyDefinition.boxEulerAngles), name = "ragdoll-inspector-rotation" };
            RegisterCommit(rotationField, () => CommitBodyField(body => body.boxEulerAngles = ToFloat3(rotationField.value)));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Rotation", rotationField, string.Empty));

            FloatField massField = new FloatField { value = bodyDefinition.mass, name = "ragdoll-inspector-mass" };
            RegisterCommit(massField, () => CommitBodyField(body => body.mass = massField.value));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Mass", massField, string.Empty));

            FloatField linearDampingField = new FloatField
            {
                value = bodyDefinition.linearDamping,
                name = "ragdoll-inspector-linear-damping"
            };
            RegisterCommit(linearDampingField, () => CommitBodyField(body => body.linearDamping = linearDampingField.value));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Linear Damping", linearDampingField, "-1 inherits the rig default below."));

            FloatField angularDampingField = new FloatField
            {
                value = bodyDefinition.angularDamping,
                name = "ragdoll-inspector-angular-damping"
            };
            RegisterCommit(angularDampingField, () => CommitBodyField(body => body.angularDamping = angularDampingField.value));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Angular Damping", angularDampingField, "-1 inherits the rig default below."));

            FloatField restitutionField = new FloatField { value = bodyDefinition.restitution, name = "ragdoll-inspector-restitution" };
            RegisterCommit(restitutionField, () => CommitBodyField(body => body.restitution = restitutionField.value));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Restitution", restitutionField, string.Empty));

            FloatField frictionField = new FloatField { value = bodyDefinition.friction, name = "ragdoll-inspector-friction" };
            RegisterCommit(frictionField, () => CommitBodyField(body => body.friction = frictionField.value));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Friction", frictionField, string.Empty));

            FloatField hingeMinField = new FloatField { value = bodyDefinition.limitMinDegrees, name = "ragdoll-inspector-hinge-min" };
            FloatField hingeMaxField = new FloatField { value = bodyDefinition.limitMaxDegrees, name = "ragdoll-inspector-hinge-max" };
            RegisterCommit(hingeMinField, () => CommitHingeLimit(hingeMinField.value, hingeMaxField.value));
            RegisterCommit(hingeMaxField, () => CommitHingeLimit(hingeMinField.value, hingeMaxField.value));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Hinge Min", hingeMinField, string.Empty));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Hinge Max", hingeMaxField, string.Empty));

            FloatField swingField = new FloatField { value = bodyDefinition.swingLimitDegrees, name = "ragdoll-inspector-swing" };
            FloatField twistField = new FloatField { value = bodyDefinition.twistLimitDegrees, name = "ragdoll-inspector-twist" };
            RegisterCommit(swingField, () => CommitConeLimit(swingField.value, twistField.value));
            RegisterCommit(twistField, () => CommitConeLimit(swingField.value, twistField.value));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Swing", swingField, string.Empty));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Twist", twistField, string.Empty));

            IntegerField selfGroupField = new IntegerField { value = bodyDefinition.selfGroup, name = "ragdoll-inspector-self-group" };
            RegisterCommit(selfGroupField, () => CommitBodyField(body => body.selfGroup = (byte)Mathf.Clamp(selfGroupField.value, 0, 7)));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Self Group", selfGroupField, string.Empty));

            IntegerField selfCollidesWithField = new IntegerField { value = bodyDefinition.selfCollidesWith, name = "ragdoll-inspector-self-collides-with" };
            RegisterCommit(selfCollidesWithField, () => CommitBodyField(body => body.selfCollidesWith = (byte)Mathf.Clamp(selfCollidesWithField.value, 0, 255)));
            bodySection.Add(ToolkitChrome.MakePropertyRow("Self Collides With", selfCollidesWithField, string.Empty));

            Toggle collidesWithWorldToggle = new Toggle { value = bodyDefinition.collidesWithWorld, name = "ragdoll-inspector-collides-with-world" };
            collidesWithWorldToggle.RegisterValueChangedCallback(changeEvent =>
            {
                if (isRefreshing)
                {
                    return;
                }

                CommitBodyField(body => body.collidesWithWorld = changeEvent.newValue);
            });
            bodySection.Add(ToolkitChrome.MakePropertyRow("Collides With World", collidesWithWorldToggle, string.Empty));
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
