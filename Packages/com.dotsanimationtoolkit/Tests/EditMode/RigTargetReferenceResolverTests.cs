using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>EditMode coverage of <see cref="RigTargetReferenceResolver"/>'s tag-vs-target-id matching and clip-name summarisation.</summary>
    public sealed class RigTargetReferenceResolverTests
    {
        private readonly List<ClipAsset> createdClips = new List<ClipAsset>();

        [TearDown]
        public void TearDown()
        {
            foreach (ClipAsset createdClip in createdClips)
            {
                if (createdClip != null)
                {
                    Object.DestroyImmediate(createdClip);
                }
            }

            createdClips.Clear();
        }

        private ClipAsset CreateClipWithTransformTrack(string clipName, uint targetId, uint tagId)
        {
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.name = clipName;
            TransformTrack track = new TransformTrack { targetId = targetId, tagId = tagId };
            clip.transformTracks.Add(track);
            createdClips.Add(clip);
            return clip;
        }

        [Test]
        public void FindClipsBoundToTarget_MatchesByTag_ThenByTargetId()
        {
            ClipAsset walkClip = CreateClipWithTransformTrack("walk", 0, 7);
            ClipAsset runClip = CreateClipWithTransformTrack("run", 42, 0);
            ClipAsset idleClip = CreateClipWithTransformTrack("idle", 999, 0);

            List<ClipAsset> clips = new List<ClipAsset> { walkClip, runClip, idleClip };

            List<ClipAsset> matchingClips = RigTargetReferenceResolver.FindClipsBoundToTarget(clips, 42, 7);

            Assert.AreEqual(2, matchingClips.Count);
            Assert.AreSame(walkClip, matchingClips[0]);
            Assert.AreSame(runClip, matchingClips[1]);
        }

        [Test]
        public void FindClipsBoundToTarget_UntaggedTarget_DoesNotSweepUpEveryUntaggedTrack()
        {
            ClipAsset otherClip = CreateClipWithTransformTrack("other", 99, 0);

            List<ClipAsset> clips = new List<ClipAsset> { otherClip };

            List<ClipAsset> matchingClips = RigTargetReferenceResolver.FindClipsBoundToTarget(clips, 42, 0);

            Assert.AreEqual(0, matchingClips.Count);
        }

        [Test]
        public void FindClipsBoundToTarget_ClipMatchingOnBothTrackTypes_AppearsOnce()
        {
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.name = "both";
            clip.transformTracks.Add(new TransformTrack { targetId = 42, tagId = 0 });
            clip.spriteTracks.Add(new SpriteTrack { targetId = 42, tagId = 0 });
            createdClips.Add(clip);

            List<ClipAsset> clips = new List<ClipAsset> { clip };

            List<ClipAsset> matchingClips = RigTargetReferenceResolver.FindClipsBoundToTarget(clips, 42, 0);

            Assert.AreEqual(1, matchingClips.Count);
        }

        [Test]
        public void DescribeClips_NamesAtMostThree()
        {
            ClipAsset clipA = CreateClipWithTransformTrack("A", 1, 0);

            Assert.AreEqual("A", RigTargetReferenceResolver.DescribeClips(new List<ClipAsset> { clipA }));
            Assert.AreEqual(string.Empty, RigTargetReferenceResolver.DescribeClips(new List<ClipAsset>()));

            ClipAsset clipB = CreateClipWithTransformTrack("B", 2, 0);
            ClipAsset clipC = CreateClipWithTransformTrack("C", 3, 0);
            ClipAsset clipD = CreateClipWithTransformTrack("D", 4, 0);
            ClipAsset clipE = CreateClipWithTransformTrack("E", 5, 0);

            List<ClipAsset> fiveClips = new List<ClipAsset> { clipA, clipB, clipC, clipD, clipE };

            Assert.AreEqual("A, B, C and 2 more", RigTargetReferenceResolver.DescribeClips(fiveClips));
        }
    }
}
