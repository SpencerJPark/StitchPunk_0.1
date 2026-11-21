// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Builds the Flipbook field and by-name Frame popups a bound sprite track shows instead of raw
    /// slice numbers.
    /// </summary>
    public static class FlipbookFramePickerBuilder
    {
        public const string AtlasTrackHint = "Slice tracks bind flipbooks; bind a sheet flipbook here";
        public const string NoFrameChoice = "(no frame)";

        public static ObjectField BuildFlipbookField(SpriteTrack track, Action<FlipbookAsset> flipbookPicked)
        {
            ObjectField flipbookField = new ObjectField("Flipbook");
            flipbookField.objectType = typeof(UnityEngine.Object);
            flipbookField.allowSceneObjects = false;
            flipbookField.SetValueWithoutNotify(track.flipbook);
            flipbookField.tooltip =
                "The flipbook or Texture2DArray this track's frames are picked from. Dropping an array reuses or creates the flipbook that names its layers. Authoring-only; never baked.";
            if (track.mode == SpriteFrameMode.AtlasRect)
            {
                flipbookField.SetEnabled(false);
            }
            flipbookField.RegisterValueChangedCallback(changeEvent =>
            {
                if (changeEvent.newValue == null)
                {
                    flipbookPicked(null);
                    return;
                }
                FlipbookAsset pickedFlipbook = changeEvent.newValue as FlipbookAsset;
                if (pickedFlipbook == null && changeEvent.newValue is Texture2DArray droppedArray)
                {
                    pickedFlipbook = FlipbookAssetUtility.GetOrCreateFlipbookForArray(droppedArray);
                }
                if (pickedFlipbook == null)
                {
                    flipbookField.SetValueWithoutNotify(track.flipbook);
                    return;
                }
                if (flipbookField.value != pickedFlipbook)
                {
                    flipbookField.SetValueWithoutNotify(pickedFlipbook);
                    flipbookPicked(pickedFlipbook);
                }
            });
            return flipbookField;
        }

        public static ObjectField BuildSheetFlipbookField(SpriteTrack track, Action<SheetFlipbookAsset> sheetFlipbookPicked)
        {
            ObjectField sheetField = new ObjectField("Sheet Flipbook");
            sheetField.objectType = typeof(SheetFlipbookAsset);
            sheetField.allowSceneObjects = false;
            sheetField.SetValueWithoutNotify(track.sheetFlipbook);
            sheetField.tooltip =
                "The sheet flipbook this atlas-rect track's frames are picked from. Authoring-only; never baked.";
            sheetField.RegisterValueChangedCallback(changeEvent =>
            {
                sheetFlipbookPicked(changeEvent.newValue as SheetFlipbookAsset);
            });
            return sheetField;
        }

        public static PopupField<string> BuildSheetFramePopup(
            string label, SheetFlipbookAsset sheetFlipbook, Unity.Mathematics.float4 currentAtlasRect,
            Action<int> frameIndexPicked)
        {
            List<string> choices = new List<string>();
            int currentFrameIndex = -1;
            if (sheetFlipbook != null && sheetFlipbook.frames != null)
            {
                for (int frameIndex = 0; frameIndex < sheetFlipbook.frames.Count; frameIndex++)
                {
                    choices.Add(sheetFlipbook.frames[frameIndex].name);
                    if (currentFrameIndex < 0
                        && Unity.Mathematics.math.all(sheetFlipbook.AtlasRectForFrame(frameIndex) == currentAtlasRect))
                    {
                        currentFrameIndex = frameIndex;
                    }
                }
            }
            choices.Add(NoFrameChoice);

            string currentChoice = currentFrameIndex >= 0 ? choices[currentFrameIndex] : NoFrameChoice;
            PopupField<string> framePopup = new PopupField<string>(label, choices, currentChoice);
            framePopup.RegisterValueChangedCallback(changeEvent =>
            {
                int pickedFrameIndex = choices.IndexOf(changeEvent.newValue);
                if (pickedFrameIndex >= 0 && pickedFrameIndex < choices.Count - 1)
                {
                    frameIndexPicked(pickedFrameIndex);
                }
            });
            return framePopup;
        }

        public static PopupField<string> BuildFramePopup(
            string label, FlipbookAsset flipbook, int layerIndex, Action<int> layerIndexPicked)
        {
            List<string> choices = new List<string>();
            if (flipbook != null && flipbook.frames != null)
            {
                foreach (FlipbookFrame frame in flipbook.frames)
                {
                    if (frame != null)
                    {
                        choices.Add(frame.name);
                    }
                }
            }
            choices.Add(NoFrameChoice);

            FlipbookFrame currentFrame = flipbook != null ? flipbook.FindFrameByLayerIndex(layerIndex) : null;
            string currentChoice = currentFrame != null ? currentFrame.name : NoFrameChoice;

            PopupField<string> framePopup = new PopupField<string>(label, choices, currentChoice);
            framePopup.RegisterValueChangedCallback(changeEvent =>
            {
                if (changeEvent.newValue == NoFrameChoice)
                {
                    return;
                }
                FlipbookFrame pickedFrame = FindFrameByName(flipbook, changeEvent.newValue);
                if (pickedFrame != null)
                {
                    layerIndexPicked(pickedFrame.index);
                }
            });
            return framePopup;
        }

        public static void SetFramePopupLayerIndex(
            PopupField<string> framePopup, FlipbookAsset flipbook, int layerIndex)
        {
            FlipbookFrame currentFrame = flipbook != null ? flipbook.FindFrameByLayerIndex(layerIndex) : null;
            framePopup.SetValueWithoutNotify(currentFrame != null ? currentFrame.name : NoFrameChoice);
        }

        private static FlipbookFrame FindFrameByName(FlipbookAsset flipbook, string frameName)
        {
            if (flipbook == null || flipbook.frames == null)
            {
                return null;
            }
            for (int listPosition = 0; listPosition < flipbook.frames.Count; listPosition++)
            {
                FlipbookFrame frame = flipbook.frames[listPosition];
                if (frame != null && frame.name == frameName)
                {
                    return frame;
                }
            }
            return null;
        }
    }
}
