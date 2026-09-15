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
    /// Builds the Sheet field and by-name Frame popups a bound sprite track shows instead of raw
    /// slice numbers.
    /// </summary>
    public static class SpriteSheetFramePickerBuilder
    {
        public const string AtlasTrackHint = "sheets bind Slice tracks";
        public const string NoFrameChoice = "(no frame)";

        public static ObjectField BuildSheetField(SpriteTrack track, Action<SpriteSheetAsset> sheetPicked)
        {
            ObjectField sheetField = new ObjectField("Sheet");
            sheetField.objectType = typeof(UnityEngine.Object);
            sheetField.allowSceneObjects = false;
            sheetField.SetValueWithoutNotify(track.sheet);
            sheetField.tooltip =
                "The sprite sheet or Texture2DArray this track's frames are picked from. Dropping an array reuses or creates its names sheet. Authoring-only; never baked.";
            if (track.mode == SpriteFrameMode.AtlasRect)
            {
                sheetField.SetEnabled(false);
            }
            sheetField.RegisterValueChangedCallback(changeEvent =>
            {
                if (changeEvent.newValue == null)
                {
                    sheetPicked(null);
                    return;
                }
                SpriteSheetAsset pickedSheet = changeEvent.newValue as SpriteSheetAsset;
                if (pickedSheet == null && changeEvent.newValue is Texture2DArray droppedArray)
                {
                    pickedSheet = SpriteSheetAssetUtility.GetOrCreateSheetForArray(droppedArray);
                }
                if (pickedSheet == null)
                {
                    sheetField.SetValueWithoutNotify(track.sheet);
                    return;
                }
                if (sheetField.value != pickedSheet)
                {
                    sheetField.SetValueWithoutNotify(pickedSheet);
                    sheetPicked(pickedSheet);
                }
            });
            return sheetField;
        }

        public static PopupField<string> BuildFramePopup(
            string label, SpriteSheetAsset sheet, int layerIndex, Action<int> layerIndexPicked)
        {
            List<string> choices = new List<string>();
            if (sheet != null && sheet.frames != null)
            {
                foreach (SpriteSheetFrame frame in sheet.frames)
                {
                    if (frame != null)
                    {
                        choices.Add(frame.name);
                    }
                }
            }
            choices.Add(NoFrameChoice);

            SpriteSheetFrame currentFrame = sheet != null ? sheet.FindFrameByLayerIndex(layerIndex) : null;
            string currentChoice = currentFrame != null ? currentFrame.name : NoFrameChoice;

            PopupField<string> framePopup = new PopupField<string>(label, choices, currentChoice);
            framePopup.RegisterValueChangedCallback(changeEvent =>
            {
                if (changeEvent.newValue == NoFrameChoice)
                {
                    return;
                }
                SpriteSheetFrame pickedFrame = FindFrameByName(sheet, changeEvent.newValue);
                if (pickedFrame != null)
                {
                    layerIndexPicked(pickedFrame.index);
                }
            });
            return framePopup;
        }

        public static void SetFramePopupLayerIndex(
            PopupField<string> framePopup, SpriteSheetAsset sheet, int layerIndex)
        {
            SpriteSheetFrame currentFrame = sheet != null ? sheet.FindFrameByLayerIndex(layerIndex) : null;
            framePopup.SetValueWithoutNotify(currentFrame != null ? currentFrame.name : NoFrameChoice);
        }

        private static SpriteSheetFrame FindFrameByName(SpriteSheetAsset sheet, string frameName)
        {
            if (sheet == null || sheet.frames == null)
            {
                return null;
            }
            for (int listPosition = 0; listPosition < sheet.frames.Count; listPosition++)
            {
                SpriteSheetFrame frame = sheet.frames[listPosition];
                if (frame != null && frame.name == frameName)
                {
                    return frame;
                }
            }
            return null;
        }
    }
}
