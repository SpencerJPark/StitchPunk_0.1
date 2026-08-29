// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// The two data operations behind amendment A56's timeline binding surface: merging one track's
    /// keys into another (a wrong merge destroys authored keys), and tagging a part at track
    /// creation (a wrong reuse breaks rule T1's one-tag-per-rig).
    /// </summary>
    public sealed class TimelineBindingLogicTests
    {
        private const uint FirstTargetId = 0x11u;
        private const uint SecondTargetId = 0x22u;

        private AuthoringTestAssets assets;
        private RigAsset rig;
        private TargetTagRegistry registry;

        [SetUp]
        public void SetUp()
        {
            assets = new AuthoringTestAssets();
            rig = assets.CreateRig("Rig", 1uL, 1, new uint[] { FirstTargetId, SecondTargetId });
            registry = ScriptableObject.CreateInstance<TargetTagRegistry>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(registry);
            assets.DestroyAll();
        }

        private static TransformKey KeyAt(float normalizedTime, float positionX)
        {
            return new TransformKey
            {
                normalizedTime = normalizedTime,
                position = new float3(positionX, 0f, 0f),
                scale = new float3(1f, 1f, 1f)
            };
        }

        [Test]
        public void MergeTransformTracks_IncomingKeyWinsCollisions_AndChannelsUnion()
        {
            TransformTrack destination = new TransformTrack
            {
                targetId = FirstTargetId,
                channels = AnimatedChannels.PositionXY,
                keys = new List<TransformKey> { KeyAt(0f, 1f), KeyAt(0.5f, 2f) }
            };
            TransformTrack source = new TransformTrack
            {
                targetId = SecondTargetId,
                channels = AnimatedChannels.Rotation,
                keys = new List<TransformKey> { KeyAt(0.5f, 9f), KeyAt(0.25f, 3f) }
            };

            ClipComponentModel.MergeTransformTracks(source, destination);

            Assert.AreEqual(
                AnimatedChannels.PositionXY | AnimatedChannels.Rotation, destination.channels);
            Assert.AreEqual(3, destination.keys.Count, "0, 0.25 and one winner at 0.5.");
            Assert.AreEqual(0f, destination.keys[0].normalizedTime);
            Assert.AreEqual(0.25f, destination.keys[1].normalizedTime);
            Assert.AreEqual(0.5f, destination.keys[2].normalizedTime);
            Assert.AreEqual(
                9f, destination.keys[2].position.x,
                "On a same-time collision the moved key wins — the gesture was 'put these there'.");
        }

        [Test]
        public void EnsureTargetTagged_ReusesTheNameMatch_ThenMintsASuffixWhenItIsWorn()
        {
            uint existingTagId = registry.CreateVocabularyEntry("Target0");

            bool createdRegistryEntry;
            uint firstAssignedTagId = ClipComponentModel.EnsureTargetTagged(
                rig, FirstTargetId, registry, out createdRegistryEntry);

            Assert.AreEqual(existingTagId, firstAssignedTagId, "Same name, unworn: reuse it.");
            Assert.IsFalse(createdRegistryEntry);
            Assert.AreEqual(existingTagId, rig.targets[0].tagId);

            // A second part with the same display name cannot reuse the worn tag (rule T1).
            rig.targets[1].displayName = "Target0";
            uint secondAssignedTagId = ClipComponentModel.EnsureTargetTagged(
                rig, SecondTargetId, registry, out createdRegistryEntry);

            Assert.IsTrue(createdRegistryEntry);
            Assert.AreNotEqual(existingTagId, secondAssignedTagId);
            Assert.AreEqual("Target0 2", registry.FindName(secondAssignedTagId));
            Assert.AreEqual(secondAssignedTagId, rig.targets[1].tagId);
        }
    }
}
