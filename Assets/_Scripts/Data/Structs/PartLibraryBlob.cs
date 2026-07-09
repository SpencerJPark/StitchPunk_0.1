using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Shared, immutable per-part static config, baked once into an enum-indexed blob by
// PartLibraryBakingSystem and read from Burst jobs via the PartLibrary singleton. Design is a
// tag-driven range list: each part belongs to a palette `group` (e.g. "Skin", "Hair", or empty for
// none), and lists ranges each tagged with a colour/group label. The character rolls one tag per
// group (saved in CharacterPalette, shared by every part in that group); a part's slice is looked up
// by offset within the ranges whose tag matches the character's tag for its group (plus any
// empty-tag / colour-independent ranges). Entities carry only a PartDefId index into `parts`.

public struct PartTagRange
{
    public FixedString32Bytes tag;        // colour/group label; empty = colour-independent ("any")
    public int  min;                       // inclusive first slice
    public int  max;                       // inclusive last slice
    public int  step;                      // stride between slices (1 = contiguous; 2 = every other, e.g. interleaved eyes)
    public bool randomizable;              // eligible for random spawn rolls (false = reached only via ChangeDesignRequest, e.g. Zombie)
}

public struct PartDef
{
    public PartDefId id;
    public FixedString32Bytes group;       // shared palette group this part follows ("Skin"/"Hair"/empty)
    public BlobArray<PartTagRange> ranges;

    // Ragdoll config for parts flagged RagdollJoint.
    public float             defaultSettleSpeed;  // deg/s toward the landing angle (0 = fall back to 8)
    public float             ragdollSegmentLength; // pendulum length (world units) for the flail; default 0.5
    public float             ragdollWeight;        // scales inherited motion + landing impulse; default 1
    public BlobArray<float2> zones;               // landing zones (x = min, y = max), one picked at death
}

public struct PartLibraryBlob
{
    public BlobArray<PartDef> parts;
}
