using System;
using System.Collections.Generic;
using UnityEngine;

// Static config for one interchangeable part KIND (e.g. HumanHead, HumanArmLower). Baked into the
// enum-indexed PartLibrary blob by PartLibraryBakingSystem; entities never read the SO at runtime.
// One asset per PartDefId. Mirrors ItemSO → ItemLibrarySO → ItemLibraryBlob.
[CreateAssetMenu(fileName = "PartDef", menuName = "Units/Part Definition")]
public class PartDefinitionSO : ScriptableObject
{
    [SearchableEnum] public PartDefId id;

    [Header("Design grid")]
    [Tooltip("StrideFormula: slice = baseSlice + shape * colorCount + color (clean re-exported arrays).\n" +
             "ExplicitTable: slice = sliceTable[shape * colorCount + color] (existing irregular arrays).")]
    public GridMode mode = GridMode.StrideFormula;

    [Tooltip("StrideFormula anchor — first slice of this part's block in the texture array.")]
    public int baseSlice;

    [Tooltip("Number of shape variants. Randomize picks a shape in [0, shapeCount).")]
    public int shapeCount = 1;

    [Tooltip("Number of colour columns per shape (e.g. skin/hair tones). 1 if the part has no colour axis.")]
    public int colorCount = 1;

    [Tooltip("Which CharacterPalette value drives the colour column. None = always column 0 (no recolour).")]
    [SearchableEnum] public PaletteGroup colorAxis = PaletteGroup.None;

    [Tooltip("ExplicitTable mode only: flat table of slices, length shapeCount * colorCount, " +
             "row-major as [shape * colorCount + color]. Ignored in StrideFormula mode.")]
    public List<int> sliceTable = new();

    [Header("Ragdoll")]
    [Tooltip("Angular settle speed (deg/s) toward the landing angle for a RagdollJoint part. " +
             "0 = default (8). A per-placement override on BodyPartAuthoring wins when set.")]
    public float defaultSettleSpeed = 8f;

    [Tooltip("Landing zones (degrees, local Z). One is picked at random on death and a random angle " +
             "within it becomes the joint target.")]
    public List<LandingZone> zones = new();
}

[Serializable]
public class LandingZone
{
    [Tooltip("Minimum Z rotation (degrees) for this landing zone.")]
    public float min;

    [Tooltip("Maximum Z rotation (degrees) for this landing zone.")]
    public float max;
}
