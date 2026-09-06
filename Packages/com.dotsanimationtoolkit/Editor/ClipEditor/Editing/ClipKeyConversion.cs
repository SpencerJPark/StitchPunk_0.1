// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Converts a pose key between the two forms the authoring data stores it in. The two types
    /// hold the same pose and differ only in the rotation: <c>TransformKey</c> keeps Euler degrees,
    /// <c>BoneKey</c> keeps a quaternion — everything else is the same value under a different name.
    /// </summary>
    public static class ClipKeyConversion
    {
        /// <summary>The same pose, with the rotation in the form a transform key stores it.</summary>
        public static TransformKey ToTransformKey(BoneKey boneKey)
        {
            return new TransformKey
            {
                normalizedTime = boneKey.normalizedTime,
                position = boneKey.localPosition,
                rotation = ClipBoneEditing.ToSignedEulerDegrees(boneKey.localRotation),
                scale = boneKey.localScale,
                interpolation = boneKey.interpolation,
                bezierStartHandle = boneKey.bezierStartHandle,
                bezierEndHandle = boneKey.bezierEndHandle
            };
        }

        /// <summary>The same pose, with the rotation in the form a bone key stores it.</summary>
        public static BoneKey ToBoneKey(TransformKey transformKey)
        {
            return new BoneKey
            {
                normalizedTime = transformKey.normalizedTime,
                localPosition = transformKey.position,
                // Read in Unity's ZXY order, matching TransformKey.rotation and the bake — any other order
                // matches on one axis and drifts on the rest.
                localRotation = quaternion.Euler(math.radians(transformKey.rotation)),
                localScale = transformKey.scale,
                interpolation = transformKey.interpolation,
                bezierStartHandle = transformKey.bezierStartHandle,
                bezierEndHandle = transformKey.bezierEndHandle
            };
        }
    }
}
