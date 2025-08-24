using System.Collections.Generic;
using UnityEngine;

namespace ScriptableSystems
{
    /// <summary>
    /// Central driver for ScriptableSystems. Initializes them and ticks each
    /// according to its own scheduling settings (GameTime, RealTime, or Frames).
    /// </summary>
    public class SystemInitializer : PersistentSingleton<SystemInitializer>, IUpdateObserver
    {
        [Tooltip("ScriptableSystems to initialize and drive.")]
        [SerializeField] private List<ScriptableSystem> systems = new();

        // Per-system runtime schedule state
        private struct RuntimeState
        {
            public bool initialized;
            public float nextDueTime;  // for GameTime / RealTime
            public int nextDueFrame; // for Frames
        }

        private readonly Dictionary<ScriptableSystem, RuntimeState> stateBySystem = new();

        private void Start()
        {
            float startGameTime = Time.time;
            float startRealTime = Time.unscaledTime;
            int startFrame = Time.frameCount;

            foreach (var system in systems)
            {
                if (system == null) continue;

                system.Initialize();

                var state = new RuntimeState { initialized = true };
                switch (system.TickMode)
                {
                    case TickMode.GameTime:
                        state.nextDueTime = startGameTime + Mathf.Max(0f, system.InitialDelay);
                        break;

                    case TickMode.RealTime:
                        state.nextDueTime = startRealTime + Mathf.Max(0f, system.InitialDelay);
                        break;

                    case TickMode.Frames:
                        int delayFrames = Mathf.RoundToInt(Mathf.Max(0f, system.InitialDelay));
                        state.nextDueFrame = startFrame + delayFrames;
                        break;
                }

                stateBySystem[system] = state;
                // Debug.Log($"Initialized ScriptableSystem: {system.name} ({system.TickMode}, rate={system.TickRate})");
            }
        }

        private void OnEnable() => UpdateManager.RegisterObserver(this);
        private void OnDisable() => UpdateManager.UnregisterObserver(this);

        public void ObservedUpdate()
        {
            float currentGameTime = Time.time;
            float currentRealTime = Time.unscaledTime;
            int currentFrame = Time.frameCount;

            // Iterate a copy to be safe if list changes externally
            for (int i = 0; i < systems.Count; i++)
            {
                var system = systems[i];
                if (system == null) continue;
                if (!stateBySystem.TryGetValue(system, out var runtime) || !runtime.initialized) continue;

                switch (system.TickMode)
                {
                    case TickMode.GameTime:
                        TickByTime(system, ref runtime, currentGameTime);
                        break;

                    case TickMode.RealTime:
                        TickByTime(system, ref runtime, currentRealTime);
                        break;

                    case TickMode.Frames:
                        TickByFrames(system, ref runtime, currentFrame);
                        break;
                }

                stateBySystem[system] = runtime;
            }
        }

        private void TickByTime(ScriptableSystem system, ref RuntimeState runtime, float now)
        {
            float intervalSeconds = Mathf.Max(0f, system.TickRate);

            // Special case: 0 seconds → tick every ObservedUpdate (once-per-frame in this time mode)
            if (intervalSeconds <= 0f)
            {
                SafeTick(system);
                return;
            }

            if (now < runtime.nextDueTime) return;

            if (!system.AllowCatchUp)
            {
                SafeTick(system);
                runtime.nextDueTime = now + intervalSeconds;
                return;
            }

            int maxCatchUp = Mathf.Max(1, system.MaxCatchUpPerUpdate);
            int ticksThisFrame = 0;

            while (now >= runtime.nextDueTime && ticksThisFrame < maxCatchUp)
            {
                SafeTick(system);
                runtime.nextDueTime += intervalSeconds;
                ticksThisFrame++;
            }

            // If we've fallen far behind, snap schedule forward to avoid huge loops next frame.
            if (now - runtime.nextDueTime > intervalSeconds * 4f)
            {
                runtime.nextDueTime = now + intervalSeconds;
            }
        }

        private void TickByFrames(ScriptableSystem system, ref RuntimeState runtime, int currentFrame)
        {
            int framesPerTick = Mathf.Max(1, Mathf.RoundToInt(system.TickRate)); // 1 = once-per-frame
            if (currentFrame < runtime.nextDueFrame) return;

            SafeTick(system);
            runtime.nextDueFrame = currentFrame + framesPerTick;
        }

        private static void SafeTick(ScriptableSystem system)
        {
            try { system.Tick(); }
            catch (System.Exception ex) { Debug.LogException(ex); }
        }

        private void OnDestroy()
        {
            // Optional: shut down systems
            foreach (var system in systems)
            {
                if (system == null) continue;
                try { system.Shutdown(); }
                catch (System.Exception ex) { Debug.LogException(ex); }
            }

            stateBySystem.Clear();
        }
    }
}