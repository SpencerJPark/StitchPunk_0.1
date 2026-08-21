// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Converts a pose key between the two forms the authoring data stores it in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The two key types hold the same pose and differ only in how the rotation is
    /// written.</strong> A <c>TransformKey</c> keeps Euler degrees, because those are the numbers a
    /// person types into three fields; a <c>BoneKey</c> keeps a quaternion, because a joint
    /// orientation arrives from a bake or a solver and nobody types it. Everything else — time,
    /// position, scale, easing, the Bézier handles — is the same value under a different name.
    /// </para>
    /// <para>
    /// Which means moving a pose between them is lossless in one direction and very nearly so in
    /// the other: Euler → quaternion is exact, and quaternion → Euler is exact up to the choice of
    /// representative angle, which <c>ClipBoneEditing.ToSignedEulerDegrees</c> makes the same way
    /// the bone inspector does. Copying a part's animation onto a bone therefore does what it looks
    /// like it does, rather than being refused because the two store their rotations differently.
    /// </para>
    /// <para>
    /// Kept here rather than on either key type because it belongs to neither: it is the seam
    /// between them, and both the component stack's promotion path and the key clipboard cross it.
    /// </para>
    /// </remarks>
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
        /// <remarks>
        /// The Euler angles are read in Unity's ZXY order, which is the order
        /// <c>TransformKey.rotation</c> documents and the order the bake converts them in. Reading
        /// them in any other would give a pose that matched on one axis and drifted on the rest —
        /// the kind of wrong that looks like a rigging mistake rather than a conversion bug.
        /// </remarks>
        public static BoneKey ToBoneKey(TransformKey transformKey)
        {
            return new BoneKey
            {
                normalizedTime = transformKey.normalizedTime,
                localPosition = transformKey.position,
                localRotation = quaternion.Euler(math.radians(transformKey.rotation)),
                localScale = transformKey.scale,
                interpolation = transformKey.interpolation,
                bezierStartHandle = transformKey.bezierStartHandle,
                bezierEndHandle = transformKey.bezierEndHandle
            };
        }
    }
}
