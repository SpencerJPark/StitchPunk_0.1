// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Add/remove-by-clip behaviour of <see cref="ClipAssetUtility"/> on an in-memory set.</summary>
    public sealed class ClipAssetUtilityTests
    {
        private ClipSetAsset clipSet;
        private ClipAsset clipA;
        private ClipAsset clipB;

        [SetUp]
        public void SetUp()
        {
            clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipA = ScriptableObject.CreateInstance<ClipAsset>();
            clipB = ScriptableObject.CreateInstance<ClipAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(clipSet);
            Object.DestroyImmediate(clipA);
            Object.DestroyImmediate(clipB);
        }

        [Test]
        public void AddExistingClipToSet_AppendsOnce_AndRefusesTheDuplicate()
        {
            bool firstAddResult = ClipAssetUtility.AddExistingClipToSet(clipSet, clipA);

            Assert.IsTrue(firstAddResult);
            Assert.AreEqual(1, clipSet.clips.Count);

            bool duplicateAddResult = ClipAssetUtility.AddExistingClipToSet(clipSet, clipA);

            Assert.IsFalse(duplicateAddResult);
            Assert.AreEqual(1, clipSet.clips.Count);
        }

        [Test]
        public void RemoveClipFromSet_ByClip_RemovesEveryEntry()
        {
            clipSet.clips.Add(clipA);
            clipSet.clips.Add(clipB);
            clipSet.clips.Add(clipA);

            bool removeResult = ClipAssetUtility.RemoveClipFromSet(clipSet, clipA);

            Assert.IsTrue(removeResult);
            Assert.AreEqual(1, clipSet.clips.Count);
            Assert.AreEqual(clipB, clipSet.clips[0]);
        }
    }
}
