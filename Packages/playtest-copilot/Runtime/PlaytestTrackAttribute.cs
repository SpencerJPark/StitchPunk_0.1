using System;

namespace PlaytestCopilot
{
    /// Marks a field or property so the state backend includes its value in every snapshot.
    /// Without it the backend records only the small fixed set (transform, velocity, grounded),
    /// because reflecting over every member of every nearby object is too slow to run at 4 Hz.
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class PlaytestTrackAttribute : Attribute
    {
        /// Overrides the name written into the state log. Defaults to "TypeName.memberName".
        public string DisplayName { get; set; }

        public PlaytestTrackAttribute()
        {
        }

        public PlaytestTrackAttribute(string displayName)
        {
            DisplayName = displayName;
        }
    }
}
