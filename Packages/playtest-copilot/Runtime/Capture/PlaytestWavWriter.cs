using System;
using System.IO;

namespace PlaytestCopilot
{
    /// Nothing else in the package writes WAV headers; every capture path funnels through here.
    public static class PlaytestWavWriter
    {
        private const int BitsPerSample = 16;
        private const int Channels = 1;

        public static void Write(string absolutePath, float[] monoSamples, int sampleCount, int sampleRate)
        {
            string directoryPath = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            int bytesPerSample = BitsPerSample / 8;
            int blockAlign = Channels * bytesPerSample;
            int byteRate = sampleRate * blockAlign;
            int dataByteCount = sampleCount * bytesPerSample;
            int riffChunkSize = 36 + dataByteCount;

            using (FileStream fileStream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter binaryWriter = new BinaryWriter(fileStream))
            {
                binaryWriter.Write(new char[] { 'R', 'I', 'F', 'F' });
                binaryWriter.Write(riffChunkSize);
                binaryWriter.Write(new char[] { 'W', 'A', 'V', 'E' });

                binaryWriter.Write(new char[] { 'f', 'm', 't', ' ' });
                binaryWriter.Write(16);
                binaryWriter.Write((short)1);
                binaryWriter.Write((short)Channels);
                binaryWriter.Write(sampleRate);
                binaryWriter.Write(byteRate);
                binaryWriter.Write((short)blockAlign);
                binaryWriter.Write((short)BitsPerSample);

                binaryWriter.Write(new char[] { 'd', 'a', 't', 'a' });
                binaryWriter.Write(dataByteCount);

                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    float clampedSample = Math.Max(-1f, Math.Min(1f, monoSamples[sampleIndex]));
                    short pcmSample = (short)(clampedSample * short.MaxValue);
                    binaryWriter.Write(pcmSample);
                }
            }
        }
    }
}
