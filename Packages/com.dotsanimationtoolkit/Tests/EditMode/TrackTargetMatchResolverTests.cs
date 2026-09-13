using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>EditMode coverage of the one tag-aware rule behind "does this track animate this rig target".</summary>
    public sealed class TrackTargetMatchResolverTests
    {
        [Test]
        public void TagBoundTrack_MatchesTargetWearingThatTag_NotByRawId()
        {
            RigTargetDefinition targetWearingTag = new RigTargetDefinition { stableId = 5u, tagId = 7u };
            RigTargetDefinition targetWithRawIdOnly = new RigTargetDefinition { stableId = 99u, tagId = 0u };

            bool bindsByTag = TrackTargetMatchResolver.TrackBindsTarget(99u, 7u, targetWearingTag);
            bool bindsByRawIdDespiteTag = TrackTargetMatchResolver.TrackBindsTarget(99u, 7u, targetWithRawIdOnly);

            Assert.IsTrue(bindsByTag, "A tag-bound track binds the target wearing its tag.");
            Assert.IsFalse(bindsByRawIdDespiteTag, "A tag-bound track never falls back to its raw target id.");
        }

        [Test]
        public void UntaggedTrack_MatchesByRawIdOnly()
        {
            RigTargetDefinition targetWithRawId = new RigTargetDefinition { stableId = 99u, tagId = 7u };
            RigTargetDefinition otherTarget = new RigTargetDefinition { stableId = 5u, tagId = 0u };

            Assert.IsTrue(TrackTargetMatchResolver.TrackBindsTarget(99u, 0u, targetWithRawId));
            Assert.IsFalse(TrackTargetMatchResolver.TrackBindsTarget(99u, 0u, otherTarget));
            Assert.IsFalse(TrackTargetMatchResolver.TrackBindsTarget(99u, 0u, null));
        }
    }
}
