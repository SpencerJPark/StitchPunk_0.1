using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct TerrainScatterPrefab
{
    public GameObject prefab;
    public float weight;
}

[System.Serializable]
public struct TerrainScatterRuleData
{
    public string label;
    public List<TerrainScatterPrefab> prefabs;
    public float onDirt;
    public float onGrass;
    public float onForest;
    public float onStone;
    public Vector2 slopeRangeDegrees;
    public Vector2 heightRange;
    public float minSpacingMetres;
    public float clusterNoiseScale;
    public float clusterThreshold;
    public Vector2 scaleRange;
    public float yawJitterDegrees;
    public bool alignToNormal;
    public float densityPer100SquareMetres;
    public bool blocksNavigation;
}

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Scatter Set", fileName = "TerrainScatterSet")]
public class TerrainScatterSet : ScriptableObject
{
    public List<TerrainScatterRuleData> rules = new List<TerrainScatterRuleData>();

    public static TerrainScatterRuleData CreateRule(string label)
    {
        TerrainScatterRuleData rule = new TerrainScatterRuleData();
        rule.label = label;
        rule.prefabs = new List<TerrainScatterPrefab>();
        rule.onDirt = 0.2f;
        rule.onGrass = 1f;
        rule.onForest = 1f;
        rule.onStone = 0.1f;
        rule.slopeRangeDegrees = new Vector2(0f, 35f);
        rule.heightRange = new Vector2(-1000f, 5000f);
        rule.minSpacingMetres = 3f;
        rule.clusterNoiseScale = 0f;
        rule.clusterThreshold = 0f;
        rule.scaleRange = new Vector2(0.8f, 1.2f);
        rule.yawJitterDegrees = 360f;
        rule.alignToNormal = false;
        rule.densityPer100SquareMetres = 1f;
        rule.blocksNavigation = false;
        return rule;
    }

    public GameObject PickPrefab(int ruleIndex, float random01)
    {
        if (ruleIndex < 0 || ruleIndex >= rules.Count)
            return null;
        List<TerrainScatterPrefab> candidates = rules[ruleIndex].prefabs;
        if (candidates == null)
            return null;

        float totalWeight = 0f;
        for (int index = 0; index < candidates.Count; index++)
        {
            if (candidates[index].prefab != null && candidates[index].weight > 0f)
                totalWeight += candidates[index].weight;
        }
        if (totalWeight <= 0f)
            return null;

        float target = Mathf.Clamp01(random01) * totalWeight;
        GameObject lastValid = null;
        for (int index = 0; index < candidates.Count; index++)
        {
            if (candidates[index].prefab == null || candidates[index].weight <= 0f)
                continue;
            lastValid = candidates[index].prefab;
            target -= candidates[index].weight;
            if (target < 0f)
                return lastValid;
        }
        return lastValid;
    }
}
