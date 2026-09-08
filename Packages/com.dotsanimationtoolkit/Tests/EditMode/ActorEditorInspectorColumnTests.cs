// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Reflection;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <see cref="ActorEditorInspectorColumn"/>'s selection-driven blocks.
    /// A bare column is never attached to a panel, so a field's own value setter cannot dispatch a
    /// <c>ChangeEvent</c> — <c>ClipEditorAddEventTests</c>' pattern applies here too: the write each
    /// callback makes is invoked directly through reflection rather than simulated on the control.
    /// </summary>
    public sealed class ActorEditorInspectorColumnTests
    {
        private ActorProfileAsset profileAsset;
        private ActorLayerDefinition middleLayer;
        private ActorAnimationDefinition animationDefinition;

        [SetUp]
        public void SetUp()
        {
            profileAsset = ScriptableObject.CreateInstance<ActorProfileAsset>();
            middleLayer = new ActorLayerDefinition { displayName = "Action" };
            animationDefinition = new ActorAnimationDefinition();
            middleLayer.animations.Add(animationDefinition);
            profileAsset.layers.Insert(1, middleLayer);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(profileAsset);
        }

        [Test]
        public void TogglingDirectionDimension_FlipsHasDirectionsAndShowsTheQueueView()
        {
            ActorEditorInspectorColumn column = new ActorEditorInspectorColumn();
            column.Bind(profileAsset, null);
            column.SetSelection(new ActorEditorSelection
            {
                kind = ActorEditorSelectionKind.Animation, layerIndex = 1, animationIndex = 0
            });

            Assert.IsFalse(animationDefinition.hasDirections);
            VisualElement directionContainer = column.Q<VisualElement>("actor-editor-inspector-direction-container");
            ObjectField clipField = column.Q<ObjectField>("actor-editor-inspector-animation-clip-field");
            Assert.IsNotNull(directionContainer);
            Assert.IsNotNull(clipField);
            Assert.AreEqual(DisplayStyle.None, directionContainer.style.display.value);
            Assert.AreEqual(DisplayStyle.Flex, clipField.style.display.value);

            InvokeApply(column, "ApplyHasDirectionsChange", true);

            Assert.IsTrue(animationDefinition.hasDirections);
            Assert.AreEqual(DisplayStyle.Flex, directionContainer.style.display.value);
            Assert.AreEqual(DisplayStyle.None, clipField.style.display.value);
            Assert.IsNotNull(
                column.Q<DirectionSetClipQueueView>("actor-editor-inspector-direction-queue-view"),
                "The queue view must exist in the tree once the direction dimension is on.");
        }

        [Test]
        public void UseClipDefaultToggle_WritesNaNBlendIn()
        {
            animationDefinition.blendIn = 0.25f;
            ActorEditorInspectorColumn column = new ActorEditorInspectorColumn();
            column.Bind(profileAsset, null);
            column.SetSelection(new ActorEditorSelection
            {
                kind = ActorEditorSelectionKind.Animation, layerIndex = 1, animationIndex = 0
            });

            Assert.IsFalse(float.IsNaN(animationDefinition.blendIn));

            InvokeApply(column, "ApplyBlendInUseClipDefaultChange", true);

            Assert.IsTrue(
                float.IsNaN(animationDefinition.blendIn),
                "The toggle's whole contract is mapping 'use clip default' to the NaN sentinel.");

            FloatField blendInField = column.Q<FloatField>("actor-editor-inspector-blend-in-field");
            Assert.IsNotNull(blendInField);
            Assert.AreEqual(DisplayStyle.None, blendInField.style.display.value);
        }

        [Test]
        public void SelectingBaseLayer_ShowsReadOnlyNameField()
        {
            ActorEditorInspectorColumn column = new ActorEditorInspectorColumn();
            column.Bind(profileAsset, null);
            column.SetSelection(new ActorEditorSelection { kind = ActorEditorSelectionKind.Layer, layerIndex = 0 });

            TextField nameField = column.Q<TextField>("actor-editor-inspector-layer-name-field");
            Assert.IsNotNull(nameField);
            Assert.IsTrue(nameField.isReadOnly, "Base is a fixed bookend; its name must not be editable.");
            Assert.AreEqual(ActorProfileAsset.BaseLayerName, nameField.value);
            Assert.IsNotNull(
                column.Q<HelpBox>("actor-editor-inspector-layer-bookend-helpbox"),
                "A bookend's read-only name needs a one-line explanation.");
        }

        private static void InvokeApply(ActorEditorInspectorColumn column, string methodName, object argument)
        {
            MethodInfo method = typeof(ActorEditorInspectorColumn)
                .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "Expected a private method named " + methodName + ".");
            method.Invoke(column, new[] { argument });
        }
    }
}
