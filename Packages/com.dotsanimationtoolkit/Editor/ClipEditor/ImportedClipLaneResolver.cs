// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    // One read-only timeline row for an imported clip: the keys on one animated node path, normalised against the ClipAsset's own duration.
    public readonly struct ImportedClipLane
    {
        public readonly string nodePath;
        public readonly string displayName;
        public readonly IReadOnlyList<float> normalizedKeyTimes;
        public readonly int keysPastClipEnd;

        public ImportedClipLane(string nodePath, string displayName, IReadOnlyList<float> normalizedKeyTimes, int keysPastClipEnd)
        {
            this.nodePath = nodePath;
            this.displayName = displayName;
            this.normalizedKeyTimes = normalizedKeyTimes;
            this.keysPastClipEnd = keysPastClipEnd;
        }
    }

    /// <summary>Builds read-only timeline lanes for an imported clip's baked curves, keyed by node path and normalised by the ClipAsset's own duration.</summary>
    public static class ImportedClipLaneResolver
    {
        private const float PastEndToleranceSeconds = 1e-4f;
        private const float DeduplicateToleranceNormalized = 1e-4f;

        public static List<ImportedClipLane> Resolve(AnimationClip sourceClip, float clipDurationSeconds)
        {
            List<ImportedClipLane> lanes = new List<ImportedClipLane>();
            if (sourceClip == null || clipDurationSeconds <= 0f)
            {
                return lanes;
            }

            Dictionary<string, List<float>> keyTimesByPath = new Dictionary<string, List<float>>();
            EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(sourceClip);
            for (int bindingIndex = 0; bindingIndex < curveBindings.Length; bindingIndex++)
            {
                EditorCurveBinding curveBinding = curveBindings[bindingIndex];
                AnimationCurve editorCurve = AnimationUtility.GetEditorCurve(sourceClip, curveBinding);
                if (editorCurve == null)
                {
                    continue;
                }

                if (!keyTimesByPath.TryGetValue(curveBinding.path, out List<float> keyTimesForPath))
                {
                    keyTimesForPath = new List<float>();
                    keyTimesByPath.Add(curveBinding.path, keyTimesForPath);
                }

                Keyframe[] keyframes = editorCurve.keys;
                for (int keyframeIndex = 0; keyframeIndex < keyframes.Length; keyframeIndex++)
                {
                    keyTimesForPath.Add(keyframes[keyframeIndex].time);
                }
            }

            foreach (KeyValuePair<string, List<float>> pathEntry in keyTimesByPath.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                List<float> normalizedKeyTimes = new List<float>();
                List<float> pastEndKeyTimes = new List<float>();
                for (int timeIndex = 0; timeIndex < pathEntry.Value.Count; timeIndex++)
                {
                    float keyTime = pathEntry.Value[timeIndex];
                    if (keyTime > clipDurationSeconds + PastEndToleranceSeconds)
                    {
                        pastEndKeyTimes.Add(keyTime);
                        continue;
                    }

                    normalizedKeyTimes.Add(keyTime / clipDurationSeconds);
                }

                normalizedKeyTimes.Sort();
                List<float> deduplicatedNormalizedKeyTimes = Deduplicate(normalizedKeyTimes);

                pastEndKeyTimes.Sort();
                List<float> deduplicatedPastEndKeyTimes = Deduplicate(pastEndKeyTimes);

                string nodePath = pathEntry.Key;
                string displayName = string.IsNullOrEmpty(nodePath) ? sourceClip.name : nodePath.Substring(nodePath.LastIndexOf('/') + 1);

                lanes.Add(new ImportedClipLane(nodePath, displayName, deduplicatedNormalizedKeyTimes, deduplicatedPastEndKeyTimes.Count));
            }

            return lanes;
        }

        private static List<float> Deduplicate(List<float> ascendingValues)
        {
            List<float> deduplicatedValues = new List<float>();
            for (int valueIndex = 0; valueIndex < ascendingValues.Count; valueIndex++)
            {
                float currentValue = ascendingValues[valueIndex];
                if (deduplicatedValues.Count > 0 && Mathf.Abs(currentValue - deduplicatedValues[deduplicatedValues.Count - 1]) <= DeduplicateToleranceNormalized)
                {
                    continue;
                }

                deduplicatedValues.Add(currentValue);
            }

            return deduplicatedValues;
        }
    }
}
