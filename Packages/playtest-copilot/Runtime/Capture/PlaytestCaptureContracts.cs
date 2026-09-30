using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlaytestCopilot
{
    /// "note_014". Three digits keeps the note folders sorting correctly up to 999 a session.
    /// Lives in Runtime because the recorders mint the ids and the Editor writers only reuse them.
    public static class PlaytestMarkerId
    {
        public static string For(int markerIndex)
        {
            return "note_" + markerIndex.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public enum PlaytestNoteIntent
    {
        Unclassified,
        Bug,
        Tuning,
        Feature,
        Content,
        Noise
    }

    public enum PlaytestVoiceMode
    {
        PushToTalk,
        AlwaysOn
    }

    public enum PlaytestAnnotationTool
    {
        Circle,
        Pen,
        Arrow,
        Eraser
    }

    /// Which capture signal produced an object reference. The resolver ranks by this before
    /// it ranks by distance, so the ordering of the enum is the ranking and must not be shuffled.
    public enum PlaytestReferenceSource
    {
        CircledRegion,
        PointerUnderCursor,
        EditorSelection,
        TranscriptName,
        NearCameraCenter,
        RecentInteraction
    }

    /// Every stream is stamped with the same clock so a spoken word can be matched to a frame
    /// and a state sample. Seconds are relative to the session start, never to process start.
    [Serializable]
    public struct PlaytestTimestamp
    {
        public double SecondsSinceSessionStart;
        public int FrameIndex;

        public override string ToString()
        {
            return SecondsSinceSessionStart.ToString("F3") + "s #" + FrameIndex;
        }
    }

    /// One name/value pair in a state snapshot. Values are strings because the state log is
    /// written with JsonUtility, which cannot serialise a Dictionary or a boxed object.
    [Serializable]
    public sealed class PlaytestStateField
    {
        public string Name;
        public string Value;

        public PlaytestStateField()
        {
        }

        public PlaytestStateField(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    [Serializable]
    public sealed class PlaytestObjectReference
    {
        public string ObjectName;
        public string HierarchyPath;
        public string PrefabAssetPath;
        public List<string> ComponentTypeNames = new List<string>();
        public float Confidence;
        public string ResolvedBy;
    }

    /// A screen-space region the user circled during pause-and-draw, in game-view pixels with
    /// the origin at the bottom left, matching Camera.ScreenPointToRay.
    [Serializable]
    public struct PlaytestScreenRegion
    {
        public Rect ScreenRect;
        public PlaytestAnnotationTool Tool;
    }

    /// One utterance or typed note. Serialised to notes/<id>/note.json.
    [Serializable]
    public sealed class PlaytestMarker
    {
        public string Id;
        public int Index;
        public PlaytestTimestamp Start;
        public PlaytestTimestamp End;
        public string TranscriptText = string.Empty;
        public string TypedNote = string.Empty;
        public string Intent = PlaytestNoteIntent.Unclassified.ToString();
        public bool HasAnnotation;
        public List<PlaytestObjectReference> References = new List<PlaytestObjectReference>();
        public List<PlaytestStateField> State = new List<PlaytestStateField>();
        public List<PlaytestScreenRegion> CircledRegions = new List<PlaytestScreenRegion>();
    }

    /// Serialised to session.json. Written once at start and rewritten at stop with EndedUtc.
    [Serializable]
    public sealed class PlaytestSessionDescriptor
    {
        public string SessionId;
        public string StartedUtc;
        public string EndedUtc;
        public string UnityVersion;
        public string ScenePath;
        public string GitCommit;
        public string VoiceMode;
        public int CaptureWidth;
        public int CaptureHeight;
        public int MicrophoneSampleRate;
        public List<string> StateBackends = new List<string>();
        public string AbsoluteFolderPath;
    }

    /// One line of state.jsonl.
    [Serializable]
    public sealed class PlaytestStateSample
    {
        public PlaytestTimestamp Time;
        public string Kind;
        public List<PlaytestStateField> Fields = new List<PlaytestStateField>();
    }

    /// Carries the three images off the annotation overlay. Not [Serializable]: the PNG bytes
    /// travel on the bus to the Editor writer and are never put through JsonUtility.
    public sealed class PlaytestAnnotationCapture
    {
        public string MarkerId;
        public PlaytestTimestamp Time;
        public byte[] FramePng;
        public byte[] AnnotationPng;
        public byte[] CombinedPng;
        public List<PlaytestScreenRegion> CircledRegions = new List<PlaytestScreenRegion>();
    }
}
