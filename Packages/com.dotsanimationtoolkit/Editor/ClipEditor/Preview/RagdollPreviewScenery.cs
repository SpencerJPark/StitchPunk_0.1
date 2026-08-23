// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The shape a drop-in preview prop presents to <see cref="RagdollPreviewProbe"/> (Phase D6,
    /// spec §8.6).
    /// </summary>
    public enum RagdollPreviewPropShape : byte
    {
        /// <summary>A flat platform: one contact plane, at the prop's own +Y face.</summary>
        Box = 0,

        /// <summary>
        /// A slanted platform: the same single contact plane as <see cref="Box"/>, oriented by the
        /// prop's own rotation rather than assumed horizontal. See <see cref="RagdollPreviewProbe"/>'s
        /// remarks for why a ramp is not modelled as a second, differently-shaped collider.
        /// </summary>
        Ramp = 1
    }

    /// <summary>
    /// One piece of drop-in test scenery (Phase D6, spec §8.6): a box or ramp a rig can be dropped
    /// onto to judge whether a pose still reads on impact.
    /// </summary>
    [Serializable]
    public sealed class RagdollPreviewPropDefinition
    {
        /// <summary>Cosmetic label shown in the scenery list.</summary>
        public string displayName = "Prop";

        /// <summary>Which contact <see cref="RagdollPreviewProbe"/> derives from this prop.</summary>
        public RagdollPreviewPropShape shape = RagdollPreviewPropShape.Box;

        /// <summary>World-space centre.</summary>
        public float3 position;

        /// <summary>Full size, local to the prop's own rotation.</summary>
        public float3 size = new float3(2f, 1f, 2f);

        /// <summary>World-space rotation, in degrees, applied ZXY (matching <c>TransformKey</c>).</summary>
        public float3 eulerAngles;

        /// <summary>Whether this prop currently takes part in the preview drop.</summary>
        public bool enabled = true;
    }

    /// <summary>
    /// Editor-only, project-wide scenery for the Ragdoll preview toggle (Phase D6, spec §8.6): a
    /// ground plane at y = 0, always present and not authored here, plus whatever drop-in props the
    /// user has placed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Never on the rig asset.</strong> A shipped rig must not carry a test box, for the same
    /// reason <c>SocketDefinition.previewAttachment</c> sits inside <c>#if UNITY_EDITOR</c> — test
    /// scenery is a fact about one developer's workstation, not about the character. A
    /// <see cref="ScriptableSingleton{T}"/> under <c>ProjectSettings/</c> is reproducible across a
    /// team (it can be checked in like any other project setting) without ever touching an asset a
    /// game ships.
    /// </para>
    /// <para>
    /// <strong>One singleton for the whole project, not one per rig.</strong> Test scenery answers
    /// "does a drop read as a fall" for whatever rig is currently open in the Clip Editor; there is
    /// nothing rig-specific about a floor and a box to drop onto, so a single shared list avoids a
    /// scenery asset nobody remembers exists for every rig in a project.
    /// </para>
    /// </remarks>
    [FilePath(
        "ProjectSettings/DotsAnimationToolkitRagdollPreviewScenery.asset",
        FilePathAttribute.Location.ProjectFolder)]
    public sealed class RagdollPreviewScenery : ScriptableSingleton<RagdollPreviewScenery>
    {
        /// <summary>World-space height of the always-present ground plane. Not authored: spec §8.6 names it fixed at y = 0.</summary>
        public const float GroundHeight = 0f;

        /// <summary>The drop-in props, in no particular order.</summary>
        [SerializeField] private List<RagdollPreviewPropDefinition> props = new List<RagdollPreviewPropDefinition>();

        /// <summary>The drop-in props, in no particular order.</summary>
        public List<RagdollPreviewPropDefinition> Props
        {
            get { return props; }
        }

        /// <summary>Adds a default box prop and persists the change.</summary>
        public RagdollPreviewPropDefinition AddBoxProp()
        {
            RagdollPreviewPropDefinition prop = new RagdollPreviewPropDefinition
            {
                displayName = "Box " + (props.Count + 1).ToString(),
                shape = RagdollPreviewPropShape.Box,
                position = new float3(0f, 0.5f, 0f),
                size = new float3(2f, 1f, 2f),
                eulerAngles = float3.zero
            };
            props.Add(prop);
            Save(true);
            return prop;
        }

        /// <summary>Removes a prop and persists the change.</summary>
        public void RemoveProp(RagdollPreviewPropDefinition prop)
        {
            if (props.Remove(prop))
            {
                Save(true);
            }
        }

        /// <summary>Persists whatever is currently in <see cref="Props"/>.</summary>
        public void PersistChange()
        {
            Save(true);
        }
    }
}
