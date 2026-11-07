using System;
using UnityEngine;

public static class StrategicZoomState
{
    private static bool isStrategicTarget;

    // Linear 0..1 progress of the transition; the profile ease is applied on top by consumers.
    public static float Mix01 { get; internal set; }

    public static bool IsStrategicTarget => isStrategicTarget;

    public static event Action<bool> StrategicTargetChanged;

    public static void SetStrategicTarget(bool strategic)
    {
        if (isStrategicTarget == strategic)
            return;
        isStrategicTarget = strategic;
        StrategicTargetChanged?.Invoke(strategic);
    }

    public static void ForceToClose()
    {
        SetStrategicTarget(false);
    }

    // Constant speed toward the target, so a mid-transition reversal continues from the current value.
    public static float StepMix(float currentMix, float targetMix, float deltaSeconds, float transitionSeconds)
    {
        if (transitionSeconds <= 0f)
            return targetMix;

        float maximumStep = Mathf.Max(0f, deltaSeconds) / transitionSeconds;
        float difference = targetMix - currentMix;
        if (Mathf.Abs(difference) <= maximumStep)
            return targetMix;
        return currentMix + Mathf.Sign(difference) * maximumStep;
    }

    public static float ApplyEase(float linearMix, AnimationCurve ease)
    {
        float clampedMix = Mathf.Clamp01(linearMix);
        if (ease == null)
            return clampedMix;
        return Mathf.Clamp01(ease.Evaluate(clampedMix));
    }

    // Statics survive entering Play mode when domain reload is off.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        Mix01 = 0f;
        isStrategicTarget = false;
        StrategicTargetChanged = null;
    }
}
