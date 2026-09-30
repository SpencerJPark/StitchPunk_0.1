using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlaytestCopilot
{
    /// Wraps UnityEngine.Microphone, which only ever hands back a small looping ring clip.
    /// TickCapture drains whatever the ring gained since the last poll into our own growing
    /// buffer so the whole session survives past the ring's length.
    public sealed class PlaytestMicrophoneRecorder : MonoBehaviour
    {
        public const int SampleRate = 16000;

        private const int RingBufferLengthSeconds = 10;
        private const float InputLevelDecayPerSecond = 2f;

        private readonly List<float> capturedSamples = new List<float>();

        private AudioClip microphoneClip;
        private string activeDeviceName;
        private int lastReadPositionSamples;
        private float currentInputLevel;

        public bool IsCapturing { get; private set; }
        public string DeviceName { get; set; }
        public float CurrentInputLevel => currentInputLevel;
        public double SecondsCaptured => capturedSamples.Count / (double)SampleRate;

        public void BeginCapture()
        {
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("Playtest Copilot: no microphone device found; voice capture is disabled for this session.");
                IsCapturing = false;
                return;
            }

            // Empty/null means "default device" to both Microphone.Start and Microphone.GetPosition.
            activeDeviceName = string.IsNullOrEmpty(DeviceName) ? null : DeviceName;
            microphoneClip = Microphone.Start(activeDeviceName, true, RingBufferLengthSeconds, SampleRate);
            lastReadPositionSamples = 0;
            currentInputLevel = 0f;
            capturedSamples.Clear();
            IsCapturing = true;
        }

        public void TickCapture()
        {
            if (!IsCapturing || microphoneClip == null)
            {
                DecayInputLevel();
                return;
            }

            int currentPositionSamples = Microphone.GetPosition(activeDeviceName);
            if (currentPositionSamples == lastReadPositionSamples)
            {
                DecayInputLevel();
                return;
            }

            int ringLengthSamples = RingBufferLengthSeconds * SampleRate;
            int newSampleCount = currentPositionSamples - lastReadPositionSamples;
            if (newSampleCount < 0)
            {
                // The write head wrapped past the end of the ring since our last read.
                newSampleCount += ringLengthSamples;
            }

            float[] newSamples = new float[newSampleCount];
            if (lastReadPositionSamples + newSampleCount <= ringLengthSamples)
            {
                microphoneClip.GetData(newSamples, lastReadPositionSamples);
            }
            else
            {
                int firstSegmentCount = ringLengthSamples - lastReadPositionSamples;
                float[] firstSegment = new float[firstSegmentCount];
                microphoneClip.GetData(firstSegment, lastReadPositionSamples);
                Array.Copy(firstSegment, 0, newSamples, 0, firstSegmentCount);

                int secondSegmentCount = newSampleCount - firstSegmentCount;
                float[] secondSegment = new float[secondSegmentCount];
                microphoneClip.GetData(secondSegment, 0);
                Array.Copy(secondSegment, 0, newSamples, firstSegmentCount, secondSegmentCount);
            }

            capturedSamples.AddRange(newSamples);
            lastReadPositionSamples = currentPositionSamples;
            currentInputLevel = ComputeClampedRootMeanSquare(newSamples);
        }

        public void EndCaptureAndWriteWav(string absoluteWavPath)
        {
            if (!IsCapturing)
            {
                // No device was ever available, so there is nothing captured to write.
                return;
            }

            TickCapture();
            Microphone.End(activeDeviceName);
            IsCapturing = false;

            PlaytestWavWriter.Write(absoluteWavPath, capturedSamples.ToArray(), capturedSamples.Count, SampleRate);
            PlaytestCaptureBus.RaiseAudioFileWritten(absoluteWavPath);
        }

        private void DecayInputLevel()
        {
            currentInputLevel = Mathf.Max(0f, currentInputLevel - InputLevelDecayPerSecond * Time.deltaTime);
        }

        private static float ComputeClampedRootMeanSquare(float[] samples)
        {
            if (samples.Length == 0)
            {
                return 0f;
            }

            double sumOfSquares = 0.0;
            for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                double sample = samples[sampleIndex];
                sumOfSquares += sample * sample;
            }

            double rootMeanSquare = Math.Sqrt(sumOfSquares / samples.Length);
            return (float)Math.Min(1.0, rootMeanSquare);
        }
    }
}
