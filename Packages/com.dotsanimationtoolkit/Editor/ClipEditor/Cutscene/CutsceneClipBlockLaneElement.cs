// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One block, as the lane displays it: what it is called, when it starts, how long, and whether it loops.</summary>
    public readonly struct CutsceneClipBlockDisplay
    {
        public readonly string label;
        public readonly float start;
        public readonly float duration;
        public readonly bool loop;

        public CutsceneClipBlockDisplay(string label, float start, float duration, bool loop)
        {
            this.label = label;
            this.start = start;
            this.duration = duration;
            this.loop = loop;
        }
    }

    /// <summary>
    /// A slot's clip lane (Phase G spec §2): named blocks the author drags to move or resize.
    /// Overlap between two blocks <em>is</em> the crossfade window and touching blocks are a hard
    /// cut — both read straight off <see cref="CutsceneClipBlockDisplay.start"/>/<c>duration</c>, so
    /// this element paints the overlap and authors nothing else about it.
    /// </summary>
    public sealed class CutsceneClipBlockLaneElement : VisualElement
    {
        public const string UssClassName = "cutscene-editor__clip-lane";
        private const string BlockUssClassName = "cutscene-editor__clip-block";
        private const string SelectedBlockUssClassName = "cutscene-editor__clip-block--selected";
        private const string LoopBlockUssClassName = "cutscene-editor__clip-block--loop";
        private const float ResizeHandleWidth = 6f;
        private const float DragThresholdPixels = 3f;

        private enum DragKind { None, Move, ResizeStart, ResizeEnd }

        private readonly List<VisualElement> blockElements = new List<VisualElement>();
        private readonly List<CutsceneClipBlockDisplay> blocks = new List<CutsceneClipBlockDisplay>();
        private int selectedIndex = -1;

        private DragKind dragKind = DragKind.None;
        private int dragIndex = -1;
        private float dragStartPointerX;
        private float dragStartStart;
        private float dragStartDuration;
        private bool draggedPastThreshold;

        public float pixelsPerSecond = 40f;

        /// <summary>Minimum block length a resize may leave behind — a zero-length block has no meaningful crossfade math.</summary>
        public float minimumDurationSeconds = 0.05f;

        public event Action<int> BlockSelected;
        public event Action<int, float, float> BlockChanged; // live, during drag: index, start, duration
        public event Action<int, float, float> BlockChangeCommitted; // index, start, duration
        public event Action<float> EmptySpaceDoubleClicked;
        public event Action<int> BlockDeleteRequested;

        public CutsceneClipBlockLaneElement()
        {
            AddToClassList(UssClassName);
            RegisterCallback<PointerDownEvent>(OnBackgroundPointerDown);
        }

        public void SetBlocks(IReadOnlyList<CutsceneClipBlockDisplay> newBlocks, int newSelectedIndex)
        {
            blocks.Clear();
            if (newBlocks != null)
            {
                blocks.AddRange(newBlocks);
            }
            selectedIndex = newSelectedIndex;
            Rebuild();
        }

        private void Rebuild()
        {
            Clear();
            blockElements.Clear();

            for (int index = 0; index < blocks.Count; index++)
            {
                int capturedIndex = index;
                VisualElement block = new VisualElement();
                block.AddToClassList(BlockUssClassName);
                block.EnableInClassList(SelectedBlockUssClassName, capturedIndex == selectedIndex);
                block.EnableInClassList(LoopBlockUssClassName, blocks[capturedIndex].loop);
                block.style.position = Position.Absolute;
                block.style.top = 2f;
                PositionBlock(block, blocks[capturedIndex]);

                Label label = new Label(blocks[capturedIndex].label);
                label.pickingMode = PickingMode.Ignore;
                label.style.overflow = Overflow.Hidden;
                label.style.fontSize = 10f;
                label.style.paddingLeft = 3f;
                block.Add(label);

                VisualElement startHandle = new VisualElement();
                startHandle.style.position = Position.Absolute;
                startHandle.style.left = 0f;
                startHandle.style.top = 0f;
                startHandle.style.bottom = 0f;
                startHandle.style.width = ResizeHandleWidth;
                block.Add(startHandle);

                VisualElement endHandle = new VisualElement();
                endHandle.style.position = Position.Absolute;
                endHandle.style.right = 0f;
                endHandle.style.top = 0f;
                endHandle.style.bottom = 0f;
                endHandle.style.width = ResizeHandleWidth;
                block.Add(endHandle);

                startHandle.RegisterCallback<PointerDownEvent>(pointerEvent =>
                    BeginDrag(pointerEvent, capturedIndex, block, DragKind.ResizeStart));
                endHandle.RegisterCallback<PointerDownEvent>(pointerEvent =>
                    BeginDrag(pointerEvent, capturedIndex, block, DragKind.ResizeEnd));
                block.RegisterCallback<PointerDownEvent>(pointerEvent =>
                    BeginDrag(pointerEvent, capturedIndex, block, DragKind.Move));
                block.RegisterCallback<PointerMoveEvent>(pointerEvent =>
                    OnDragMove(pointerEvent, capturedIndex, block));
                block.RegisterCallback<PointerUpEvent>(pointerEvent =>
                    OnDragEnd(pointerEvent, capturedIndex, block));
                block.AddManipulator(new ContextualMenuManipulator(
                    menuEvent => menuEvent.menu.AppendAction(
                        "Delete", _ => BlockDeleteRequested?.Invoke(capturedIndex))));

                Add(block);
                blockElements.Add(block);
            }
        }

        private void PositionBlock(VisualElement block, CutsceneClipBlockDisplay display)
        {
            CutsceneTimelineGeometry geometry = CutsceneTimelineGeometry.Create(pixelsPerSecond);
            block.style.left = geometry.TimeToX(display.start);
            block.style.width = Mathf.Max(2f, display.duration * geometry.pixelsPerSecond);
        }

        private void BeginDrag(PointerDownEvent pointerEvent, int index, VisualElement block, DragKind kind)
        {
            block.CapturePointer(pointerEvent.pointerId);
            dragKind = kind;
            dragIndex = index;
            draggedPastThreshold = false;
            dragStartPointerX = pointerEvent.position.x;
            dragStartStart = blocks[index].start;
            dragStartDuration = blocks[index].duration;
            pointerEvent.StopPropagation();
        }

        private void OnDragMove(PointerMoveEvent moveEvent, int index, VisualElement block)
        {
            if (dragIndex != index || dragKind == DragKind.None || !block.HasPointerCapture(moveEvent.pointerId))
            {
                return;
            }

            float deltaPixels = moveEvent.position.x - dragStartPointerX;
            if (!draggedPastThreshold && Mathf.Abs(deltaPixels) < DragThresholdPixels)
            {
                return;
            }
            draggedPastThreshold = true;

            float deltaSeconds = deltaPixels / CutsceneTimelineGeometry.Create(pixelsPerSecond).pixelsPerSecond;
            float newStart = blocks[index].start;
            float newDuration = blocks[index].duration;

            switch (dragKind)
            {
                case DragKind.Move:
                    newStart = Mathf.Max(0f, dragStartStart + deltaSeconds);
                    break;
                case DragKind.ResizeStart:
                    newStart = Mathf.Min(
                        dragStartStart + dragStartDuration - minimumDurationSeconds,
                        Mathf.Max(0f, dragStartStart + deltaSeconds));
                    newDuration = dragStartStart + dragStartDuration - newStart;
                    break;
                case DragKind.ResizeEnd:
                    newDuration = Mathf.Max(minimumDurationSeconds, dragStartDuration + deltaSeconds);
                    break;
            }

            blocks[index] = new CutsceneClipBlockDisplay(blocks[index].label, newStart, newDuration, blocks[index].loop);
            PositionBlock(block, blocks[index]);
            BlockChanged?.Invoke(index, newStart, newDuration);
        }

        private void OnDragEnd(PointerUpEvent upEvent, int index, VisualElement block)
        {
            if (dragIndex != index)
            {
                return;
            }
            block.ReleasePointer(upEvent.pointerId);
            DragKind endedKind = dragKind;
            dragKind = DragKind.None;
            dragIndex = -1;

            if (draggedPastThreshold)
            {
                BlockChangeCommitted?.Invoke(index, blocks[index].start, blocks[index].duration);
            }
            else if (endedKind == DragKind.Move)
            {
                BlockSelected?.Invoke(index);
            }
        }

        private void OnBackgroundPointerDown(PointerDownEvent pointerEvent)
        {
            if (pointerEvent.target != this)
            {
                return;
            }

            if (pointerEvent.clickCount >= 2)
            {
                float time = CutsceneTimelineGeometry.Create(pixelsPerSecond)
                    .XToTime(pointerEvent.localPosition.x);
                EmptySpaceDoubleClicked?.Invoke(time);
            }
            else
            {
                BlockSelected?.Invoke(-1);
            }
        }
    }
}
