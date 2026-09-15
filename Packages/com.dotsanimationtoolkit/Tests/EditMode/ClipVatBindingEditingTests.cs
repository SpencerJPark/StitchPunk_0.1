// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <see cref="ClipVatBindingEditing"/>'s read/write split between
    /// <see cref="ClipAsset.vatSource"/> and <see cref="ClipAsset.vatTracks"/>.
    /// </summary>
    public sealed class ClipVatBindingEditingTests
    {
        private ClipAsset clip;
        private AnimationClip firstSourceClip;
        private AnimationClip secondSourceClip;

        [SetUp]
        public void SetUp()
        {
            clip = ScriptableObject.CreateInstance<ClipAsset>();
            firstSourceClip = new AnimationClip();
            secondSourceClip = new AnimationClip();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(firstSourceClip);
            Object.DestroyImmediate(secondSourceClip);
        }

        [Test]
        public void SetSourceClip_TargetedWritesOneVatTrackRow_UntargetedWritesVatSource()
        {
            ClipVatBindingEditing.SetSourceClip(clip, 7, firstSourceClip);

            Assert.AreEqual(1, clip.vatTracks.Count);
            Assert.AreEqual(7u, clip.vatTracks[0].targetId);
            Assert.AreEqual(firstSourceClip, clip.vatTracks[0].sourceClip);
            Assert.IsNull(clip.vatSource);

            ClipVatBindingEditing.SetSourceClip(clip, 0, secondSourceClip);

            Assert.IsNotNull(clip.vatSource);
            Assert.AreEqual(secondSourceClip, clip.vatSource.sourceClip);
            Assert.AreEqual(1, clip.vatTracks.Count);

            ClipVatBindingEditing.SetSourceClip(clip, 7, null);

            Assert.AreEqual(0, clip.vatTracks.Count);
            Assert.AreEqual(secondSourceClip, clip.vatSource.sourceClip);
        }
    }
}
