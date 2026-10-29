// The single home for G5's animation-name convention (D1: no per-unit mapping table — the
// animation name equals the enum name). UnitLibraryBakingSystem resolves these strings through the
// toolkit's AnimationNameRegistry into animation keys at bake time.
public static class AnimationNameConvention
{
    public const string Idle = "Idle";
    public const string Walk = "Walk";

    public static string ForAction(ActionType action) => action.ToString();

    // Face clip naming (G5 follow-up): "<Action>Face", e.g. Death -> "DeathFace". Optional per
    // unit — an unresolved face name bakes key 0 and is never warned about.
    public static string ForActionFace(ActionType action) => action + "Face";

    public static string ForStanceIdle(StanceType stance) => stance + Idle;

    public static string ForStanceWalk(StanceType stance) => stance + Walk;

    // Vehicle clips (Caravan_System.md CV-D21), played on the vehicle's own actor. A name the owner hasn't
    // registered bakes key 0 and the code fallback runs instead.
    public static string ForDoorOpen(string doorAnimationName) => doorAnimationName + "Open";
    public static string ForDoorClose(string doorAnimationName) => doorAnimationName + "Close";
    public static string ForDoorSlam(string doorAnimationName) => doorAnimationName + "Slam";
    public const string VehicleShakeBuildUp = "VehicleShakeBuildUp";
    public const string VehicleShakeBurst = "VehicleShakeBurst";
    public const string VehicleBroken = "VehicleBroken";
}
