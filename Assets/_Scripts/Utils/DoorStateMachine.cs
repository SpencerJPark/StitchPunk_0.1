using Unity.Mathematics;

// A door as a tiny state machine (CV-D20). Zones and scripts write wants onto the Door; DoorSystem calls
// ApplyRequests then Advance; the hinge and the art read Openness. One place, so the sequence and the art agree.
public static class DoorControl
{
    // A script override beats the zones: a smoke burst needs shut doors to burst through even with the player stood there.
    public static void ApplyRequests(ref Door door)
    {
        if (door.burstRequested)
        {
            BurstOpen(ref door);
            door.burstRequested = false;
            return;
        }

        bool wantsOpen = door.scriptOverride == DoorOverride.HoldOpen
            || (door.scriptOverride == DoorOverride.None && door.zoneWantsOpen);
        if (wantsOpen) RequestOpen(ref door);
        else RequestClose(ref door);
    }

    public static void RequestOpen(ref Door door)
    {
        if (door.phase == DoorPhase.Open || door.phase == DoorPhase.Opening
            || door.phase == DoorPhase.BurstOpen) return;
        door.phase = DoorPhase.Opening;
        door.phaseSeconds = 0f;
    }

    public static void RequestClose(ref Door door)
    {
        if (door.phase == DoorPhase.Closed || door.phase == DoorPhase.Closing) return;
        door.phase = DoorPhase.Closing;
        door.phaseSeconds = 0f;
    }

    public static void BurstOpen(ref Door door)
    {
        door.phase = DoorPhase.BurstOpen;
        door.phaseSeconds = 0f;
    }

    public static void Advance(ref Door door, float deltaTime)
    {
        door.phaseSeconds += deltaTime;
        if (door.phase == DoorPhase.Opening && door.phaseSeconds >= door.openSeconds)
            SettleInto(ref door, DoorPhase.Open);
        else if (door.phase == DoorPhase.Closing && door.phaseSeconds >= door.closeSeconds)
            SettleInto(ref door, DoorPhase.Closed);
        else if (door.phase == DoorPhase.BurstOpen && door.phaseSeconds >= door.burstSeconds)
            SettleInto(ref door, DoorPhase.Open);
        door.angleDegrees = Openness(in door) * door.openDegrees;
    }

    // 0 = shut, 1 = fully open. A burst overshoots past 1 and springs back, which is what sells the slam.
    public static float Openness(in Door door)
    {
        switch (door.phase)
        {
            case DoorPhase.Open: return 1f;
            case DoorPhase.Opening: return SmoothStep01(door.phaseSeconds / math.max(door.openSeconds, 0.0001f));
            case DoorPhase.Closing: return 1f - SmoothStep01(door.phaseSeconds / math.max(door.closeSeconds, 0.0001f));
            case DoorPhase.BurstOpen: return EaseOutBack(door.phaseSeconds / math.max(door.burstSeconds, 0.0001f));
            default: return 0f;
        }
    }

    private static void SettleInto(ref Door door, DoorPhase settledPhase)
    {
        door.phase = settledPhase;
        door.phaseSeconds = 0f;
    }

    private static float SmoothStep01(float progress)
    {
        float clampedProgress = math.saturate(progress);
        return clampedProgress * clampedProgress * (3f - 2f * clampedProgress);
    }

    private static float EaseOutBack(float progress)
    {
        const float Overshoot = 2.2f;
        float shiftedProgress = math.saturate(progress) - 1f;
        return 1f + (Overshoot + 1f) * shiftedProgress * shiftedProgress * shiftedProgress
                  + Overshoot * shiftedProgress * shiftedProgress;
    }
}
