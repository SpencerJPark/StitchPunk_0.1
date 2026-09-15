// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;

namespace DotsAnimationToolkit.Editor
{
    public enum HealthSeverity : byte
    {
        Note,
        Warning,
        Error
    }

    /// <summary>One cross-asset problem the Health tab lists: severity, code, a short title and explanation, the assets involved, and the actions that deal with it.</summary>
    public sealed class HealthFinding
    {
        public const string ClipInNoSetCode = "H01";
        public const string ClipSetListsNullClipCode = "H02";
        public const string ProfileNamesUnregisteredAnimationCode = "H03";
        public const string ProfileRigDiffersFromClipSetRigCode = "H04";
        public const string RigUsedByNoProfileCode = "H05";
        public const string StaleOrUnbakedVatSetCode = "H06";
        public const string TrackTagNotInRegistryCode = "H07";
        public const string EventKeyNotInRegistryCode = "H08";
        public const string UnpersistedStableIdCode = "H09";
        public const string ClipPosesNothingOnRigCode = "H10";
        public const string ClipSetDoesNotBindToProfileRigCode = "H11";
        public const string SharedClipBindingProblemCode = "H12";

        public HealthSeverity severity;
        public string code = string.Empty;

        // The specific one-line sentence naming the offending assets; the search filter matches it.
        public string message = string.Empty;

        public string title = string.Empty;

        // What is wrong and why it matters at run time, shown wrapped in the detail panel.
        public string detail = string.Empty;

        public UnityEngine.Object target;

        // Locate pings this after target; a stale VAT finding uses it for the clip set behind the texture set.
        public UnityEngine.Object secondaryTarget;

        public List<UnityEngine.Object> relatedAssets = new List<UnityEngine.Object>();
        public List<HealthFindingAction> actions = new List<HealthFindingAction>();

        // A stale or unbaked VAT bake plays old motion with no run-time error, so it outranks every error.
        public bool IsPinnedFirst
        {
            get { return code == StaleOrUnbakedVatSetCode; }
        }
    }
}
