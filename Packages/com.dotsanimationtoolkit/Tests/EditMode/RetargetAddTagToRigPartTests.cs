// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>A Skipped track becomes Bound once its tag is assigned to the matching rig part, and a tag already worn elsewhere is refused.</summary>
    public sealed class RetargetAddTagToRigPartTests
    {
        private readonly List<Object> createdObjects = new List<Object>();

        [SetUp]
        public void IgnoreUnsavedInstanceWarnings()
        {
            // These rigs are CreateInstance copies with no asset path, so the SaveAssetIfDirty
            // inside the write can warn; the assertions here do not depend on it.
            LogAssert.ignoreFailingMessages = true;
        }

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
        public void AddTagToUntaggedPart_SkippedTrackBecomesBound()
        {
            TargetTagRegistry registry = CreateRegistryWithTag("Hand_L", 7u);
            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();
            rig.targets.Add(new RigTargetDefinition { displayName = "Hand_L_Part", stableId = 5u, tagId = 0u });
            createdObjects.Add(rig);
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.transformTracks.Add(new TransformTrack { targetId = 1u, tagId = 7u });
            createdObjects.Add(clip);

            List<TrackBinding> bindingsBefore = RetargetBindingResolver.Resolve(clip, rig, registry);
            Assert.AreEqual(1, bindingsBefore.Count);
            Assert.AreEqual(TrackBindingState.Skipped, bindingsBefore[0].state);

            bool didAddTag = RetargetRemapEditing.AddTagToRigPart(rig, 5u, 7u, out string failureMessage);
            Assert.IsTrue(didAddTag);
            Assert.IsEmpty(failureMessage);

            List<TrackBinding> bindingsAfter = RetargetBindingResolver.Resolve(clip, rig, registry);
            Assert.AreEqual(TrackBindingState.Bound, bindingsAfter[0].state);
            Assert.AreEqual(1, RetargetBindingResolver.CountBound(bindingsAfter));
        }

        [Test]
        public void AddTagWornByAnotherPart_IsRefused()
        {
            TargetTagRegistry registry = CreateRegistryWithTag("Hand_L", 7u);
            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();
            rig.targets.Add(new RigTargetDefinition { displayName = "Torso", stableId = 5u, tagId = 7u });
            rig.targets.Add(new RigTargetDefinition { displayName = "Hand_L_Part", stableId = 6u, tagId = 0u });
            createdObjects.Add(rig);

            bool didAddTag = RetargetRemapEditing.AddTagToRigPart(rig, 6u, 7u, out string failureMessage);

            Assert.IsFalse(didAddTag);
            Assert.IsNotEmpty(failureMessage);
            Assert.AreEqual(0u, rig.targets[1].tagId);
            Assert.AreEqual(7u, rig.targets[0].tagId);
        }

        private TargetTagRegistry CreateRegistryWithTag(string tagName, uint tagId)
        {
            TargetTagRegistry registry = ScriptableObject.CreateInstance<TargetTagRegistry>();
            registry.entries.Add(new TargetTagEntry { name = tagName, stableId = tagId });
            createdObjects.Add(registry);
            return registry;
        }
    }
}
