using System.Collections.Generic;
using System.Text;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

// RG-T2: authors the eleven ragdoll bodies on NewRig.asset from the MaleCitizen prefab's own part
// meshes, so the boxes always match whatever the art currently is instead of hand-typed guesses.
public static class NewRigRagdollAuthoring
{
    private const string RigAssetPath = "Assets/ScriptableObjects/Animations/NewRig.asset";
    private const string PrefabAssetPath = "Assets/Prefabs/Units/MaleCitizen.prefab";

    // z is clamped up from 0 (a flat cutout quad) so rule V-R4 (all box extents > 0) is satisfied.
    private const float MinimumBoxDepth = 0.05f;

    private struct BodyRecipe
    {
        public string TargetDisplayName;
        public float Mass;
        public float LimitMinDegrees;
        public float LimitMaxDegrees;
    }

    // Buffer order = depth, per spec §3. Pelvis has no parent joint, so its limits are unused by
    // the solver; 0/0 is used rather than the ±45 default so nobody mistakes it for a tuned hinge.
    // Lower-arm and lower-leg limits put the elbow/knee bend side on the positive value per the
    // parent instructions — the correct sign is unconfirmed until judged visually at T4.
    private static readonly BodyRecipe[] BodyRecipes = new BodyRecipe[]
    {
        new BodyRecipe { TargetDisplayName = "Pelvis", Mass = 3f, LimitMinDegrees = 0f, LimitMaxDegrees = 0f },
        new BodyRecipe { TargetDisplayName = "Torso", Mass = 4f, LimitMinDegrees = -20f, LimitMaxDegrees = 20f },
        new BodyRecipe { TargetDisplayName = "BaseHead", Mass = 1.5f, LimitMinDegrees = -25f, LimitMaxDegrees = 25f },
        new BodyRecipe { TargetDisplayName = "LeftUpperArm", Mass = 1f, LimitMinDegrees = -120f, LimitMaxDegrees = 120f },
        new BodyRecipe { TargetDisplayName = "LeftLowerArm", Mass = 0.7f, LimitMinDegrees = -5f, LimitMaxDegrees = 120f },
        new BodyRecipe { TargetDisplayName = "RightUpperArm", Mass = 1f, LimitMinDegrees = -120f, LimitMaxDegrees = 120f },
        new BodyRecipe { TargetDisplayName = "RightLowerArm", Mass = 0.7f, LimitMinDegrees = -5f, LimitMaxDegrees = 120f },
        new BodyRecipe { TargetDisplayName = "LeftUpperLeg", Mass = 1.5f, LimitMinDegrees = -70f, LimitMaxDegrees = 70f },
        new BodyRecipe { TargetDisplayName = "LeftLowerLeg", Mass = 1f, LimitMinDegrees = -5f, LimitMaxDegrees = 110f },
        new BodyRecipe { TargetDisplayName = "RightUpperLeg", Mass = 1.5f, LimitMinDegrees = -70f, LimitMaxDegrees = 70f },
        new BodyRecipe { TargetDisplayName = "RightLowerLeg", Mass = 1f, LimitMinDegrees = -5f, LimitMaxDegrees = 110f },
    };

    [MenuItem("Tools/Ragdoll/Author NewRig Bodies (RG-T2)")]
    public static string AuthorBodies()
    {
        RigAsset rigAsset = AssetDatabase.LoadAssetAtPath<RigAsset>(RigAssetPath);
        if (rigAsset == null)
        {
            string missingRigMessage = "NewRigRagdollAuthoring.AuthorBodies: could not load rig at " + RigAssetPath;
            Debug.LogError(missingRigMessage);
            return missingRigMessage;
        }

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabAssetPath);
        if (prefabRoot == null)
        {
            string missingPrefabMessage = "NewRigRagdollAuthoring.AuthorBodies: could not load prefab at " + PrefabAssetPath;
            Debug.LogError(missingPrefabMessage);
            return missingPrefabMessage;
        }

        rigAsset.ragdollBodies.Clear();

        List<string> targetsNotFound = new List<string>();
        List<string> partsWithoutMesh = new List<string>();
        int bodiesAuthored = 0;

