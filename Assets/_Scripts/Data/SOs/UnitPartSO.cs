using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// Static DESIGN config for one interchangeable part KIND (e.g. HumanHead, HumanArmLower). Baked into
// the enum-indexed PartLibrary blob by PartLibraryBakingSystem; entities never read the SO at
// runtime. One asset per UnitPartId. Purely descriptive and design-only: ragdoll/physics lives on
// the joint empties (RagdollJointAuthoring + RagdollJointSO), and WHAT a random spawn may roll is
// decided by authoring (CharacterRigAuthoring.randomTags), not here.
//
// The part belongs to a shape-tag `group` (free text — e.g. "Skin", "Hair", or empty for none) and
// lists DESIGNS. Each design bundles one tagged texture-slice span with its palette slots, so shape
// and colour switch together. The character rolls one tag per group once (shared by every part in
// that group — so hair + mustache always match), then each part picks a shape within the designs
// matching that tag; the matched design's palette slots colour the part. A design's `tag` left
// empty means tag-independent (e.g. bald). Designs whose tag appears in no authoring randomTags
// entry (e.g. "Zombie") are reached only via a ChangeDesignRequest at conversion.
[CreateAssetMenu(fileName = "Unit Part", menuName = "Units/Unit Parts")]
public class UnitPartSO : ScriptableObject
{
    [SearchableEnum] public UnitPartId id;

    [Header("Design")]
    [Tooltip("Shared palette group this part follows — free text, e.g. \"Skin\" or \"Hair\". " +
             "Every part with the same group name shares the character's rolled tag for that group. " +
             "Leave empty for a part with no shared tag (its designs' tags are used as-is).")]
    public string group = "";
    
    public List<PartDesign> designs = new();
}

[Serializable]
public class PartDesign
{
    [Tooltip("Colour/group tag. Empty = colour-independent (always available, never recoloured).")]
    public string tag = "";

    [Tooltip("First texture-array slice (inclusive).")]
    public int minTextureIndex;

    [Tooltip("Last texture-array slice (inclusive).")]
    public int maxTextureIndex;

    [Tooltip("Stride between slices. 1 = every index (contiguous). 2 = every other (e.g. interleaved " +
             "left/right eyes: right = min 0 step 2, left = min 1 step 2). For a hand-picked set of " +
             "unrelated indices, add one range per index with min == max and the same tag.")]
    public int step = 1;
    
    
    // Not all units will have the same amount of colors, their shaders might not even have all 3 options. 
    [Tooltip("Palette feeding _BaseColor — the packed mask's R channel (base fill). None = the part's " +
             "baked tint is left untouched.")]
    public PaletteSlot primaryColor = new();
    
    [Tooltip("Palette feeding _SecondaryColor — the packed mask's G layer. Colour alpha = layer blend " +
             "strength. None = the baked value is left untouched.")]
    public PaletteSlot secondaryColor = new();

    [Tooltip("Palette feeding _TertiaryColor — the packed mask's B layer. Colour alpha = layer blend " +
             "strength. None = the baked value is left untouched.")]
    public PaletteSlot tertiaryColor = new();
}

[Serializable]
public class PaletteSlot
{
    [Tooltip("Which palette this slot draws from. None = slot unused (the part keeps its baked tint).")]
    [SearchableEnum] public ColorPaletteType palette = ColorPaletteType.None;

    [Tooltip("Use the palette's ENTIRE colour range — no index bookkeeping needed. Untick to " +
             "narrow this slot to a window (fixed colour = [n,n]).")]
    public bool useFullRange = true;

    [ShowWhen("useFullRange", false)]
    public int minColorIndex;

    [ShowWhen("useFullRange", false)]
    public int maxColorIndex;

    public bool useAlternateColor = false;
}