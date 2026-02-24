// =====================================
// HELPER: Bulk operations for animation clips
// =====================================

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;

public static class AnimationClipUtilities
{
    [MenuItem("Assets/Animation/Duplicate Clip")]
    private static void DuplicateClip()
    {
        var source = Selection.activeObject as AnimationClipSO;
        if (source == null) return;
        
        string path = AssetDatabase.GetAssetPath(source);
        string newPath = path.Replace(".asset", "_Copy.asset");
        
        var copy = ScriptableObject.CreateInstance<AnimationClipSO>();
        copy.animationType = source.animationType;
        copy.duration = source.duration;
        copy.looping = source.looping;
        copy.allowBlendIn = source.allowBlendIn;
        copy.allowBlendOut = source.allowBlendOut;
        
        copy.partTracks = source.partTracks.Select(t => new AnimationClipSO.PartTrack
        {
            animationTarget = t.animationTarget,
            blendMode = t.blendMode,
            interpolation = t.interpolation,
            animatedProperties = t.animatedProperties,
            keyframes = t.keyframes.Select(k => new AnimationClipSO.Keyframe
            {
                normalizedTime = k.normalizedTime,
                position = k.position,
                rotation = k.rotation,
                scale = k.scale,
                imageIndex = k.imageIndex,
                overrideInterpolation = k.overrideInterpolation,
                interpolationOverride = k.interpolationOverride
            }).ToList()
        }).ToList();
        
        AssetDatabase.CreateAsset(copy, newPath);
        AssetDatabase.SaveAssets();
        Selection.activeObject = copy;
    }
    
    [MenuItem("Assets/Animation/Duplicate Clip", true)]
    private static bool DuplicateClipValidation()
    {
        return Selection.activeObject is AnimationClipSO;
    }
    
    [MenuItem("Assets/Animation/Mirror Clip (Flip X)")]
    private static void MirrorClip()
    {
        var source = Selection.activeObject as AnimationClipSO;
        if (source == null) return;
        
        string path = AssetDatabase.GetAssetPath(source);
        string newPath = path.Replace(".asset", "_Mirrored.asset");
        
        var copy = ScriptableObject.CreateInstance<AnimationClipSO>();
        copy.animationType = source.animationType;
        copy.duration = source.duration;
        copy.looping = source.looping;
        copy.allowBlendIn = source.allowBlendIn;
        copy.allowBlendOut = source.allowBlendOut;
        
        copy.partTracks = source.partTracks.Select(t => new AnimationClipSO.PartTrack
        {
            animationTarget = MirrorBodyPart(t.animationTarget),
            blendMode = t.blendMode,
            interpolation = t.interpolation,
            animatedProperties = t.animatedProperties,
            keyframes = t.keyframes.Select(k => new AnimationClipSO.Keyframe
            {
                normalizedTime = k.normalizedTime,
                position = new Vector3(-k.position.x, k.position.y, k.position.z), // Flip X
                rotation = -k.rotation, // Flip rotation
                scale = new Vector2(-k.scale.x, k.scale.y), // Flip scale X
                imageIndex = k.imageIndex,
                overrideInterpolation = k.overrideInterpolation,
                interpolationOverride = k.interpolationOverride
            }).ToList()
        }).ToList();
        
        AssetDatabase.CreateAsset(copy, newPath);
        AssetDatabase.SaveAssets();
        Selection.activeObject = copy;
    }
    
    private static AnimationTarget MirrorBodyPart(AnimationTarget part)
    {
        return part switch
        {
            AnimationTarget.UpperLeftArm => AnimationTarget.UpperRightArm,
            AnimationTarget.UpperRightArm => AnimationTarget.UpperLeftArm,
            AnimationTarget.LowerLeftArm => AnimationTarget.LowerRightArm,
            AnimationTarget.LowerRightArm => AnimationTarget.LowerLeftArm,
            AnimationTarget.LeftHand => AnimationTarget.RightHand,
            AnimationTarget.RightHand => AnimationTarget.LeftHand,
            AnimationTarget.LeftEyebrow => AnimationTarget.RightEyebrow,
            AnimationTarget.RightEyebrow => AnimationTarget.LeftEyebrow,
            _ => part
        };
    }
    
    [MenuItem("Assets/Animation/Mirror Clip (Flip X)", true)]
    private static bool MirrorClipValidation()
    {
        return Selection.activeObject is AnimationClipSO;
    }
    
    [MenuItem("Assets/Create/Animation/Animation Clip SO")]
    private static void CreateAnimationClip()
    {
        var clip = ScriptableObject.CreateInstance<AnimationClipSO>();
        clip.duration = 1f;
        clip.looping = true;
        
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (string.IsNullOrEmpty(path))
            path = "Assets";
        else if (System.IO.Path.GetExtension(path) != "")
            path = System.IO.Path.GetDirectoryName(path);
        
        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{path}/NewAnimationClip.asset");
        AssetDatabase.CreateAsset(clip, assetPath);
        AssetDatabase.SaveAssets();
        
        Selection.activeObject = clip;
        EditorGUIUtility.PingObject(clip);
    }
}
#endif
/*
Scene Setup

Here's how to set up your animation editing scene:

1. **Create a new scene** called "AnimationEditor"

2. **Add required GameObjects:**
```
AnimationEditorScene (empty)
├── AnimationEditorScene (component)
├── AnimationEditorSceneAuthoring (component) 
├── AnimationPreviewController (component)
│
├── Main Camera
│   └── Camera (ortho, size 5)
│
├── AnimationLibrary (empty)
│   └── AnimationLibraryAuthoring (component, assign your library SO)
│
└── PreviewCharacter (your character prefab)
    ├── CharacterAnimationAuthoring
    └── [Body part children with BodyPartAuthoring] 
    */