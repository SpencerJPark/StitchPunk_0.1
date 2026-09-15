// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit.Editor
{
    public enum HealthSeverity : byte
    {
        Note,
        Warning,
        Error
    }

    /// <summary>One cross-asset problem the Health tab lists: severity, display code, message, the asset to locate, and an optional one-click fix.</summary>
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

        public HealthSeverity severity;
        public string code = string.Empty;
        public string message = string.Empty;
        public UnityEngine.Object target;

        // Locate pings this after target; a stale VAT finding uses it for the clip set behind the texture set.
        public UnityEngine.Object secondaryTarget;

        public Action fix;
        public string fixLabel = string.Empty;

        // A stale or unbaked VAT bake plays old motion with no run-time error, so it outranks every error.
        public bool IsPinnedFirst
        {
            get { return code == StaleOrUnbakedVatSetCode; }
        }
    }
}
