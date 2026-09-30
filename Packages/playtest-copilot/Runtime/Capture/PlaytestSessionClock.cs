using UnityEngine;

namespace PlaytestCopilot
{
    /// The one clock every capture stream stamps against: wall time since the session started,
    /// plus the frame index. Uses realtimeSinceStartup rather than Time.time so pause-and-draw
    /// (which sets timeScale to zero) does not stop the clock and desync audio from video.
    public static class PlaytestSessionClock
    {
        private static double sessionStartRealtime;
        private static int sessionStartFrame;

        public static bool IsRunning { get; private set; }

        public static void StartSession()
        {
            sessionStartRealtime = Time.realtimeSinceStartupAsDouble;
            sessionStartFrame = Time.frameCount;
            IsRunning = true;
        }

        public static void StopSession()
        {
            IsRunning = false;
        }

        public static double SecondsSinceSessionStart
        {
            get
            {
                if (!IsRunning)
                {
                    return 0.0;
                }

                return Time.realtimeSinceStartupAsDouble - sessionStartRealtime;
            }
        }

        public static PlaytestTimestamp Now
        {
            get
            {
                PlaytestTimestamp timestamp = new PlaytestTimestamp();
                timestamp.SecondsSinceSessionStart = SecondsSinceSessionStart;
                timestamp.FrameIndex = IsRunning ? Time.frameCount - sessionStartFrame : 0;
                return timestamp;
            }
        }

        /// Timestamp for a moment already in the past, used when a ring buffer is drained after
        /// the fact and the sample's age is known rather than its absolute time.
        public static PlaytestTimestamp AtSecondsAgo(double secondsAgo)
        {
            PlaytestTimestamp timestamp = Now;
            timestamp.SecondsSinceSessionStart -= secondsAgo;
            if (timestamp.SecondsSinceSessionStart < 0.0)
            {
                timestamp.SecondsSinceSessionStart = 0.0;
            }

            return timestamp;
        }
    }
}
