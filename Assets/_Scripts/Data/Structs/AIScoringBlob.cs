using Unity.Entities;
using Unity.Mathematics;

public struct AIScoringBlob
{
    public BlobArray<float> samples;
    public int resolution;

    public float Evaluate(float input)
    {
        float t = math.saturate((input + 100f) / 200f);
        float fIndex = t * (resolution - 1);
        int lower = (int)math.floor(fIndex);
        int upper = math.min(lower + 1, resolution - 1);
        float frac = fIndex - lower;
        return math.lerp(samples[lower], samples[upper], frac);
    }
}

public struct AIScoringCurveEntryBlob
{
    public MotivationType motivationType;
    public AIScoringBlob curve;
}

public struct AIScoringLibraryBlob
{
    public BlobArray<AIScoringCurveEntryBlob> curves;
}