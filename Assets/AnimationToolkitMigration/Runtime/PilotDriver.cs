// Copyright (c) 2026 Stitch Punk. All rights reserved.

using DotsAnimationToolkit;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkitMigration
{
    /// <summary>
    /// Drives the pilot actor so that the three things a static scene cannot show — crossfading,
    /// facing as live state, and alt-view stepping — happen on a timer where they can be watched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is scaffolding for the §13.2 step-2 review, not migration output.</strong> The
    /// real host will drive playback from its behaviour systems and facing from movement; this
    /// exists only so a human can sit in front of the pilot scene and see whether the converted data
    /// blends and flips. It should be deleted, not extended, once the call-site rewrites land.
    /// </para>
    /// <para>
    /// Each of the three effects is on its own period so they never change together — if they all
    /// ticked at once it would be impossible to attribute what you just saw to one of them.
    /// </para>
    /// </remarks>
    public struct PilotDriver : IComponentData
    {
        /// <summary>Seconds between clip swaps on the driven layer.</summary>
        public float clipInterval;

        /// <summary>Crossfade seconds requested on each swap — the thing being demonstrated.</summary>
        public float blendDuration;

        /// <summary>Seconds between mirror flips, deliberately not a multiple of the clip interval.</summary>
        public float mirrorInterval;

        /// <summary>Seconds between alt-view steps. Inert while every target's framesPerVariant is 1.</summary>
        public float viewInterval;

        /// <summary>The layer the driver plays on.</summary>
        public byte layerIndex;

        /// <summary>The two clips it alternates between.</summary>
        public ClipId firstClip;

        /// <summary>The second of the two alternating clips.</summary>
        public ClipId secondClip;

        // Live state.
        public float clipTimer;
        public float mirrorTimer;
        public float viewTimer;
        public bool showingSecondClip;
        public bool mirrored;
        public int viewOffset;
    }

    /// <summary>
    /// Authoring for <see cref="PilotDriver"/>. Defaults are tuned for watching rather than for
    /// realism: a one-second crossfade is far longer than the ≤0.25 s §12 R1 recommends for shipping
    /// content, precisely so the blend is slow enough to see rather than to feel.
    /// </summary>
    public sealed class PilotDriverAuthoring : MonoBehaviour
    {
        [Tooltip("Seconds between clip swaps.")]
        public float clipInterval = 3f;

        [Tooltip("Crossfade seconds. Deliberately long so the blend is watchable.")]
        public float blendDuration = 1f;

        [Tooltip("Seconds between mirror flips. Kept coprime-ish with the clip interval.")]
        public float mirrorInterval = 4f;

        [Tooltip("Seconds between alt-view steps. Does nothing until a target sets Frames Per Variant above 1.")]
        public float viewInterval = 2f;

        [Tooltip("Which playback layer to drive.")]
        public int layerIndex = 3;

        [Tooltip("The two clips to alternate between.")]
        public DotsAnimationToolkit.Authoring.ClipAsset firstClip;

        /// <summary>The second alternating clip.</summary>
        public DotsAnimationToolkit.Authoring.ClipAsset secondClip;

        private sealed class PilotDriverBaker : Baker<PilotDriverAuthoring>
        {
            public override void Bake(PilotDriverAuthoring authoring)
            {
                Entity actorEntity = GetEntity(TransformUsageFlags.Dynamic);
                DependsOn(authoring.firstClip);
                DependsOn(authoring.secondClip);

                AddComponent(actorEntity, new PilotDriver
                {
                    clipInterval = authoring.clipInterval,
                    blendDuration = authoring.blendDuration,
                    mirrorInterval = authoring.mirrorInterval,
                    viewInterval = authoring.viewInterval,
                    layerIndex = (byte)authoring.layerIndex,
                    firstClip = authoring.firstClip != null ? authoring.firstClip.Id : default,
                    secondClip = authoring.secondClip != null ? authoring.secondClip.Id : default,
                    clipTimer = 0f,
                    mirrorTimer = 0f,
                    viewTimer = 0f,
                    showingSecondClip = false,
                    mirrored = false,
                    viewOffset = 0
                });
            }
        }
    }
}
