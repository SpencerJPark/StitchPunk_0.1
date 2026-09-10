// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Tests.EditMode
{
    [TestFixture]
    public sealed class CoverPaneSplitViewTests
    {
        private const string ScratchKeySuffix = "Tests.Scratch";

        [SetUp]
        public void SetUp()
        {
            EditorPrefs.DeleteKey(CoverPaneSplitView.PrefsKeyPrefix + ScratchKeySuffix);
        }

        [TearDown]
        public void TearDown()
        {
            EditorPrefs.DeleteKey(CoverPaneSplitView.PrefsKeyPrefix + ScratchKeySuffix);
        }

        [Test]
        public void StoredDimension_SurvivesAFreshInstance_AndReappliesToTheFixedPane()
        {
            CoverPaneSplitView firstInstance = new CoverPaneSplitView(ScratchKeySuffix, 0, 100f, TwoPaneSplitViewOrientation.Horizontal);
            firstInstance.Add(new VisualElement());
            firstInstance.Add(new VisualElement());
            firstInstance.StoreDimension(300f);

            CoverPaneSplitView secondInstance = new CoverPaneSplitView(ScratchKeySuffix, 0, 100f, TwoPaneSplitViewOrientation.Horizontal);
            secondInstance.Add(new VisualElement());
            secondInstance.Add(new VisualElement());
            secondInstance.FixedPane.style.width = 200f;
            secondInstance.ReapplyStoredDimension();

            Assert.AreEqual(300f, secondInstance.StoredDimension);
            Assert.AreEqual(300f, secondInstance.FixedPane.style.width.value.value, 0.01f);
        }
    }
}
