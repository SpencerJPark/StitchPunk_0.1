// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>EditMode coverage of <see cref="ClipSetsPanel"/>'s selection, name field, and picker tick sync.</summary>
    public sealed class ClipSetsPanelTests
    {
        private ClipAsset walkClip;
        private ClipAsset runClip;
        private ClipAsset idleClip;
        private ClipSetAsset clipSetAsset;

        [SetUp]
        public void SetUp()
        {
            walkClip = ScriptableObject.CreateInstance<ClipAsset>();
            runClip = ScriptableObject.CreateInstance<ClipAsset>();
            idleClip = ScriptableObject.CreateInstance<ClipAsset>();

            clipSetAsset = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipSetAsset.clips = new List<ClipAsset> { walkClip, runClip, walkClip };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(walkClip);
            Object.DestroyImmediate(runClip);
            Object.DestroyImmediate(idleClip);
            Object.DestroyImmediate(clipSetAsset);
        }

        [Test]
        public void SelectSet_ShowsTheSetsClipsTicked()
        {
            ClipSetsPanel panel = new ClipSetsPanel();
            panel.LoadCatalog(new List<ClipSetAsset> { clipSetAsset }, new List<ClipAsset> { walkClip, runClip, idleClip });

            panel.SelectSet(clipSetAsset);

            Assert.AreEqual(clipSetAsset, panel.SelectedSet);

            List<ClipAsset> checkedClips = panel.Q<ClipPickerListElement>("clip-picker").CheckedClips.ToList();
            CollectionAssert.AreEquivalent(new List<ClipAsset> { walkClip, runClip }, checkedClips);
        }

        [Test]
        public void SelectSet_PutsTheSetsNameInTheNameField()
        {
            ClipSetsPanel panel = new ClipSetsPanel();
            panel.LoadCatalog(new List<ClipSetAsset> { clipSetAsset }, new List<ClipAsset> { walkClip, runClip, idleClip });

            panel.SelectSet(clipSetAsset);

            Assert.AreEqual(clipSetAsset.name, panel.Q<TextField>("clip-set-name-field").value);
        }
    }
}
