using Unity.Mathematics;

// The caravan's back doors as a tiny state machine. Sim asks for open/close/burst; CaravanDoorSystem
// advances time; CaravanPresenter reads Openness. One place, so the sequence and the art agree.
public static class CaravanDoorControl
{
    public static void RequestOpen(ref CaravanDoors doors)
    {
        if (doors.phase == CaravanDoorPhase.Open || doors.phase == CaravanDoorPhase.Opening
            || doors.phase == CaravanDoorPhase.BurstOpen) return;
        doors.phase = CaravanDoorPhase.Opening;
        doors.phaseSeconds = 0f;
    }

    public static void RequestClose(ref CaravanDoors doors)
    {
        if (doors.phase == CaravanDoorPhase.Closed || doors.phase == CaravanDoorPhase.Closing) return;
        doors.phase = CaravanDoorPhase.Closing;
        doors.phaseSeconds = 0f;
    }

    public static void BurstOpen(ref CaravanDoors doors)
    {
        doors.phase = CaravanDoorPhase.BurstOpen;
        doors.phaseSeconds = 0f;
    }

    public static void Advance(ref CaravanDoors doors, float deltaTime)
    {
        doors.phaseSeconds += deltaTime;
        if (doors.phase == CaravanDoorPhase.Opening && doors.phaseSeconds >= doors.openSeconds)
            SettleInto(ref doors, CaravanDoorPhase.Open);
        else if (doors.phase == CaravanDoorPhase.Closing && doors.phaseSeconds >= doors.closeSeconds)
            SettleInto(ref doors, CaravanDoorPhase.Closed);
        else if (doors.phase == CaravanDoorPhase.BurstOpen && doors.phaseSeconds >= doors.burstSeconds)
            SettleInto(ref doors, CaravanDoorPhase.Open);
    }

    // 0 = shut, 1 = fully open. A burst overshoots past 1 and springs back, which is what sells the slam.
    public static float Openness(in CaravanDoors doors)
    {
        switch (doors.phase)
        {
            case CaravanDoorPhase.Open: return 1f;
            case CaravanDoorPhase.Opening: return SmoothStep01(doors.phaseSeconds / math.max(doors.openSeconds, 0.0001f));
            case CaravanDoorPhase.Closing: return 1f - SmoothStep01(doors.phaseSeconds / math.max(doors.closeSeconds, 0.0001f));
            case CaravanDoorPhase.BurstOpen: return EaseOutBack(doors.phaseSeconds / math.max(doors.burstSeconds, 0.0001f));
            default: return 0f;
        }
    }

    private static void SettleInto(ref CaravanDoors doors, CaravanDoorPhase settledPhase)
    {
        doors.phase = settledPhase;
        doors.phaseSeconds = 0f;
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
