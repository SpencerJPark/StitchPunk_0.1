// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>EditMode coverage of <see cref="ClipPickerModel"/>'s search filter and tick-set retention across <see cref="ClipPickerModel.SetEntries"/>.</summary>
    public sealed class ClipPickerModelTests
    {
        private ClipAsset walkClip;
        private ClipAsset runClip;
        private ClipAsset idleClip;
        private ClipAsset jumpClip;

        [TearDown]
        public void TearDown()
        {
            if (walkClip != null)
            {
                Object.DestroyImmediate(walkClip);
            }

            if (runClip != null)
            {
                Object.DestroyImmediate(runClip);
            }

            if (idleClip != null)
            {
                Object.DestroyImmediate(idleClip);
            }

            if (jumpClip != null)
            {
                Object.DestroyImmediate(jumpClip);
            }
        }

        [Test]
        public void SearchText_FiltersByNameOrFolder_CaseInsensitive()
        {
            walkClip = ScriptableObject.CreateInstance<ClipAsset>();
            runClip = ScriptableObject.CreateInstance<ClipAsset>();
            idleClip = ScriptableObject.CreateInstance<ClipAsset>();

            ClipPickerModel model = new ClipPickerModel();
            model.SetEntries(new[]
            {
                new ClipPickerEntry { Clip = walkClip, Name = "Walk", FolderPath = "Assets/A" },
                new ClipPickerEntry { Clip = runClip, Name = "Run", FolderPath = "Assets/B" },
                new ClipPickerEntry { Clip = idleClip, Name = "Idle", FolderPath = "Assets/walkcycle" },
            });

            model.SearchText = "WALK";

            Assert.AreEqual(2, model.VisibleEntries.Count);
            bool hasWalk = false;
            bool hasIdle = false;
            foreach (ClipPickerEntry entry in model.VisibleEntries)
            {
                if (entry.Name == "Walk")
                {
                    hasWalk = true;
                }

                if (entry.Name == "Idle")
                {
                    hasIdle = true;
                }
            }

            Assert.IsTrue(hasWalk, "Walk must be visible — name match.");
            Assert.IsTrue(hasIdle, "Idle must be visible — folder path match.");
        }

        [Test]
        public void SetEntries_KeepsTicksForClipsStillPresent()
        {
            walkClip = ScriptableObject.CreateInstance<ClipAsset>();
            jumpClip = ScriptableObject.CreateInstance<ClipAsset>();

            ClipPickerModel model = new ClipPickerModel();
            model.SetEntries(new[]
            {
                new ClipPickerEntry { Clip = walkClip, Name = "Walk", FolderPath = "Assets/A" },
            });
            model.SetCheckedState(walkClip, true);

            model.SetEntries(new[]
            {
                new ClipPickerEntry { Clip = walkClip, Name = "Walk", FolderPath = "Assets/A" },
                new ClipPickerEntry { Clip = jumpClip, Name = "Jump", FolderPath = "Assets/A" },
            });

            Assert.IsTrue(model.IsChecked(walkClip));
            Assert.AreEqual(1, model.CheckedCount);

            model.ShowCheckedOnly = true;

            Assert.AreEqual(1, model.VisibleEntries.Count);
            Assert.AreEqual("Walk", model.VisibleEntries[0].Name);
        }
    }
}
