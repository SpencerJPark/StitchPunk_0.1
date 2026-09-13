// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One event marker on a clip or a cutscene, edited through one inspector. Setters record undo on
    /// <see cref="OwnerAsset"/> and write through; a field the marker type lacks reports its Has flag false and
    /// ignores writes.</summary>
    public interface IEventMarkerAccessor
    {
        ScriptableObject OwnerAsset { get; }

        // False once the marker's index no longer names a marker (the list shrank under an undo).
        bool MarkerExists { get; }

        uint Key { get; set; }

        int IntParam { get; set; }

        float FloatParam { get; set; }

        // Seconds from the owner's start; a clip marker converts through the clip's duration.
        float DisplayTimeSeconds { get; set; }

        // True for a clip marker, whose time is also worth showing in frames.
        bool TimeIsClipRelative { get; }

        bool HasWindow { get; }

        float WindowSeconds { get; set; }

        bool HasSkipFlag { get; }

        bool FireOnSkip { get; set; }

        bool HasHoldFlag { get; }

        bool HoldUntilReleased { get; set; }
    }

    public enum EventMarkerField
    {
        Key,
        IntParam,
        FloatParam,
        Window,
        FireOnSkip,
        HoldUntilReleased
    }
}
