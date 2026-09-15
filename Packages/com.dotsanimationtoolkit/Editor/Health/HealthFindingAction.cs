// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One way to deal with a Health finding: a labelled button, a one-line description, and an optional confirmation asked before a destructive run.</summary>
    public sealed class HealthFindingAction
    {
        public string label = string.Empty;
        public string description = string.Empty;
        public bool isDestructive;

        // Null runs the action without asking; destructive actions always supply one naming the asset path and its remaining usage.
        public Func<string> buildConfirmation;

        public Action run;
    }
}
