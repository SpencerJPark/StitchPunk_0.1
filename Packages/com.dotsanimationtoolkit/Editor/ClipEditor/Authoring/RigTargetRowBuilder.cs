// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One row of the New Rig / Rig Targets list: a prefab node merged with the rig target it may or may not already be.</summary>
    public sealed class RigTargetRow
    {
        public string SourceNodePath;
        public string DisplayName;
        public uint TargetStableId;
        public uint TagId;
        public bool IsTarget;
        public bool IsMissingNode;
        public bool PreTicked;
    }

    /// <summary>Merges "what the prefab has" with "what the rig claims" into one ordered row list for the New Rig and Rig Targets panels.</summary>
    public static class RigTargetRowBuilder
    {
        /// <summary>Rows for create mode: one per renderer-bearing node, in hierarchy order, pre-ticked per the existing rule.</summary>
        public static List<RigTargetRow> BuildForNewRig(GameObject sourcePrefab)
        {
            List<RigTargetRow> rows = new List<RigTargetRow>();
            if (sourcePrefab == null)
            {
                return rows;
            }

            Renderer[] renderers = sourcePrefab.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Transform rendererTransform = renderer.transform;

                string nodePath = PrefabAuthoringBridge.GetHierarchyPath(rendererTransform, sourcePrefab.transform);
                if (string.IsNullOrEmpty(nodePath))
                {
                    // A renderer directly on the prefab root has no path distinct from
                    // RigTargetDefinition.sourceNodePath's own "unbound" convention (empty means
                    // not tied to a node). Skipped rather than emitted as a target nothing could
                    // tell apart from one with no binding at all.
                    continue;
                }

                // Pre-ticked when the renderer looks like something the author actually wants
                // shown — enabled and on an active node. A disabled renderer or an inactive helper
                // object (an alternate LOD, a debug visualization) is offered but left unticked,
                // rather than forcing every candidate on and making the list something to prune.
                bool preTicked = renderer.enabled && rendererTransform.gameObject.activeSelf;

                rows.Add(new RigTargetRow
                {
                    SourceNodePath = nodePath,
                    DisplayName = rendererTransform.name,
                    TargetStableId = 0u,
                    TagId = 0u,
                    IsTarget = false,
                    IsMissingNode = false,
                    PreTicked = preTicked,
                });
            }

            return rows;
        }

        /// <summary>Rows for edit mode: every renderer-bearing node of the rig's prefab (ticked when the rig has a target for it), then one missing row per target the prefab cannot account for.</summary>
        public static List<RigTargetRow> BuildForRig(RigAsset rig)
        {
            List<RigTargetRow> rows = new List<RigTargetRow>();
            if (rig == null || rig.sourcePrefab == null)
            {
                return rows;
            }

            GameObject sourcePrefab = rig.sourcePrefab;
            HashSet<RigTargetDefinition> matchedTargets = new HashSet<RigTargetDefinition>();

            Renderer[] renderers = sourcePrefab.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Transform rendererTransform = renderer.transform;

                string nodePath = PrefabAuthoringBridge.GetHierarchyPath(rendererTransform, sourcePrefab.transform);
                if (string.IsNullOrEmpty(nodePath))
                {
                    continue;
                }

                RigTargetDefinition matchedTarget = FindMatchingTarget(rig.targets, nodePath, matchedTargets);
                if (matchedTarget != null)
                {
                    matchedTargets.Add(matchedTarget);
                    rows.Add(new RigTargetRow
                    {
                        SourceNodePath = nodePath,
                        DisplayName = matchedTarget.displayName,
                        TargetStableId = matchedTarget.Id.Value,
                        TagId = matchedTarget.tagId,
                        IsTarget = true,
                        IsMissingNode = false,
                        PreTicked = false,
                    });
                }
                else
                {
                    rows.Add(new RigTargetRow
                    {
                        SourceNodePath = nodePath,
                        DisplayName = rendererTransform.name,
                        TargetStableId = 0u,
                        TagId = 0u,
                        IsTarget = false,
                        IsMissingNode = false,
                        PreTicked = false,
                    });
                }
            }

            if (rig.targets != null)
            {
                for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
                {
                    RigTargetDefinition targetDefinition = rig.targets[targetIndex];
                    if (targetDefinition == null)
                    {
                        continue;
                    }

                    bool isUnbound = string.IsNullOrEmpty(targetDefinition.sourceNodePath);
                    if (isUnbound || !matchedTargets.Contains(targetDefinition))
                    {
                        rows.Add(new RigTargetRow
                        {
                            SourceNodePath = targetDefinition.sourceNodePath,
                            DisplayName = targetDefinition.displayName,
                            TargetStableId = targetDefinition.Id.Value,
                            TagId = targetDefinition.tagId,
                            IsTarget = true,
                            IsMissingNode = true,
                            PreTicked = false,
                        });
                    }
                }
            }

            return rows;
        }

        private static RigTargetDefinition FindMatchingTarget(
            List<RigTargetDefinition> targets, string nodePath, HashSet<RigTargetDefinition> matchedTargets)
        {
            if (targets == null)
            {
                return null;
            }

            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                RigTargetDefinition targetDefinition = targets[targetIndex];
                if (targetDefinition == null || matchedTargets.Contains(targetDefinition))
                {
                    continue;
                }

                if (string.Equals(targetDefinition.sourceNodePath, nodePath, StringComparison.Ordinal))
                {
                    return targetDefinition;
                }
            }

            return null;
        }
    }
}
