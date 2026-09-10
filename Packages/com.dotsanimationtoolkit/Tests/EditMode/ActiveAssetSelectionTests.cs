// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>EditMode coverage of <see cref="ActiveAssetSelection"/>'s change-detection and event raising.</summary>
    public sealed class ActiveAssetSelectionTests
    {
        private RigAsset rigAsset;
        private ClipSetAsset clipSetAsset;
        private ActiveAssetSelection activeAssetSelection;

        [SetUp]
        public void SetUp()
        {
            rigAsset = ScriptableObject.CreateInstance<RigAsset>();
            clipSetAsset = ScriptableObject.CreateInstance<ClipSetAsset>();
            activeAssetSelection = new ActiveAssetSelection();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(rigAsset);
            Object.DestroyImmediate(clipSetAsset);
        }

        [Test]
        public void SetRig_RaisesRigChangedOnce_AndNotAgainForTheSameValue()
        {
            int rigChangedEventCount = 0;
            activeAssetSelection.RigChanged += rig => rigChangedEventCount++;

            activeAssetSelection.SetRig(rigAsset);
            activeAssetSelection.SetRig(rigAsset);

            Assert.AreEqual(1, rigChangedEventCount);

            activeAssetSelection.SetRig(null);

            Assert.AreEqual(2, rigChangedEventCount);
        }

        [Test]
        public void SetClipSet_LeavesTheRigAlone()
        {
            int rigChangedEventCount = 0;
            activeAssetSelection.RigChanged += rig => rigChangedEventCount++;

            activeAssetSelection.SetRig(rigAsset);
            activeAssetSelection.SetClipSet(clipSetAsset);

            Assert.AreEqual(1, rigChangedEventCount);
            Assert.AreEqual(rigAsset, activeAssetSelection.Rig);
        }
    }
}
