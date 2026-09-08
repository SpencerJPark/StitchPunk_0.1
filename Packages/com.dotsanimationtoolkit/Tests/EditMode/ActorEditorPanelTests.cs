// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <see cref="ActorEditorPanel"/>'s shell: the three named columns and the
    /// header's profile field exist, and assigning a profile raises <see cref="ActorEditorPanel.ProfileChanged"/>.
    /// </summary>
    public sealed class ActorEditorPanelTests
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
        public void Panel_ExposesThreeNamedColumnsAndTheProfileField()
        {
            ActorEditorPanel panel = new ActorEditorPanel();

            Assert.IsNotNull(panel.Q<VisualElement>("layers-column"), "layers-column must exist.");
            Assert.IsNotNull(panel.Q<VisualElement>("viewport-column"), "viewport-column must exist.");
            Assert.IsNotNull(panel.Q<VisualElement>("inspector-column"), "inspector-column must exist.");
            Assert.IsNotNull(
                panel.Q<ObjectField>("actor-editor-profile-field"),
                "the header Profile field must exist.");
        }

        [Test]
        public void AssigningAProfile_RaisesProfileChangedAndUpdatesTheField()
        {
            ActorEditorPanel panel = new ActorEditorPanel();
            ActorProfileAsset raisedProfile = null;
            panel.ProfileChanged += changedProfile => raisedProfile = changedProfile;

            panel.Profile = profileAsset;

            Assert.AreEqual(profileAsset, raisedProfile);
            Assert.AreEqual(profileAsset, panel.Profile);
            Assert.AreEqual(
                profileAsset, panel.Q<ObjectField>("actor-editor-profile-field").value,
                "the header field must reflect a profile assigned through the property.");
        }

        [Test]
        public void LayersColumn_ListsOneRowPerLayerAndPerAnimation()
        {
            profileAsset.layers.Insert(1, new ActorLayerDefinition
            {
                displayName = "Action",
                animations = new List<ActorAnimationDefinition>
                {
                    new ActorAnimationDefinition { animationKey = 111u },
                    new ActorAnimationDefinition { animationKey = 222u }
                }
            });

            ActorEditorLayersColumn layersColumn = new ActorEditorLayersColumn();
            layersColumn.Bind(profileAsset, null);

            List<VisualElement> layerRows =
                layersColumn.Query<VisualElement>(className: "actor-editor__layer-row").ToList();
            List<VisualElement> animationRows =
                layersColumn.Query<VisualElement>(className: "actor-editor__animation-row").ToList();

            Assert.AreEqual(3, layerRows.Count, "Base, Action and Override should each get one row.");
            Assert.AreEqual(2, animationRows.Count, "Both animations on the Action layer should get one row each.");
        }

        [Test]
        public void AddLayer_InsertsAboveOverrideAndKeepsBookendsInPlace()
        {
            ActorEditorLayersColumn layersColumn = new ActorEditorLayersColumn();
            layersColumn.Bind(profileAsset, null);

            layersColumn.AddLayer();

            Assert.AreEqual(3, profileAsset.layers.Count);
            Assert.AreEqual(ActorProfileAsset.BaseLayerName, profileAsset.layers[0].displayName);
            Assert.AreEqual(ActorProfileAsset.OverrideLayerName, profileAsset.layers[2].displayName);
        }

        [Test]
        public void MoveLayer_RevertsAnAttemptToMoveTheBaseBookend()
        {
            profileAsset.layers.Insert(1, new ActorLayerDefinition { displayName = "Action" });
            ActorEditorLayersColumn layersColumn = new ActorEditorLayersColumn();
            layersColumn.Bind(profileAsset, null);

            layersColumn.MoveLayer(0, 1);

            Assert.AreEqual(ActorProfileAsset.BaseLayerName, profileAsset.layers[0].displayName,
                "Base must never move out of index 0.");
            Assert.AreEqual("Action", profileAsset.layers[1].displayName,
                "the illegal move must leave the non-bookend layer where it was.");
        }
    }
}
