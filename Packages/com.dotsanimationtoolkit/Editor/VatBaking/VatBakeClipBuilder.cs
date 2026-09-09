// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One VAT part's bake list: the source it samples and the clips that feed it.</summary>
    public sealed class VatBakeSourcePlan
    {
        public VatBakeSource Source;
        public List<VatBakeClip> Clips;
    }

    /// <summary>What a bake would do: the parts that have clips, the parts that have none, and the tracks that name nothing.</summary>
    public sealed class VatBakePlan
    {
        public List<VatBakeSourcePlan> Sources;
        public List<string> SkippedPartNames;
        public List<string> UnknownTrackTargets;
        public bool HasAnythingToBake => Sources.Count > 0;
    }

    /// <summary>Works out which clips feed which VAT part of a rig, and which parts and tracks nothing will bake.</summary>
    public static class VatBakeClipBuilder
    {
        public static VatBakePlan Build(ClipSetAsset clipSet, List<VatBakeSource> sources)
        {
            VatBakePlan plan = new VatBakePlan
            {
                Sources = new List<VatBakeSourcePlan>(),
                SkippedPartNames = new List<string>(),
                UnknownTrackTargets = new List<string>()
            };

            List<ClipAsset> clips = clipSet.clips ?? new List<ClipAsset>();

            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                VatBakeSource source = sources[sourceIndex];
                List<VatBakeClip> bakeClips = new List<VatBakeClip>();

                for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
                {
                    ClipAsset clip = clips[clipIndex];
                    if (clip == null)
                    {
                        continue;
                    }

                    VatTrack matchingTrack = FindTargetedTrack(clip, source.TargetId);
                    if (matchingTrack != null)
                    {
                        bakeClips.Add(new VatBakeClip
                        {
                            clipId = clip.Id.Value,
                            targetId = source.TargetId,
                            animationClip = matchingTrack.sourceClip,
                            boneTracks = null,
                            durationSeconds = clip.duration,
                            // The clip's own FPS, so its texture block matches the frames the Clip
                            // Editor rules its timeline into; clips no longer need a shared rate.
                            samplesPerSecond = clip.frameRate,
                            loopSafe = matchingTrack.loopSafe
                        });
                        continue;
                    }

                    int boneTrackCount = clip.boneTracks == null ? 0 : clip.boneTracks.Count;
                    bool hasImportedSource = clip.vatSource != null && clip.vatSource.sourceClip != null;

                    // Authored bone tracks make a clip VAT-bound on their own, so a clip animated
                    // entirely inside the Clip Editor bakes without naming an imported AnimationClip.
                    if (hasImportedSource || boneTrackCount > 0)
                    {
                        bakeClips.Add(new VatBakeClip
                        {
                            clipId = clip.Id.Value,
                            targetId = source.TargetId,
                            animationClip = hasImportedSource ? clip.vatSource.sourceClip : null,
                            boneTracks = clip.boneTracks,
                            durationSeconds = clip.duration,
                            samplesPerSecond = clip.frameRate,
                            // Not gated on hasImportedSource: a clip animated entirely from bone tracks
                            // loops exactly as much as an imported one, and needs the same extra frame.
                            loopSafe = clip.vatSource != null && clip.vatSource.loopSafe
                        });
                    }
                }

                if (bakeClips.Count > 0)
                {
                    plan.Sources.Add(new VatBakeSourcePlan { Source = source, Clips = bakeClips });
                }
                else
                {
                    plan.SkippedPartNames.Add(source.DisplayName);
                }
            }

            AppendUnknownTrackTargets(clips, sources, plan.UnknownTrackTargets);

            return plan;
        }

        private static VatTrack FindTargetedTrack(ClipAsset clip, uint sourceTargetId)
        {
            if (sourceTargetId == 0u || clip.vatTracks == null)
            {
                return null;
            }

            for (int trackIndex = 0; trackIndex < clip.vatTracks.Count; trackIndex++)
            {
                VatTrack track = clip.vatTracks[trackIndex];
                if (track != null && track.targetId == sourceTargetId)
                {
                    return track;
                }
            }
            return null;
        }

        private static void AppendUnknownTrackTargets(List<ClipAsset> clips, List<VatBakeSource> sources, List<string> unknownTrackTargets)
        {
            HashSet<uint> reportedTargetIds = new HashSet<uint>();

            for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                ClipAsset clip = clips[clipIndex];
                if (clip == null || clip.vatTracks == null)
                {
                    continue;
                }

                for (int trackIndex = 0; trackIndex < clip.vatTracks.Count; trackIndex++)
                {
                    VatTrack track = clip.vatTracks[trackIndex];
                    if (track == null || reportedTargetIds.Contains(track.targetId))
                    {
                        continue;
                    }

                    if (!TargetIdHasSource(sources, track.targetId))
                    {
                        reportedTargetIds.Add(track.targetId);
                        unknownTrackTargets.Add(track.targetId.ToString("X"));
                    }
                }
            }
        }

        private static bool TargetIdHasSource(List<VatBakeSource> sources, uint targetId)
        {
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                if (sources[sourceIndex].TargetId == targetId)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
