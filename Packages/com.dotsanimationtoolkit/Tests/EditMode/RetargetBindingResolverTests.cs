// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>The Retarget tab's two miss states must stay distinct, matching the bind validator's warning and error.</summary>
    public sealed class RetargetBindingResolverTests
    {
        private readonly List<Object> createdObjects = new List<Object>();

        [TearDown]
        public void DestroyCreatedObjects()
        {
            foreach (Object createdObject in createdObjects)
            {
                if (createdObject != null)
                {
                    Object.DestroyImmediate(createdObject);
                }
            }
            createdObjects.Clear();
        }

        [Test]
        public void TagOnRegistryButNotRig_IsSkipped_NotDangling()
        {
            TargetTagRegistry registry = CreateRegistryWithTag("Hand_L", 7u);
            RigAsset rigWithoutHandTag = CreateRigWithTaggedTarget("Torso", 5u, 3u);
            ClipAsset clip = CreateClipWithTransformTrackTag(7u);

            List<TrackBinding> bindings = RetargetBindingResolver.Resolve(clip, rigWithoutHandTag, registry);

            Assert.AreEqual(1, bindings.Count);
            Assert.AreEqual(TrackBindingState.Skipped, bindings[0].state);
            Assert.AreEqual("Hand_L", bindings[0].trackName);
            Assert.AreEqual(0, RetargetBindingResolver.CountBound(bindings));
        }

        [Test]
        public void TagNotInRegistry_IsDangling()
        {
            TargetTagRegistry registry = CreateRegistryWithTag("Hand_L", 7u);
            // The rig still wears the retired tag: dangling must win over a rig-side match.
            RigAsset rigWearingRetiredTag = CreateRigWithTaggedTarget("Jaw", 5u, 9u);
            ClipAsset clip = CreateClipWithTransformTrackTag(9u);

            List<TrackBinding> bindings = RetargetBindingResolver.Resolve(clip, rigWearingRetiredTag, registry);

            Assert.AreEqual(1, bindings.Count);
            Assert.AreEqual(TrackBindingState.Dangling, bindings[0].state);
            Assert.AreEqual(0, RetargetBindingResolver.CountBound(bindings));
        }

        private TargetTagRegistry CreateRegistryWithTag(string tagName, uint tagId)
        {
            TargetTagRegistry registry = ScriptableObject.CreateInstance<TargetTagRegistry>();
            registry.entries.Add(new TargetTagEntry { name = tagName, stableId = tagId });
            createdObjects.Add(registry);
            return registry;
        }

        private RigAsset CreateRigWithTaggedTarget(string displayName, uint targetStableId, uint tagId)
        {
            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();
            rig.name = "ScratchRig";
            rig.targets.Add(new RigTargetDefinition { displayName = displayName, stableId = targetStableId, tagId = tagId });
            createdObjects.Add(rig);
            return rig;
        }

        private ClipAsset CreateClipWithTransformTrackTag(uint tagId)
        {
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.transformTracks.Add(new TransformTrack { targetId = 1u, tagId = tagId });
            createdObjects.Add(clip);
            return clip;
        }
    }
}
