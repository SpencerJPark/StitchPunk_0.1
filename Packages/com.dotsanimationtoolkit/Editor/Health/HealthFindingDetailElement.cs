// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Health tab's right panel: the selected finding's title, explanation, affected assets and fix actions.</summary>
    public sealed class HealthFindingDetailElement : VisualElement
    {
        public event Action<HealthFinding, HealthFindingAction> ActionRan;

        public HealthFindingDetailElement()
        {
            name = "health-finding-detail";
            style.flexGrow = 1f;
        }

        public void SetFinding(HealthFinding finding)
        {
        }

        private void RaiseActionRan(HealthFinding finding, HealthFindingAction action)
        {
            ActionRan?.Invoke(finding, action);
        }
    }
}
