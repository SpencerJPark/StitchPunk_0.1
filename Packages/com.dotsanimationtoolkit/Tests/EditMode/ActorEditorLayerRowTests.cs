// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Pins A107-D2: selecting an animation row must drive the Animation card's speed field,
    /// not the layer row that used to own it.
    /// </summary>
    public sealed class ActorEditorLayerRowTests
    {
        private ActorProfileAsset profileAsset;

        [SetUp]
        public void SetUp()
        {
            profileAsset = ScriptableObject.CreateInstance<ActorProfileAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(profileAsset);
        }

        [Test]
        public void SelectingAnAnimationRow_ShowsItsSpeed_InTheAnimationCard()
        {
            // 2.75f, not the 1f default, so an unbound card would read wrong rather than
            // accidentally right.
            profileAsset.layers[0].animations.Add(new ActorAnimationDefinition { speed = 2.75f });

            ActorEditorPanel panel = new ActorEditorPanel();
            panel.Profile = profileAsset;

            ActorEditorInspectorColumn inspectorColumn = panel.Q<ActorEditorInspectorColumn>();
            Assert.IsNotNull(inspectorColumn, "the panel must host an ActorEditorInspectorColumn.");

            inspectorColumn.SetSelection(new ActorEditorSelection
            {
                kind = ActorEditorSelectionKind.Animation, layerIndex = 0, animationIndex = 0
            });

            FloatField speedField = panel.Q<FloatField>("actor-editor-inspector-speed-field");
            Assert.IsNotNull(speedField, "the Animation card's speed field must exist once an animation is selected.");
            Assert.AreEqual(2.75f, speedField.value,
                "the Animation card must read the selected animation's own speed, not the layer row's.");
        }
    }
}
