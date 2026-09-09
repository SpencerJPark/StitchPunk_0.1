// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Target add/remove/tag behaviour of <see cref="RigAssetUtility"/> on an in-memory rig.</summary>
    public sealed class RigAssetUtilityTests
    {
        private RigAsset rig;

        [SetUp]
        public void SetUp()
        {
            rig = ScriptableObject.CreateInstance<RigAsset>();
            // An unsaved instance has no asset path, so SaveAssetIfDirty inside the utility is a
            // no-op that can log a warning; the assertions under test do not depend on it.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            Object.DestroyImmediate(rig);
        }

        [Test]
        public void AddTargetToRig_MintsAStableId_AndRefusesADuplicatePath()
        {
            RigTargetDefinition addedTarget = RigAssetUtility.AddTargetToRig(rig, "Root/Torso", "Torso");

            Assert.IsNotNull(addedTarget);
            Assert.AreNotEqual(0u, addedTarget.Id.Value);
            Assert.AreEqual(1, rig.targets.Count);

            RigTargetDefinition duplicateTarget = RigAssetUtility.AddTargetToRig(rig, "Root/Torso", "Torso Again");

            Assert.IsNull(duplicateTarget);
            Assert.AreEqual(1, rig.targets.Count);
        }

        [Test]
        public void RemoveTargetFromRig_RemovesOnlyTheMatchingId()
        {
            RigTargetDefinition firstTarget = RigAssetUtility.AddTargetToRig(rig, "Root/Torso", "Torso");
            RigTargetDefinition secondTarget = RigAssetUtility.AddTargetToRig(rig, "Root/Head", "Head");

            bool removeResult = RigAssetUtility.RemoveTargetFromRig(rig, secondTarget.Id.Value);

            Assert.IsTrue(removeResult);
            Assert.AreEqual(1, rig.targets.Count);
            Assert.AreEqual(firstTarget, rig.targets[0]);

            bool removeMissingResult = RigAssetUtility.RemoveTargetFromRig(rig, secondTarget.Id.Value);

            Assert.IsFalse(removeMissingResult);
        }

        [Test]
        public void SetTargetTag_WritesTheTag_AndZeroIsLegal()
        {
            RigTargetDefinition addedTarget = RigAssetUtility.AddTargetToRig(rig, "Root/Torso", "Torso");

            bool setTagResult = RigAssetUtility.SetTargetTag(rig, addedTarget.Id.Value, 0xABCDu);

            Assert.IsTrue(setTagResult);
            Assert.AreEqual(0xABCDu, addedTarget.tagId);

            bool clearTagResult = RigAssetUtility.SetTargetTag(rig, addedTarget.Id.Value, 0u);

            Assert.IsTrue(clearTagResult);
            Assert.AreEqual(0u, addedTarget.tagId);
        }

        [Test]
        public void RenameRig_RefusesTheCasesThatCannotRename()
        {
            // An unsaved instance has no asset path, so this can never reach AssetDatabase.RenameAsset.
            Assert.IsFalse(RigAssetUtility.RenameRig(rig, "Whatever"));
            Assert.IsFalse(RigAssetUtility.RenameRig(null, "Whatever"));
            Assert.IsFalse(RigAssetUtility.RenameRig(rig, "   "));
            Assert.IsFalse(RigAssetUtility.RenameRig(rig, rig.name));
        }

        [Test]
        public void DeleteRig_RefusesTheCasesThatCannotDelete()
        {
            Assert.IsFalse(RigAssetUtility.DeleteRig(null));

            RigAsset unsavedRig = ScriptableObject.CreateInstance<RigAsset>();
            Assert.IsFalse(RigAssetUtility.DeleteRig(unsavedRig));
            Object.DestroyImmediate(unsavedRig);
        }
    }
}
