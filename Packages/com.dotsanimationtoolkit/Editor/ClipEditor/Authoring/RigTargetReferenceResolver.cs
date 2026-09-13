using System.Collections.Generic;
using System.Text;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Finds which clips bind to a given rig target, for the Rigs tab's delete/rename confirmation flow.</summary>
    public static class RigTargetReferenceResolver
    {
        /// <summary>Every clip carrying a transform or sprite track bound to this target, by tag when tagId is non-zero or by target id otherwise. Never null; ordered as clips was, each clip at most once.</summary>
        public static List<ClipAsset> FindClipsBoundToTarget(IReadOnlyList<ClipAsset> clips, uint targetStableId, uint tagId)
        {
            List<ClipAsset> matchingClips = new List<ClipAsset>();

            if (clips == null)
            {
                return matchingClips;
            }

            for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                ClipAsset candidateClip = clips[clipIndex];

                if (candidateClip == null)
                {
                    continue;
                }

                if (ClipHasTrackBoundToTarget(candidateClip, targetStableId, tagId))
                {
                    matchingClips.Add(candidateClip);
                }
            }

            return matchingClips;
        }

        /// <summary>"Walk, Run and 2 more" — the clip names for the confirm dialog, at most three named. Empty string for an empty list.</summary>
        public static string DescribeClips(IReadOnlyList<ClipAsset> clips)
        {
            if (clips == null || clips.Count == 0)
            {
                return string.Empty;
            }

            int namedCount = clips.Count < 3 ? clips.Count : 3;
            string[] namedClips = new string[namedCount];

            for (int clipIndex = 0; clipIndex < namedCount; clipIndex++)
            {
                ClipAsset clip = clips[clipIndex];
                namedClips[clipIndex] = clip == null || string.IsNullOrEmpty(clip.name) ? "(unnamed)" : clip.name;
            }

            if (clips.Count == 1)
            {
                return namedClips[0];
            }

            if (clips.Count <= 3)
            {
                StringBuilder exactListBuilder = new StringBuilder();
                for (int nameIndex = 0; nameIndex < namedClips.Length - 1; nameIndex++)
                {
                    if (nameIndex > 0)
                    {
                        exactListBuilder.Append(", ");
                    }

                    exactListBuilder.Append(namedClips[nameIndex]);
                }

                exactListBuilder.Append(" and ").Append(namedClips[namedClips.Length - 1]);
                return exactListBuilder.ToString();
            }

            int remainingCount = clips.Count - 3;
            return string.Format("{0}, {1}, {2} and {3} more", namedClips[0], namedClips[1], namedClips[2], remainingCount);
        }

        private static bool ClipHasTrackBoundToTarget(ClipAsset clip, uint targetStableId, uint tagId)
        {
            List<TransformTrack> transformTracks = clip.transformTracks;

            if (transformTracks != null)
            {
                for (int trackIndex = 0; trackIndex < transformTracks.Count; trackIndex++)
                {
                    TransformTrack track = transformTracks[trackIndex];

                    if (track != null && TrackMatchesTarget(track.targetId, track.tagId, targetStableId, tagId))
                    {
                        return true;
                    }
                }
            }

            List<SpriteTrack> spriteTracks = clip.spriteTracks;

            if (spriteTracks != null)
            {
                for (int trackIndex = 0; trackIndex < spriteTracks.Count; trackIndex++)
                {
                    SpriteTrack track = spriteTracks[trackIndex];

                    if (track != null && TrackMatchesTarget(track.targetId, track.tagId, targetStableId, tagId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TrackMatchesTarget(uint trackTargetId, uint trackTagId, uint targetStableId, uint tagId)
        {
            return TrackTargetMatchResolver.TrackBindsTarget(trackTargetId, trackTagId, targetStableId, tagId);
        }
    }
}