        for (int recipeIndex = 0; recipeIndex < BodyRecipes.Length; recipeIndex++)
        {
            BodyRecipe recipe = BodyRecipes[recipeIndex];

            RigTargetDefinition matchedTarget = null;
            for (int targetIndex = 0; targetIndex < rigAsset.targets.Count; targetIndex++)
            {
                RigTargetDefinition candidateTarget = rigAsset.targets[targetIndex];
                if (candidateTarget != null && candidateTarget.displayName == recipe.TargetDisplayName)
                {
                    matchedTarget = candidateTarget;
                    break;
                }
            }

            if (matchedTarget == null)
            {
                targetsNotFound.Add(recipe.TargetDisplayName);
                continue;
            }

            float3 boxCenter = float3.zero;
            float3 boxSize = new float3(1f, 1f, 1f);

            Transform partTransform = prefabRoot.transform.Find(matchedTarget.sourceNodePath);
            MeshFilter partMeshFilter = partTransform == null ? null : partTransform.GetComponent<MeshFilter>();
            Mesh partMesh = partMeshFilter == null ? null : partMeshFilter.sharedMesh;

            if (partMesh == null)
            {
                partsWithoutMesh.Add(recipe.TargetDisplayName);
            }
            else
            {
                Bounds meshBounds = partMesh.bounds;
                boxCenter = new float3(meshBounds.center.x, meshBounds.center.y, meshBounds.center.z);
                boxSize = new float3(
                    meshBounds.size.x,
                    meshBounds.size.y,
                    math.max(meshBounds.size.z, MinimumBoxDepth));
            }

            RagdollBodyDefinition bodyDefinition = new RagdollBodyDefinition
            {
                displayName = matchedTarget.displayName,
                address = new RigNodeAddress { kind = RigNodeAddressKind.RigTarget, targetId = matchedTarget.Id.Value },
                boxCenter = boxCenter,
                boxSize = boxSize,
                boxEulerAngles = float3.zero,
                mass = recipe.Mass,
                linearDamping = -1f,
                angularDamping = -1f,
                restitution = 0f,
                friction = 0.5f,
                limitMinDegrees = recipe.LimitMinDegrees,
                limitMaxDegrees = recipe.LimitMaxDegrees,
                selfGroup = 0,
                selfCollidesWith = 0,
                collidesWithWorld = true,
            };

            rigAsset.ragdollBodies.Add(bodyDefinition);
            bodiesAuthored++;
        }

        rigAsset.EnsureStableIds();
        EditorUtility.SetDirty(rigAsset);
        AssetDatabase.SaveAssets();
        PrefabUtility.UnloadPrefabContents(prefabRoot);

        StringBuilder summaryBuilder = new StringBuilder();
        summaryBuilder.Append("NewRigRagdollAuthoring.AuthorBodies: authored ").Append(bodiesAuthored).Append(" bodies.");
        if (targetsNotFound.Count > 0)
        {
            summaryBuilder.Append(" Targets not found: ").Append(string.Join(", ", targetsNotFound)).Append('.');
        }
        if (partsWithoutMesh.Count > 0)
        {
            summaryBuilder.Append(" Parts without a mesh (fell back to a 1x1x1 box): ").Append(string.Join(", ", partsWithoutMesh)).Append('.');
        }

        string summary = summaryBuilder.ToString();
        Debug.Log(summary);
        return summary;
    }

    [MenuItem("Tools/Ragdoll/Verify NewRig Bodies (RG-T2)")]
    public static string Verify()
    {
        AssetDatabase.Refresh();
        RigAsset rigAsset = AssetDatabase.LoadAssetAtPath<RigAsset>(RigAssetPath);
        if (rigAsset == null)
        {
            string missingRigMessage = "NewRigRagdollAuthoring.Verify: could not load rig at " + RigAssetPath;
            Debug.LogError(missingRigMessage);
            return missingRigMessage;
        }

        int bodyCount = rigAsset.ragdollBodies.Count;
        int nonZeroIdCount = 0;
        for (int bodyIndex = 0; bodyIndex < rigAsset.ragdollBodies.Count; bodyIndex++)
        {
            if (rigAsset.ragdollBodies[bodyIndex].Id.Value != 0u)
            {
                nonZeroIdCount++;
            }
        }

        List<ValidationMessage> validationMessages = ClipValidation.ValidateRig(rigAsset);

        StringBuilder summaryBuilder = new StringBuilder();
        summaryBuilder.Append("NewRigRagdollAuthoring.Verify: bodyCount=").Append(bodyCount);
        summaryBuilder.Append(" nonZeroIdCount=").Append(nonZeroIdCount);
        summaryBuilder.Append(" validationMessages=[");
        for (int messageIndex = 0; messageIndex < validationMessages.Count; messageIndex++)
        {
            if (messageIndex > 0)
            {
                summaryBuilder.Append(" | ");
            }
            summaryBuilder.Append(validationMessages[messageIndex].ToString());
        }
        summaryBuilder.Append(']');

        string summary = summaryBuilder.ToString();
        Debug.Log(summary);
        return summary;
    }
}
