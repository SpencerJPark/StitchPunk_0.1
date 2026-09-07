// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// What an actor plays: the rig and clip sets it draws from, its turn granularity, and its
    /// named, layered animations. The game plays by name; layer position is priority, never identity.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewActorProfile",
        menuName = "DOTS Animation Toolkit/Actor Profile",
        order = 5)]
    public sealed class ActorProfileAsset : ScriptableObject, IStableIdMintReporter
    {
        /// <summary>The maximum number of playback layers a profile may define, bookends included.</summary>
        public const int MaxLayerCount = 8;

        /// <summary>Display name of the fixed first layer every profile carries.</summary>
        public const string BaseLayerName = "Base";

        /// <summary>Display name of the fixed last layer every profile carries.</summary>
        public const string OverrideLayerName = "Override";

        [SerializeField] internal ulong stableId;

        [Tooltip("The rig every animation in this profile is bound against.")]
        public RigAsset rig;

        [Tooltip("Clip sets this profile draws its named animations from.")]
        public List<ClipSetAsset> clipSets = new List<ClipSetAsset>();

        [Tooltip("How many directions this actor turns through. Content, not rig.")]
        public AnimationDirections turnDirections = AnimationDirections.Six;

        [Tooltip("Playback layers, lowest priority first. [0] is always Base and the last is always Override.")]
        public List<ActorLayerDefinition> layers = new List<ActorLayerDefinition>();

        /// <summary>This profile's stable 64-bit identity.</summary>
        public ulong StableId
        {
            get { return stableId; }
        }

        /// <summary>Assigns a fresh stable id when this profile still carries the reserved 0 value. Idempotent.</summary>
        public void EnsureStableIds()
        {
            if (stableId == 0UL)
            {
                stableId = StableIdMinting.NewAssetStableId();
                hasUnpersistedStableId = true;
            }
        }

        // Bookends are structural, not cosmetic: BehaviorExecution and the Actor Editor both key
        // "the top layer" and "the base layer" off list position, so a brand-new profile must never
        // exist without both, and an existing one must never lose either.
        /// <summary>Guarantees layers[0] is Base and layers[^1] is Override, creating either when missing. Idempotent.</summary>
        public void EnsureBookends()
        {
            if (layers == null)
            {
                layers = new List<ActorLayerDefinition>();
            }

            bool hasBaseAtStart = layers.Count > 0 &&
                                   layers[0] != null &&
                                   layers[0].displayName == BaseLayerName;
            if (!hasBaseAtStart)
            {
                layers.Insert(0, new ActorLayerDefinition { displayName = BaseLayerName });
            }

            int overrideIndex = layers.Count - 1;
            bool hasOverrideAtEnd = layers[overrideIndex] != null &&
                                     layers[overrideIndex].displayName == OverrideLayerName;
            if (!hasOverrideAtEnd)
            {
                layers.Add(new ActorLayerDefinition { displayName = OverrideLayerName });
            }
        }

        // Not serialized: this describes an in-memory condition for the current session, and a
        // persisted "needs persisting" flag would contradict itself.
        [System.NonSerialized] private bool hasUnpersistedStableId;

        /// <inheritdoc />
        public bool HasUnpersistedStableId
        {
            get { return hasUnpersistedStableId; }
        }

        /// <inheritdoc />
        public void MarkStableIdPersisted()
        {
            hasUnpersistedStableId = false;
        }

        // Unity raises Awake when an instance is created and OnEnable both after creation and
        // after an asset is deserialized, so between them no asset can reach an inspector, a bake,
        // or a test without an id or its bookends. All four funnel into the same idempotent calls.
        private void Awake()
        {
            EnsureStableIds();
            EnsureBookends();
        }

        private void OnEnable()
        {
            EnsureStableIds();
            EnsureBookends();
        }

        private void OnValidate()
        {
            EnsureStableIds();
            EnsureBookends();
        }

        private void Reset()
        {
            EnsureStableIds();
            EnsureBookends();
        }
    }

    /// <summary>One playback layer of an <see cref="ActorProfileAsset"/>. Identity is list position; <see cref="displayName"/> is cosmetic except for the fixed Base/Override bookends.</summary>
    [Serializable]
    public sealed class ActorLayerDefinition
    {
        [Tooltip("Cosmetic label, except for the fixed Base (index 0) and Override (last index) bookends.")]
        public string displayName = string.Empty;

        [Tooltip("Whether the baked actor starts with this layer active.")]
        public bool defaultActive;

        [Tooltip("Animation name that seeds this layer at bake. 0 = none.")]
        public uint startingAnimationKey;

        [Tooltip("Named animations authored on this layer.")]
        public List<ActorAnimationDefinition> animations = new List<ActorAnimationDefinition>();
    }

    /// <summary>One named animation: the clip or per-direction clips it plays, how it loops and blends, and whether it triggers ragdoll.</summary>
    [Serializable]
    public sealed class ActorAnimationDefinition
    {
        [Tooltip("Animation name registry id. Unique across the whole profile, across every layer.")]
        public uint animationKey;

        [Tooltip("Whether this animation resolves a clip per facing via directionSlots, or plays one clip outright.")]
        public bool hasDirections;

        [Tooltip("The clip this animation plays when hasDirections is false.")]
        public ClipAsset clip;

        [Tooltip("Per-facing clips this animation resolves against when hasDirections is true.")]
        public DirectionSlots directionSlots = new DirectionSlots();

        [Tooltip("How this animation loops. UseClipDefault defers to the clip's own authored default.")]
        public LoopMode loop = LoopMode.UseClipDefault;

        [Tooltip("Playback speed multiplier.")]
        public float speed = 1f;

        [Tooltip("Blend-in duration, seconds. NaN = the clip's own default.")]
        public float blendIn = float.NaN;

        [Tooltip("Whether playing this animation starts or stops the actor's ragdoll.")]
        public RagdollTrigger ragdollTrigger = RagdollTrigger.None;

        [Tooltip("Event key that fires the ragdoll trigger mid-clip. 0 = at play, immediately.")]
        public uint ragdollAtEventKey;
    }
}
