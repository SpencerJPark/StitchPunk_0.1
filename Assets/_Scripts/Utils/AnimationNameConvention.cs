// The single home for G5's animation-name convention (D1: no per-unit mapping table — the
// animation name equals the enum name). UnitLibraryBakingSystem resolves these strings through the
// toolkit's AnimationNameRegistry into animation keys at bake time.
public static class AnimationNameConvention
{
    public const string Idle = "Idle";
    public const string Walk = "Walk";

    public static string ForAction(ActionType action) => action.ToString();

    public static string ForStanceIdle(StanceType stance) => stance + Idle;

    public static string ForStanceWalk(StanceType stance) => stance + Walk;
}
