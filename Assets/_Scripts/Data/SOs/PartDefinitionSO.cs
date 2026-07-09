using System;
using System.Collections.Generic;
using UnityEngine;

// Static config for one interchangeable part KIND (e.g. HumanHead, HumanArmLower). Baked into the
// enum-indexed PartLibrary blob by PartLibraryBakingSystem; entities never read the SO at runtime.
// One asset per PartDefId.
//
// Design is tag-driven: the part belongs to a palette `group` (free text — e.g. "Skin", "Hair", or
// empty for none), and lists ranges each tagged with a colour/group label. The character rolls one
// tag per group once (shared by every part in that group — so hair + mustache always match), then
// each part picks a shape within the ranges matching that tag. A range's `tag` left empty means
// colour-independent (e.g. bald). Mark a range non-randomizable to keep it out of random spawns and
// reach it only via a ChangeDesignRequest (e.g. a "Zombie" tag applied at conversion).
[CreateAssetMenu(fileName = "PartDef", menuName = "Units/Part Definition")]
public class PartDefinitionSO : ScriptableObject
{
    [SearchableEnum] public PartDefId id;

    [Header("Design")]
    [Tooltip("Shared palette group this part follows — free text, e.g. \"Skin\" or \"Hair\". " +
             "Every part with the same group name shares the character's rolled tag for that group. " +
             "Leave empty for a part with no shared colour (its ranges' tags are used as-is).")]
    public string group = "";

    [Tooltip("The texture-array ranges for this part. Each range has a colour/group tag (empty = " +
             "colour-independent, e.g. bald), an inclusive [min,max] slice span, and a randomizable flag. " +
             "For parts that recolour on conversion, keep each colour's ranges the same length and order " +
             "(parallel) so a tag swap keeps the shape.")]
    public List<PartRange> ranges = new();

    [Header("Ragdoll")]
    [Tooltip("Angular settle speed (deg/s) toward the landing angle for a RagdollJoint part. " +
             "0 = default (8). A per-placement override on BodyPartAuthoring wins when set.")]
    public float defaultSettleSpeed = 8f;

    [Tooltip("Landing zones (degrees, local Z). One is picked at random on death and a random angle " +
             "within it becomes the joint target.")]
    public List<LandingZone> zones = new();

    [Tooltip("Flail pendulum length (world units) for a RagdollJoint part — pivot-to-tip reach of " +
             "the limb it bends. Shorter = twitchier flail. 0 = default (0.5).")]
    public float ragdollSegmentLength = 0.5f;

    [Tooltip("Flail weight for a RagdollJoint part — scales inherited motion and the landing kick. " +
             "0 = default (1).")]
    public float ragdollWeight = 1f;
}

[Serializable]
public class PartRange
{
    [Tooltip("Colour/group tag. Empty = colour-independent (always available, never recoloured).")]
    public string tag = "";

    [Tooltip("First texture-array slice (inclusive).")]
    public int min;

    [Tooltip("Last texture-array slice (inclusive).")]
    public int max;

    [Tooltip("Stride between slices. 1 = every index (contiguous). 2 = every other (e.g. interleaved " +
             "left/right eyes: right = min 0 step 2, left = min 1 step 2). For a hand-picked set of " +
             "unrelated indices, add one range per index with min == max and the same tag.")]
    public int step = 1;

    [Tooltip("If true, random spawns may roll this range. Uncheck for looks only reached explicitly " +
             "(e.g. a Zombie tag applied via ChangeDesignRequest).")]
    public bool randomizable = true;
}

[Serializable]
public class LandingZone
{
    [Tooltip("Minimum Z rotation (degrees) for this landing zone.")]
    public float min;

    [Tooltip("Maximum Z rotation (degrees) for this landing zone.")]
    public float max;
}
