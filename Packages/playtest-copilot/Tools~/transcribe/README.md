# Local transcription

Turns a session's `audio.wav` into `transcript.md` and `transcript.json`. The Editor runs this
automatically when a session ends, and `Tools > Playtest Copilot > Transcribe Last Session` runs it
on demand.

## One-time setup

```
python -m venv .venv
.venv/Scripts/python.exe -m pip install faster-whisper
```

The model downloads itself on first use (`small.en` is about 250 MB) and is cached per machine.
Nothing else is needed — in particular **ffmpeg is not required**, because the capture side always
writes 16 kHz mono 16-bit WAV, which this script decodes with Python's own `wave` module.

## Why it does not use faster-whisper's own decoder

faster-whisper decodes audio through PyAV, and recent PyAV removed the `metadata_errors` keyword
it passes, so `transcribe("path.wav")` fails with `open() got an unexpected keyword argument
'metadata_errors'`. Feeding it a float32 numpy array instead skips PyAV entirely. If you ever see
that error from another tool in this venv, this is why.

## Device

CUDA is tried first, then CPU. A CUDA GPU is not sufficient on its own — ctranslate2 needs a
matching CUDA runtime, and a machine with CUDA 11 installed fails on `cublas64_12.dll`. CPU `int8`
runs this workload at roughly eight times realtime, so the fallback is not a problem.
