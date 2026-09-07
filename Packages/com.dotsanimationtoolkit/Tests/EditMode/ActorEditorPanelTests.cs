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
    }
}
