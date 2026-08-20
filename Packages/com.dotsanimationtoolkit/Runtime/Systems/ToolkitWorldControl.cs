// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The supported way for a host to turn the whole toolkit on or off in a world
    /// (architecture section 5.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hosts that gate features by scene or by game state — Spencer Park gates on its
    /// <c>GameSceneSystemGroup</c> — call this instead of adding a tag component the package would
    /// have to query for. One boolean on one group is cheaper than a tag on every actor, and it means
    /// the package's own systems carry no host concept at all.
    /// </para>
    /// <para>
    /// Disabling stops <em>everything</em>, timers included, and that is the intent: a disabled
    /// toolkit is a paused toolkit, not a hidden one. To keep gameplay timing running while nothing
    /// draws, leave the group enabled and disable <see cref="AnimVisible"/> on the actors instead —
    /// that is the visibility boundary of architecture section 5.9, and it is a different question
    /// from this one.
    /// </para>
    /// </remarks>
    public static class ToolkitWorldControl
    {
        /// <summary>
        /// Enables or disables <see cref="AnimationToolkitSystemGroup"/> in
        /// <paramref name="world"/>, and with it every system this package owns.
        /// </summary>
        /// <param name="world">
        /// The world to act on. A null or unborn world is ignored rather than throwing: hosts call
        /// this from scene-load paths that can run against a torn-down world during shutdown, and a
        /// throw there would turn a benign ordering quirk into a crash.
        /// </param>
        /// <param name="enabled">True to run the toolkit, false to stop it entirely.</param>
        /// <returns>
        /// True if the group was found and set; false if the world was unusable or contains no
        /// toolkit group — which is the normal answer for a world that has never had an actor in it,
        /// and is not an error.
        /// </returns>
        public static bool SetEnabled(World world, bool enabled)
        {
            if (world == null || !world.IsCreated)
            {
                return false;
            }

            AnimationToolkitSystemGroup toolkitGroup =
                world.GetExistingSystemManaged<AnimationToolkitSystemGroup>();
            if (toolkitGroup == null)
            {
                return false;
            }

            toolkitGroup.Enabled = enabled;
            return true;
        }

        /// <summary>
        /// Reports whether <see cref="AnimationToolkitSystemGroup"/> is currently running in
        /// <paramref name="world"/>. False when the world is unusable or has no toolkit group.
        /// </summary>
        public static bool IsEnabled(World world)
        {
            if (world == null || !world.IsCreated)
            {
                return false;
            }

            AnimationToolkitSystemGroup toolkitGroup =
                world.GetExistingSystemManaged<AnimationToolkitSystemGroup>();
            return toolkitGroup != null && toolkitGroup.Enabled;
        }
    }
}
