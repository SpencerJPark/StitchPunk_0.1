using UnityEngine;

namespace PlaytestCopilot
{
    /// Decides when an utterance starts and stops and raises a PlaytestMarker for each one.
    /// Reads no input itself: the host passes the record-key state into TickCapture so this
    /// class can be driven from a test with no keyboard.
    public sealed class PlaytestVoiceCaptureController : MonoBehaviour
    {
        // A key bounce is not an utterance: markers shorter than this are dropped silently.
        private const float MinimumMarkerDurationSeconds = 0.25f;

        [SerializeField]
        private PlaytestVoiceMode mode = PlaytestVoiceMode.PushToTalk;

        [SerializeField]
        private KeyCode recordKey = KeyCode.BackQuote;

        [SerializeField]
        private float voiceActivityThreshold = 0.02f;

        [SerializeField]
        // 3 s, not the 0.8 s this shipped with. The owner's first transcribed session measured
        // mid-sentence pauses of 1.0-2.8 s, every one of which closed a marker and split one remark
        // into four notes. Trimming the trailing silence off the end time is what makes a gap this
        // long cost nothing.
        private float silenceHangSeconds = 3.0f;
        private bool closedBySilence;

        [SerializeField]
        private PlaytestMicrophoneRecorder microphone;

        private bool markerOpen;
        private bool manualHoldActive;
        private float silenceSecondsElapsed;
        private PlaytestTimestamp markerStartTimestamp;
        private int completedMarkerCount;

        public PlaytestVoiceMode Mode
        {
            get { return this.mode; }
            set { this.mode = value; }
        }

        public KeyCode RecordKey
        {
            get { return this.recordKey; }
            set { this.recordKey = value; }
        }

        public float VoiceActivityThreshold
        {
            get { return this.voiceActivityThreshold; }
            set { this.voiceActivityThreshold = value; }
        }

        public float SilenceHangSeconds
        {
            get { return this.silenceHangSeconds; }
            set { this.silenceHangSeconds = value; }
        }

        public PlaytestMicrophoneRecorder Microphone
        {
            get { return this.microphone; }
            set { this.microphone = value; }
        }

        public bool IsMarkerOpen
        {
            get { return this.markerOpen; }
        }

        public int CompletedMarkerCount
        {
            get { return this.completedMarkerCount; }
        }

        /// Called by the on-screen record button. Acts like a sustained key press: TickCapture
        /// keeps the marker open until EndMarkerManually releases it, in either voice mode.
        public void BeginMarkerManually()
        {
            this.manualHoldActive = true;
            this.OpenMarkerIfClosed();
        }

        public void EndMarkerManually()
        {
            this.manualHoldActive = false;
            this.CloseMarkerIfOpenAndLongEnough();
        }

        public void TickCapture(bool recordKeyHeld)
        {
            bool keyForcesOpen = recordKeyHeld || this.manualHoldActive;

            if (this.mode == PlaytestVoiceMode.PushToTalk)
            {
                this.TickPushToTalk(keyForcesOpen);
            }
            else
            {
                this.TickAlwaysOn(keyForcesOpen);
            }
        }

        private void TickPushToTalk(bool keyForcesOpen)
        {
            if (keyForcesOpen)
            {
                this.silenceSecondsElapsed = 0.0f;
                this.OpenMarkerIfClosed();
            }
            else
            {
                this.CloseMarkerIfOpenAndLongEnough();
            }
        }

        private void TickAlwaysOn(bool keyForcesOpen)
        {
            // Microphone.CurrentInputLevel is only readable when a microphone recorder is
            // present; a machine with no microphone must never throw here, it simply never
            // triggers on voice and falls back to the record key.
            bool voiceLevelAboveThreshold = this.microphone != null
                && this.microphone.CurrentInputLevel > this.voiceActivityThreshold;

            if (keyForcesOpen || voiceLevelAboveThreshold)
            {
                this.silenceSecondsElapsed = 0.0f;
                this.OpenMarkerIfClosed();
                return;
            }

            if (!this.markerOpen)
            {
                return;
            }

            this.silenceSecondsElapsed += Time.unscaledDeltaTime;
            if (this.silenceSecondsElapsed >= this.silenceHangSeconds)
            {
                this.closedBySilence = true;
                this.CloseMarkerIfOpenAndLongEnough();
            }
        }

        private void OpenMarkerIfClosed()
        {
            if (this.markerOpen)
            {
                return;
            }

            this.markerOpen = true;
            this.silenceSecondsElapsed = 0.0f;
            this.markerStartTimestamp = PlaytestSessionClock.Now;
        }

        private void CloseMarkerIfOpenAndLongEnough()
        {
            if (!this.markerOpen)
            {
                return;
            }

            this.markerOpen = false;
            this.silenceSecondsElapsed = 0.0f;

            // A voice-detected close happens SilenceHangSeconds *after* the last word, so the raw
            // end time includes that wait. Trim it back, or every note claims a window of silence it
            // did not speak into — which makes neighbouring notes overlap and share each other's
            // transcript text. Push-to-talk ends on the key, so nothing is trimmed there.
            PlaytestTimestamp endTimestamp = PlaytestSessionClock.Now;
            if (this.closedBySilence)
            {
                endTimestamp.SecondsSinceSessionStart -= this.SilenceHangSeconds;
                if (endTimestamp.SecondsSinceSessionStart < this.markerStartTimestamp.SecondsSinceSessionStart)
                {
                    endTimestamp.SecondsSinceSessionStart = this.markerStartTimestamp.SecondsSinceSessionStart;
                }
            }

            this.closedBySilence = false;
            double durationSeconds = endTimestamp.SecondsSinceSessionStart
                - this.markerStartTimestamp.SecondsSinceSessionStart;

            if (durationSeconds < MinimumMarkerDurationSeconds)
            {
                return;
            }

            int markerIndex = this.completedMarkerCount;
            PlaytestMarker completedMarker = new PlaytestMarker
            {
                Id = PlaytestMarkerId.For(markerIndex),
                Index = markerIndex,
                Start = this.markerStartTimestamp,
                End = endTimestamp,
            };

            this.completedMarkerCount++;
            PlaytestCaptureBus.RaiseMarkerCompleted(completedMarker);
        }
    }
}
