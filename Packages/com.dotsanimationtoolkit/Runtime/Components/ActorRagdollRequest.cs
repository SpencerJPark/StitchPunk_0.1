// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>Actor-root request for a ragdoll trigger authored on the currently playing animation. Baked disabled; honoured only where <see cref="RagdollActor"/> is present.</summary>
    public struct ActorRagdollRequest : IComponentData, IEnableableComponent
    {
        public RagdollTrigger trigger;
    }
}
