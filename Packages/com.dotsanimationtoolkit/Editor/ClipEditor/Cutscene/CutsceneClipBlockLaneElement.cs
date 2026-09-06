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
    /// A slot's clip lane: named blocks the author drags to move or resize. Overlap between two
    /// blocks is the crossfade window and touching blocks are a hard cut — both read straight off
    /// <see cref="CutsceneClipBlockDisplay.start"/>/<c>duration</c>.
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
        private readonly List<float> dragStartStarts = new List<float>();
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

        // Asked per block on every rebuild and every in-place refresh, so a lane draws the panel's
        // whole selection set rather than the one index it was handed. Null means "only selectedIndex".
        /// <summary>Whether the block at an index is in the panel's selection.</summary>
        public Func<int, bool> isItemSelected;

        public event Action<int> BlockSelected;
        public event Action<int, float, float> BlockChanged; // live, during drag: index, start, duration
        public event Action<int, float, float> BlockChangeCommitted; // index, start, duration
        public event Action<float> EmptySpaceDoubleClicked;
        public event Action<int> BlockDeleteRequested;

        /// <summary>Raised on a block press, before any drag, with (index, toggles the item, adds to the set).</summary>
        public event Action<int, bool, bool> BlockPointerDown;

        /// <summary>Raised on every pointer move while moving a block, with the drag's total time delta. Never raised by a resize.</summary>
        public event Action<float> SelectionDragMoved;

        /// <summary>Raised once on release of a move, with the final time delta to write across the selection.</summary>
        public event Action<float> SelectionDragCommitted;

        /// <summary>Raised on a press in empty lane space, handing over the event so the panel can start a band drag.</summary>
        public event Action<PointerDownEvent> BackgroundPointerDown;

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
                block.EnableInClassList(SelectedBlockUssClassName, IsSelected(capturedIndex));
                block.EnableInClassList(LoopBlockUssClassName, blocks[capturedIndex].loop);
                block.style.position = Position.Absolute;
                block.style.top = 2f;
                block.style.bottom = 2f;
                PositionBlock(block, blocks[capturedIndex]);

                // The loop glyph rides the label, not a style: a looping walk reads as "Walk ⟳" at
                // any zoom, where a subtle tint alone did not.
                Label label = new Label(
                    blocks[capturedIndex].label + (blocks[capturedIndex].loop ? "  ⟳" : string.Empty));
                label.AddToClassList("cutscene-editor__clip-block-label");
                label.pickingMode = PickingMode.Ignore;
                label.style.overflow = Overflow.Hidden;
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

        private bool IsSelected(int index)
        {
            return isItemSelected != null ? isItemSelected(index) : index == selectedIndex;
        }

        /// <summary>Repaints which blocks look selected, without tearing the lane down mid-gesture.</summary>
        public void RefreshSelectionVisuals()
        {
            for (int index = 0; index < blockElements.Count; index++)
            {
                blockElements[index].EnableInClassList(SelectedBlockUssClassName, IsSelected(index));
            }
        }

        /// <summary>Draws every selected block shifted by a delta, previewing a group drag before anything is written.</summary>
        public void PreviewOffsetForSelected(float deltaSeconds)
        {
            if (dragStartStarts.Count != blocks.Count)
            {
                CaptureDragStartStarts();
            }
            for (int index = 0; index < blockElements.Count && index < dragStartStarts.Count; index++)
            {
                if (!IsSelected(index))
                {
                    continue;
                }
                CutsceneClipBlockDisplay display = blocks[index];
                blocks[index] = new CutsceneClipBlockDisplay(
                    display.label, Mathf.Max(0f, dragStartStarts[index] + deltaSeconds),
                    display.duration, display.loop);
                PositionBlock(blockElements[index], blocks[index]);
            }
        }

        /// <summary>Every block whose span overlaps a band given in this lane's own space.</summary>
        public void CollectItemsInBand(Rect bandInLaneSpace, List<int> collected)
        {
            if (collected == null)
            {
                return;
            }
            CutsceneTimelineGeometry geometry = CutsceneTimelineGeometry.Create(pixelsPerSecond);
            for (int index = 0; index < blocks.Count; index++)
            {
                float blockLeft = geometry.TimeToX(blocks[index].start);
                float blockRight = geometry.TimeToX(blocks[index].start + blocks[index].duration);
                if (blockRight >= bandInLaneSpace.xMin && blockLeft <= bandInLaneSpace.xMax)
                {
                    collected.Add(index);
                }
            }
        }

        // The pre-drag starts, so every preview frame offsets from where the group started rather
        // than from where the previous frame left it.
        private void CaptureDragStartStarts()
        {
            dragStartStarts.Clear();
            for (int index = 0; index < blocks.Count; index++)
            {
                dragStartStarts.Add(blocks[index].start);
            }
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
            CaptureDragStartStarts();

            // Selection resolves on press, not on release: a drag has to know what it is moving
            // before it starts moving it, and the panel answers isItemSelected out of that set.
            BlockPointerDown?.Invoke(
                index,
                pointerEvent.ctrlKey || pointerEvent.commandKey,
                pointerEvent.shiftKey);
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
                    // A resize stays single-item by design; only a move carries the rest of the set.
                    SelectionDragMoved?.Invoke(Mathf.Max(deltaSeconds, -dragStartStart));
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
                if (endedKind == DragKind.Move)
                {
                    SelectionDragCommitted?.Invoke(
                        Mathf.Max(blocks[index].start - dragStartStart, -dragStartStart));
                    return;
                }
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
                return;
            }

            BackgroundPointerDown?.Invoke(pointerEvent);
            BlockSelected?.Invoke(-1);
        }
    }
}
