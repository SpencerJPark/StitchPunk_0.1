// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Makes a transport caption the drag handle for the field beside it, via a real
    /// <see cref="FieldMouseDragger{T}"/> so sensitivity and modifiers match Unity's own fields.
    /// </summary>
    public static class CaptionDragHandle
    {
        private const string DraggableCaptionClassName = "toolkit-transport__caption--draggable";

        public static void Attach<TValue>(VisualElement caption, TextValueField<TValue> field)
        {
            if (caption == null || field == null)
            {
                return;
            }

            new FieldMouseDragger<TValue>(field).SetDragZone(caption);
            caption.AddToClassList(DraggableCaptionClassName);

            if (!field.isDelayed)
            {
                return;
            }
            // Lifted for the drag so the value moves live, then restored — typing still needs it delayed.
            caption.RegisterCallback<PointerDownEvent>(downEvent => field.isDelayed = false);

            // PointerCaptureOut rather than PointerUp: the dragger captures the caption, and a
            // capture lost any other way still has to put the field back, or typing quietly loses
            // its guard for the rest of the session.
            caption.RegisterCallback<PointerCaptureOutEvent>(
                captureOutEvent => field.isDelayed = true);
        }
    }
}
