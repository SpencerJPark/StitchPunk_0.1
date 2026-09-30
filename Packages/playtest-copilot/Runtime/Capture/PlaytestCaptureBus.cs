using System;
using UnityEngine;

namespace PlaytestCopilot
{
    /// The seam between the runtime recorders (which must run inside play mode) and the Editor
    /// code that writes the session folder. Every raise is guarded: a throwing subscriber must
    /// not take the recording down mid-session, because the session is unrepeatable.
    public static class PlaytestCaptureBus
    {
        public static event Action<PlaytestSessionDescriptor> SessionStarted;
        public static event Action<PlaytestSessionDescriptor> SessionEnded;
        /// Runs before MarkerCompleted, and is where the recorder host fills in the marker's
        /// References and State. It exists because subscription order decides who sees a marker
        /// first, and the Editor's folder writer subscribes at domain load, long before the host
        /// is created at play start — so without this the note would be written empty.
        public static event Action<PlaytestMarker> MarkerEnriching;
        public static event Action<PlaytestMarker> MarkerCompleted;
        public static event Action<PlaytestAnnotationCapture> AnnotationCaptured;
        public static event Action<PlaytestStateSample> StateSampleRecorded;
        public static event Action<string> AudioFileWritten;

        public static void RaiseSessionStarted(PlaytestSessionDescriptor descriptor)
        {
            Raise(SessionStarted, descriptor, nameof(SessionStarted));
        }

        public static void RaiseSessionEnded(PlaytestSessionDescriptor descriptor)
        {
            Raise(SessionEnded, descriptor, nameof(SessionEnded));
        }

        public static void RaiseMarkerCompleted(PlaytestMarker marker)
        {
            Raise(MarkerEnriching, marker, nameof(MarkerEnriching));
            Raise(MarkerCompleted, marker, nameof(MarkerCompleted));
        }

        public static void RaiseAnnotationCaptured(PlaytestAnnotationCapture capture)
        {
            Raise(AnnotationCaptured, capture, nameof(AnnotationCaptured));
        }

        public static void RaiseStateSampleRecorded(PlaytestStateSample sample)
        {
            Raise(StateSampleRecorded, sample, nameof(StateSampleRecorded));
        }

        public static void RaiseAudioFileWritten(string absolutePath)
        {
            Raise(AudioFileWritten, absolutePath, nameof(AudioFileWritten));
        }

        /// Subscribers are Editor-side statics that survive a domain reload badly. Tests and the
        /// session teardown both call this so a stale subscriber cannot write into a dead session.
        public static void RemoveAllSubscribers()
        {
            SessionStarted = null;
            MarkerEnriching = null;
            SessionEnded = null;
            MarkerCompleted = null;
            AnnotationCaptured = null;
            StateSampleRecorded = null;
            AudioFileWritten = null;
        }

        private static void Raise<TPayload>(Action<TPayload> handlers, TPayload payload, string eventName)
        {
            if (handlers == null)
            {
                return;
            }

            Delegate[] invocationList = handlers.GetInvocationList();
            for (int handlerIndex = 0; handlerIndex < invocationList.Length; handlerIndex++)
            {
                Action<TPayload> handler = (Action<TPayload>)invocationList[handlerIndex];
                try
                {
                    handler(payload);
                }
                catch (Exception exception)
                {
                    Debug.LogError("Playtest Copilot: a " + eventName + " subscriber threw and was skipped. " + exception);
                }
            }
        }
    }
}
