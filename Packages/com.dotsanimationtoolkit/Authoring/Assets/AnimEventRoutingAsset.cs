// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>The project's event routes: data a host's consuming system reads once baked. The package never interprets a route.</summary>
    public sealed class AnimEventRoutingAsset : ScriptableObject
    {
        public List<AnimEventRoute> routes = new List<AnimEventRoute>();
    }

    [Serializable]
    public sealed class AnimEventRoute
    {
        public uint eventKey;

        public AnimEventRouteKind kind;

        [Tooltip("The host's own id for this route: a sound enum value, a prefab hash, a material view index.")]
        public uint routeId;

        [Tooltip("Free-text note shown in the Events tab. Not baked.")]
        public string note = string.Empty;

        [Tooltip("Editor convenience only: the clip, prefab or material this route stands for. Not baked.")]
        public UnityEngine.Object displayAsset;
    }
}
