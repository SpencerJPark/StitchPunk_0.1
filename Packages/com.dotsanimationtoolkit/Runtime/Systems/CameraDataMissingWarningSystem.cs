// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Logs one warning per world when billboarded rigs, or actors under distance LOD, have waited 120
    /// frames for an <see cref="AnimationToolkitCameraData"/> singleton, then disables itself.
    /// </summary>
    // Not Burst-compiled: the warning is a managed string. No order within the group is declared,
    // because the 120-frame grace makes this system's position inside a frame irrelevant.
    [UpdateInGroup(typeof(AnimationToolkitSystemGroup))]
    public partial struct CameraDataMissingWarningSystem : ISystem
    {
        private const int FramesBeforeWarning = 120;

        private EntityQuery billboardRootsQuery;
        private EntityQuery lodActorsQuery;
        private int framesWithoutCamera;

        public void OnCreate(ref SystemState state)
        {
            billboardRootsQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BillboardRootElement>()
                .Build(ref state);
            lodActorsQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AnimLod>()
                .Build(ref state);
            state.RequireAnyForUpdate(billboardRootsQuery, lodActorsQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.HasSingleton<AnimationToolkitCameraData>())
            {
                state.Enabled = false;
                return;
            }

            bool billboardsAreWaiting = !billboardRootsQuery.IsEmptyIgnoreFilter;

            // An AnimLod actor only needs the camera when distance LOD is switched on; a host may give
            // actors AnimLod purely to write the level itself.
            bool distanceLodIsWaiting = false;
            if (!lodActorsQuery.IsEmptyIgnoreFilter
                && SystemAPI.TryGetSingleton(out AnimationToolkitConfig toolkitConfig))
            {
                distanceLodIsWaiting = toolkitConfig.distanceLodEnabled;
            }

            if (!billboardsAreWaiting && !distanceLodIsWaiting)
            {
                return;
            }

            framesWithoutCamera++;
            if (framesWithoutCamera < FramesBeforeWarning)
            {
                return;
            }

            UnityEngine.Debug.LogWarning(BuildWarningMessage(billboardsAreWaiting, distanceLodIsWaiting));
            state.Enabled = false;
        }

        private static string BuildWarningMessage(bool billboardsAreWaiting, bool distanceLodIsWaiting)
        {
            string consequence;
            if (billboardsAreWaiting && distanceLodIsWaiting)
            {
                consequence = "billboarded rigs are not turning to face the camera and distance LOD is not running";
            }
            else if (billboardsAreWaiting)
            {
                consequence = "billboarded rigs are not turning to face the camera";
            }
            else
            {
                consequence = "distance LOD is not running, so AnimLod levels stay where they are";
            }

            return "DOTS Animation Toolkit: no AnimationToolkitCameraData singleton after " + FramesBeforeWarning +
                " frames, so " + consequence + ". Write the singleton from your camera every frame, or import " +
                "the Camera Sync sample and add ToolkitCameraSync to a scene object.";
        }
    }
}
