// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Static-method behaviour of <see cref="ClipSetSaveLocation"/> — no EditorPrefs writes.</summary>
    public sealed class ClipSetSaveLocationTests
    {
        [Test]
        public void TryMakeProjectRelative_AcceptsAssetsSubfolders_AndRefusesOutsiders()
        {
            bool nestedFolderResult = ClipSetSaveLocation.TryMakeProjectRelative(
                "C:/Proj/Assets/Anim/Sets", "C:/Proj/Assets", out string nestedFolderRelativePath);
            Assert.IsTrue(nestedFolderResult);
            Assert.AreEqual("Assets/Anim/Sets", nestedFolderRelativePath);

            bool exactFolderResult = ClipSetSaveLocation.TryMakeProjectRelative(
                "C:\\Proj\\Assets", "C:/Proj/Assets", out string exactFolderRelativePath);
            Assert.IsTrue(exactFolderResult);
            Assert.AreEqual("Assets", exactFolderRelativePath);

            bool foreignRootResult = ClipSetSaveLocation.TryMakeProjectRelative(
                "C:/Other/Assets/X", "C:/Proj/Assets", out string foreignRootRelativePath);
            Assert.IsFalse(foreignRootResult);
            Assert.IsNull(foreignRootRelativePath);

            bool boundaryResult = ClipSetSaveLocation.TryMakeProjectRelative(
                "C:/Proj/AssetsBackup/X", "C:/Proj/Assets", out string boundaryRelativePath);
            Assert.IsFalse(boundaryResult);
            Assert.IsNull(boundaryRelativePath);
        }

        [Test]
        public void SanitizeAssetName_StripsInvalidCharacters_AndFallsBackWhenEmpty()
        {
            Assert.AreEqual("Walk v2", ClipSetSaveLocation.SanitizeAssetName("Walk: v2?"));
            Assert.AreEqual("NewClipSet", ClipSetSaveLocation.SanitizeAssetName("   "));
        }
    }
}
