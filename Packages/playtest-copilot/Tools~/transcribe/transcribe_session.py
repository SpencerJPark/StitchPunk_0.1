"""Transcribe a Playtest Copilot session's audio.wav into transcript.md and transcript.json.

Run by the Editor after a session ends, as a separate process, so nothing blocks the Editor's main
thread. Writes next to the audio and prints one JSON line of status on stdout for the Editor to read.

    python transcribe_session.py <session folder> [--model small.en] [--device auto]

The Editor does the rest: assigning transcript text to notes, and regenerating index.md and
session-for-agent.md. This script only turns audio into words with timings.
"""

import argparse
import json
import os
import sys
import time
import wave


def decode_wav_as_float32(wav_path):
    """Read a 16-bit PCM mono WAV into the float32 array faster-whisper expects.

    faster-whisper would normally decode with PyAV, but recent PyAV removed the `metadata_errors`
    keyword it passes, which fails on this machine. The capture side always writes 16 kHz mono
    16-bit, which is Whisper's native input, so decoding it here skips PyAV entirely and removes a
    whole dependency from the chain.
    """
    import numpy

    with wave.open(wav_path, "rb") as handle:
        channel_count = handle.getnchannels()
        sample_width = handle.getsampwidth()
        sample_rate = handle.getframerate()
        frame_bytes = handle.readframes(handle.getnframes())

    if sample_width != 2:
        raise ValueError("expected 16-bit PCM, got %d-bit" % (sample_width * 8))

    samples = numpy.frombuffer(frame_bytes, dtype=numpy.int16).astype(numpy.float32) / 32768.0
    if channel_count > 1:
        samples = samples.reshape(-1, channel_count).mean(axis=1)

    return samples, sample_rate


def choose_device(requested_device):
    """CUDA when it actually loads, CPU otherwise.

    A CUDA-capable GPU is not enough: ctranslate2 needs a matching CUDA runtime, and a machine with
    CUDA 11 installed fails at cublas64_12.dll. Probing beats guessing, and CPU int8 runs this
    workload at roughly eight times realtime anyway.
    """
    if requested_device in ("cpu", "cuda"):
        return [(requested_device, "float16" if requested_device == "cuda" else "int8")]

    return [("cuda", "float16"), ("cpu", "int8")]


def transcribe(session_folder, model_name, requested_device):
    from faster_whisper import WhisperModel

    audio_path = os.path.join(session_folder, "audio.wav")
    if not os.path.isfile(audio_path):
        return {"ok": False, "error": "no audio.wav in " + session_folder}

    samples, sample_rate = decode_wav_as_float32(audio_path)
    duration_seconds = len(samples) / float(sample_rate)

    last_error = None
    for device, compute_type in choose_device(requested_device):
        try:
            started = time.time()
            model = WhisperModel(model_name, device=device, compute_type=compute_type)
            segment_iterator, info = model.transcribe(
                samples, word_timestamps=True, vad_filter=False)
            segments = list(segment_iterator)
            elapsed_seconds = time.time() - started
            break
        except Exception as exception:  # noqa: BLE001 - the point is to fall through to CPU
            last_error = str(exception)
            segments = None

    if segments is None:
        return {"ok": False, "error": "every device failed, last: " + str(last_error)}

    transcript = {
        "model": model_name,
        "device": device,
        "language": getattr(info, "language", ""),
        "audioSeconds": round(duration_seconds, 3),
        "transcribeSeconds": round(elapsed_seconds, 3),
        "segments": [],
    }

    for segment in segments:
        transcript["segments"].append({
            "start": round(segment.start, 3),
            "end": round(segment.end, 3),
            "text": segment.text.strip(),
            "words": [
                {"start": round(word.start, 3), "end": round(word.end, 3), "word": word.word}
                for word in (segment.words or [])
            ],
        })

    write_transcript_files(session_folder, transcript)
    return {
        "ok": True,
        "segments": len(transcript["segments"]),
        "device": device,
        "audioSeconds": transcript["audioSeconds"],
        "transcribeSeconds": transcript["transcribeSeconds"],
    }


def format_timestamp(total_seconds):
    minutes = int(total_seconds // 60)
    seconds = int(total_seconds % 60)
    return "%d:%02d" % (minutes, seconds)


def write_transcript_files(session_folder, transcript):
    json_path = os.path.join(session_folder, "transcript.json")
    with open(json_path, "w", encoding="utf-8") as handle:
        json.dump(transcript, handle, indent=1)

    lines = [
        "# Transcript",
        "",
        "%s, %s on %s, %.1fs of audio transcribed in %.1fs."
        % (transcript["model"], transcript.get("language", "") or "unknown language",
           transcript["device"], transcript["audioSeconds"], transcript["transcribeSeconds"]),
        "",
    ]
    for segment in transcript["segments"]:
        lines.append("**%s** %s" % (format_timestamp(segment["start"]), segment["text"]))
        lines.append("")

    markdown_path = os.path.join(session_folder, "transcript.md")
    with open(markdown_path, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("session_folder")
    parser.add_argument("--model", default="small.en")
    parser.add_argument("--device", default="auto", choices=("auto", "cpu", "cuda"))
    arguments = parser.parse_args()

    try:
        result = transcribe(arguments.session_folder, arguments.model, arguments.device)
    except Exception as exception:  # noqa: BLE001 - the Editor needs a parseable failure, not a crash
        result = {"ok": False, "error": str(exception)}

    # One JSON line on the last line of stdout: the Editor parses exactly this.
    sys.stdout.write("\nPLAYTEST_TRANSCRIBE_RESULT " + json.dumps(result) + "\n")
    sys.stdout.flush()
    return 0 if result.get("ok") else 1


if __name__ == "__main__":
    sys.exit(main())
