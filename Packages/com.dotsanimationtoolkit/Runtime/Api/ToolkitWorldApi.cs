// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Turns the whole toolkit on or off in a world. Disabling stops everything, timers included —
    /// a disabled toolkit is paused, not hidden. To hide actors while gameplay timing keeps running,
    /// disable <see cref="AnimVisible"/> on them instead.
    /// </summary>
    public static class ToolkitWorldApi
    {
        /// <param name="world">A null or unborn world is ignored rather than throwing.</param>
        /// <returns>False if the world was unusable or contains no toolkit group — the normal answer for a world that has never had an actor in it.</returns>
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
