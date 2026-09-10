// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>EditMode coverage of <see cref="VatBakePanel"/> following a shared <see cref="ActiveAssetSelection"/>.</summary>
    public sealed class VatBakePanelTests
    {
        private RigAsset rigAsset;
        private VatBakePanel panel;
        private ActiveAssetSelection selection;

        [SetUp]
        public void SetUp()
        {
            rigAsset = ScriptableObject.CreateInstance<RigAsset>();
            selection = new ActiveAssetSelection();
            panel = new VatBakePanel();
        }

        [TearDown]
        public void TearDown()
        {
            panel.Dispose();
            Object.DestroyImmediate(rigAsset);
        }

        // Only the selection-to-field direction is asserted: a field write raises its ChangeEvent
        // through a panel, and a panel-less fixture element dispatches nothing.
        [Test]
        public void Bind_FollowsTheSharedSelection_AndLeavesTheFieldEditable()
        {
            selection.SetRig(rigAsset);
            panel.Bind(selection);

            ObjectField rigField = panel.Q<ObjectField>("vat-bake-rig-field");
            Assert.AreEqual(rigAsset, rigField.value);
            Assert.IsTrue(rigField.enabledSelf, "the rig field is a live picker now, never a disabled mirror");

            selection.SetRig(null);

            Assert.IsNull(rigField.value);
        }
    }
}
