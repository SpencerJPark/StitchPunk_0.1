// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class ClipPreviewRegistryTests
    {
        private AuthoringTestAssets authoringAssets;
        private ClipPreviewController controller;

        [SetUp]
        public void CreateFixture()
        {
            authoringAssets = new AuthoringTestAssets();
            controller = new ClipPreviewController();
        }

        [TearDown]
        public void DestroyFixture()
        {
            controller.Dispose();
            authoringAssets.DestroyAll();
        }

        [Test]
        public void Refresh_TargetlessRigWithABoneTrackSet_BuildsARegistryHoldingTheClipAndSaysNothing()
        {
            RigAsset rig = authoringAssets.CreateRig("Rig", 1UL, new uint[0]);
            ClipAsset clip = authoringAssets.CreateClip("Clip", 2UL, 1f);
            clip.boneTracks.Add(new BoneTrack
            {
                boneName = "Hips",
                keys =
                {
                    new BoneKey { normalizedTime = 0f, interpolation = Interpolation.Linear },
                    new BoneKey { normalizedTime = 1f, interpolation = Interpolation.Linear }
                }
            });
            ClipSetAsset set = authoringAssets.CreateSet("Set", rig, 3UL, clip);

            controller.SetRig(rig);
            controller.SetClipSet(set);

            Assert.IsTrue(controller.HasRegistry);
            Assert.IsTrue(controller.IsClipInRegistry(clip.Id.Value));
            Assert.IsEmpty(controller.StatusMessage);
        }
    }
}
