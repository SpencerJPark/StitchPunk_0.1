using Unity.Entities;
using Unity.Mathematics;

// One inline consideration baked from a UtilityActionSO. Curve is pre-sampled over normalized [0,1]
// input. Curve is pre-sampled at bake time and evaluated via linear interpolation.
public struct ConsiderationBlob
{
    public ConsiderationType input;
    public NeedType          needType;    // used when input == ConsiderationType.Motivation
    public PersonalityTypes  traitType;  // used when input == ConsiderationType.Trait
    public float             weight;
    public int               resolution;
    public BlobArray<float>  samples;   // sampled at t in [0,1]

    // Sample the curve at a normalized input t in [0,1] via linear interpolation between samples.
    public float Evaluate(float t)
    {
        float position = math.saturate(t) * (resolution - 1);
        int   lower    = (int)math.floor(position);
        int   upper    = math.min(lower + 1, resolution - 1);
        return math.lerp(samples[lower], samples[upper], position - lower);
    }
}

// One choosable action. behavior is a BehaviorType key into the separate BehaviorLibrary blob.
public struct ActionDefBlob
{
    public ActionType    actionType;
    public int           priority;
    public TargetingMode targeting;
    public BehaviorType  behavior;        // -> BehaviorLibrary, not an internal index
    public int           maxCandidates;   // pool size: how many action entities to spawn per unit
    public BlobArray<ConsiderationBlob> considerations;
}

// One unit archetype's action set. Indices point into BrainLibraryBlob.actionDefs.
public struct BrainBlob
{
    public UnitType       unitType;
    public BlobArray<int> actionDefIndices;
}

// Master config blob: brains (indexed by UnitType) + the shared, de-duplicated action-def table.
public struct BrainLibraryBlob
{
    public BlobArray<BrainBlob>      brains;       // indexed by (int)UnitType
    public BlobArray<ActionDefBlob>  actionDefs;
}
