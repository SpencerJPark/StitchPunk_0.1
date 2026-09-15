// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One way to deal with a Health finding: a labelled button, a one-line description, and an optional confirmation asked before a destructive run.</summary>
    public sealed class HealthFindingAction
    {
        private const int MaximumListedReferences = 12;

        public string label = string.Empty;
        public string description = string.Empty;
        public bool isDestructive;

        // Null runs the action without asking; destructive actions always supply one naming the asset path and its remaining usage.
        public Func<string> buildConfirmation;

        public Action run;

        public static HealthFindingAction Locate(string label, string description, UnityEngine.Object asset)
        {
            HealthFindingAction action = new HealthFindingAction();
            action.label = label;
            action.description = description;
            action.run = () =>
            {
                if (asset == null)
                {
                    return;
                }

                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            };
            return action;
        }

        public static string BuildDeleteConfirmation(string assetPath, List<AssetReference> remainingReferences)
        {
            StringBuilder confirmation = new StringBuilder();
            confirmation.Append("This moves '").Append(assetPath).Append("' to the OS trash.\n\n");

            if (remainingReferences == null || remainingReferences.Count == 0)
            {
                confirmation.Append("Nothing in the project references it.");
                return confirmation.ToString();
            }

            confirmation.Append("Still referenced by:\n");
            int listedCount = Math.Min(remainingReferences.Count, MaximumListedReferences);
            for (int referenceIndex = 0; referenceIndex < listedCount; referenceIndex++)
            {
                AssetReference reference = remainingReferences[referenceIndex];
                string ownerName = reference.owner != null ? reference.owner.name : "(missing)";
                string usage = reference.detail.Length > 0 ? reference.detail : reference.kind.ToString();
                confirmation.Append("  - ").Append(ownerName).Append(" (").Append(usage).Append(")\n");
            }

            if (remainingReferences.Count > listedCount)
            {
                confirmation.Append("  ...and ").Append(remainingReferences.Count - listedCount).Append(" more\n");
            }

            confirmation.Append("\nThose references break once it is gone.");
            return confirmation.ToString();
        }
    }
}
