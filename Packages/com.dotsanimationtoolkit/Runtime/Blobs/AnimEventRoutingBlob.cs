// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>Baked event routes a host job reads: which of the host's own sound, VFX or view ids an event key maps to. The package never acts on a route.</summary>
    public struct AnimEventRoutingBlob
    {
        public BlobArray<AnimEventRouteBlob> routes; // sorted by eventKey, then kind, then routeId

        public BlobArray<uint> keys; // distinct routed event keys, ascending; the binary-search array

        public BlobArray<int> keyStarts; // keys.Length + 1 entries: the routes of keys[i] are [keyStarts[i], keyStarts[i + 1])
    }

    public struct AnimEventRouteBlob
    {
        public uint eventKey;

        public AnimEventRouteKind kind;

        public uint routeId; // the host's own id: a sound enum cast, a prefab hash, a material view index
    }

    // What a route names, for the host's switch. The package interprets none of these.
    public enum AnimEventRouteKind : byte
    {
        Sound = 0,
        Vfx = 1,
        Ragdoll = 2,
        ShaderView = 3,
        Custom = 4
    }
}
