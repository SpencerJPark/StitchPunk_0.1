// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// A "logical animation" that turns: five east-side clip slots, mirrored to cover the west side
    /// for free. Effective coverage is derived from which slots are filled, never declared.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewDirectionSet",
        menuName = "DOTS Animation Toolkit/Direction Set Asset",
        order = 3)]
    public class DirectionSetAsset : ScriptableObject
    {
        public DirectionSlots slots = new DirectionSlots();
    }
}
