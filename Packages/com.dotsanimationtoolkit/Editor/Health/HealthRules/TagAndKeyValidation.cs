// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Health rules for vocabulary references: track tags and event keys missing from their registries, and clips that pose nothing on a profile's rig.</summary>
    public static class TagAndKeyValidation
    {
        public static void EvaluateTrackTagsNotInRegistry(HealthScanContext context, List<HealthFinding> output)
        {
            if (context.targetTags == null)
            {
                return;
            }

            foreach (ClipAsset clip in context.clips)
            {
                if (clip == null)
                {
                    continue;
                }

                List<ValidationMessage> clipMessages = ClipValidation.ValidateClip(clip, context.targetTags, null);
                foreach (ValidationMessage clipMessage in clipMessages)
                {
                    if (clipMessage.code != ValidationCode.V36)
                    {
                        continue;
                    }

                    HealthFinding finding = new HealthFinding();
                    finding.severity = HealthSeverity.Error;
                    finding.code = HealthFinding.TrackTagNotInRegistryCode;
                    finding.message = clipMessage.text;
                    finding.target = clip;
                    output.Add(finding);
                }
            }
        }

        public static void EvaluateEventKeysNotInRegistry(HealthScanContext context, List<HealthFinding> output)
        {
            if (context.eventKeys == null)
            {
                return;
            }

            Func<uint, bool> registryContainsKey = AnimEventValidation.RegistryContainsKey(context.eventKeys);
            List<uint> keysInFirstSeenOrder = new List<uint>();
            Dictionary<uint, List<ClipAsset>> clipsUsingKey = new Dictionary<uint, List<ClipAsset>>();

            foreach (ClipAsset clip in context.clips)
            {
                if (clip == null || clip.events == null)
                {
                    continue;
                }

                foreach (EventMarker eventMarker in clip.events)
                {
                    uint eventKey = eventMarker.eventKey;
                    if (eventKey < (uint)ReservedEventKeys.FirstUserKey || registryContainsKey(eventKey))
                    {
                        continue;
                    }

                    List<ClipAsset> clipsForKey;
                    if (!clipsUsingKey.TryGetValue(eventKey, out clipsForKey))
                    {
                        clipsForKey = new List<ClipAsset>();
                        clipsUsingKey.Add(eventKey, clipsForKey);
                        keysInFirstSeenOrder.Add(eventKey);
                    }

                    if (!clipsForKey.Contains(clip))
                    {
                        clipsForKey.Add(clip);
                    }
                }
            }

            foreach (uint eventKey in keysInFirstSeenOrder)
            {
                List<ClipAsset> clipsForKey = clipsUsingKey[eventKey];
                List<string> quotedClipNames = new List<string>();
                foreach (ClipAsset clipForKey in clipsForKey)
                {
                    quotedClipNames.Add("'" + clipForKey.name + "'");
                }

                HealthFinding finding = new HealthFinding();
                finding.severity = HealthSeverity.Error;
                finding.code = HealthFinding.EventKeyNotInRegistryCode;
                finding.message = "Event key 0x" + eventKey.ToString("X8") + " is used by " + clipsForKey.Count +
                    " clip(s) but is not in the event keys registry: " + string.Join(", ", quotedClipNames);
                finding.target = clipsForKey[0];
                output.Add(finding);
            }
        }

        public static void EvaluateClipsPosingNothingOnRig(HealthScanContext context, List<HealthFinding> output)
        {
            if (context.profiles == null)
            {
                return;
            }

            HashSet<(RigAsset, ClipAsset)> reportedPairs = new HashSet<(RigAsset, ClipAsset)>();

            foreach (ActorProfileAsset profile in context.profiles)
            {
                if (profile == null || profile.rig == null || profile.clipSets == null)
                {
                    continue;
                }

                RigAsset rig = profile.rig;
                HashSet<uint> tagsWithRigTarget = new HashSet<uint>();
                if (rig.targets != null)
                {
                    foreach (RigTargetDefinition rigTarget in rig.targets)
                    {
                        if (rigTarget != null && rigTarget.tagId != 0)
                        {
                            tagsWithRigTarget.Add(rigTarget.tagId);
                        }
                    }
                }

                foreach (ClipSetAsset clipSet in profile.clipSets)
                {
                    if (clipSet == null || clipSet.clips == null)
                    {
                        continue;
                    }

                    foreach (ClipAsset clip in clipSet.clips)
                    {
                        if (clip == null)
                        {
                            continue;
                        }

                        HashSet<uint> clipTrackTags = new HashSet<uint>();
                        if (clip.transformTracks != null)
                        {
                            foreach (TransformTrack transformTrack in clip.transformTracks)
                            {
                                if (transformTrack != null && transformTrack.tagId != 0)
                                {
                                    clipTrackTags.Add(transformTrack.tagId);
                                }
                            }
                        }

                        if (clip.spriteTracks != null)
                        {
                            foreach (SpriteTrack spriteTrack in clip.spriteTracks)
                            {
                                if (spriteTrack != null && spriteTrack.tagId != 0)
                                {
                                    clipTrackTags.Add(spriteTrack.tagId);
                                }
                            }
                        }

                        if (clipTrackTags.Count == 0)
                        {
                            continue;
                        }

                        bool rigHasMatchingTarget = false;
                        foreach (uint clipTrackTag in clipTrackTags)
                        {
                            if (tagsWithRigTarget.Contains(clipTrackTag))
                            {
                                rigHasMatchingTarget = true;
                                break;
                            }
                        }

                        if (rigHasMatchingTarget)
                        {
                            continue;
                        }

                        if (!reportedPairs.Add((rig, clip)))
                        {
                            continue;
                        }

                        HealthFinding finding = new HealthFinding();
                        finding.severity = HealthSeverity.Warning;
                        finding.code = HealthFinding.ClipPosesNothingOnRigCode;
                        finding.message = "Clip '" + clip.name + "' in clip set '" + clipSet.name +
                            "' uses only tags that rig '" + rig.name + "' has no target for, so it poses " +
                            "nothing on profile '" + profile.name + "'.";
                        finding.target = clip;
                        finding.secondaryTarget = rig;
                        output.Add(finding);
                    }
                }
            }
        }
    }
}
