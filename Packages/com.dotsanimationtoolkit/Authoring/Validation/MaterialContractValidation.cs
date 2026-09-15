// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    [Flags]
    public enum TargetKindMask : byte
    {
        None = 0,
        Quad = 1 << 0,
        VatMesh = 1 << 1,
        FlipbookPlane = 1 << 2
    }

    public enum ContractPropertyState : byte
    {
        Present = 0,
        Missing = 1,
        PresentButNotNeeded = 2,
        CoveredByAlternative = 3,
        NotNeeded = 4
    }

    public readonly struct ContractProperty
    {
        public readonly string name;
        public readonly TargetKindMask requiredFor;

        // Non-null: any one present property sharing this group satisfies the requirement for every member.
        public readonly string alternativeGroup;

        public ContractProperty(string name, TargetKindMask requiredFor, string alternativeGroup)
        {
            this.name = name;
            this.requiredFor = requiredFor;
            this.alternativeGroup = alternativeGroup;
        }
    }

    public readonly struct ContractPropertyStatus
    {
        public readonly ContractProperty property;
        public readonly ContractPropertyState state;

        public ContractPropertyStatus(ContractProperty property, ContractPropertyState state)
        {
            this.property = property;
            this.state = state;
        }
    }

    /// <summary>The shader contract as data: which per-instance properties a material must declare for each target kind.</summary>
    public static class MaterialContractValidation
    {
        public const string TextureArraySamplerPropertyName = "_MainTexArray";

        private const string FlipbookFrameGroup = "FlipbookFrame";

        private static readonly ContractProperty[] PropertyTable = new ContractProperty[]
        {
            new ContractProperty("_ImageIndex", TargetKindMask.FlipbookPlane, FlipbookFrameGroup),
            new ContractProperty("_AtlasFrame", TargetKindMask.FlipbookPlane, FlipbookFrameGroup),
            new ContractProperty("_VatFrameA", TargetKindMask.VatMesh, null),
            new ContractProperty("_VatFrameB", TargetKindMask.VatMesh, null),
            new ContractProperty("_VatBlend", TargetKindMask.VatMesh, null),

            // The host writes _BillboardParams and the package never does, so no kind requires it.
            new ContractProperty("_BillboardParams", TargetKindMask.None, null),
        };

        public static IReadOnlyList<ContractProperty> Properties
        {
            get { return PropertyTable; }
        }

        public static TargetKindMask MaskFor(TargetKind kind)
        {
            switch (kind)
            {
                case TargetKind.VatMesh:
                    return TargetKindMask.VatMesh;
                case TargetKind.FlipbookPlane:
                    return TargetKindMask.FlipbookPlane;
                default:
                    return TargetKindMask.Quad;
            }
        }

        public static void EvaluateProperties(Material material, TargetKind kind, List<ContractPropertyStatus> output)
        {
            if (material == null)
            {
                return;
            }

            TargetKindMask kindMask = MaskFor(kind);

            for (int propertyIndex = 0; propertyIndex < PropertyTable.Length; propertyIndex++)
            {
                ContractProperty property = PropertyTable[propertyIndex];
                bool required = (property.requiredFor & kindMask) != 0;
                bool present = material.HasProperty(property.name);

                ContractPropertyState state;
                if (required && present)
                {
                    state = ContractPropertyState.Present;
                }
                else if (required && !present)
                {
                    state = HasPresentAlternative(material, property, kindMask)
                        ? ContractPropertyState.CoveredByAlternative
                        : ContractPropertyState.Missing;
                }
                else if (!required && present)
                {
                    state = ContractPropertyState.PresentButNotNeeded;
                }
                else
                {
                    state = ContractPropertyState.NotNeeded;
                }

                output.Add(new ContractPropertyStatus(property, state));
            }
        }

        private static bool HasPresentAlternative(Material material, ContractProperty property, TargetKindMask kindMask)
        {
            if (property.alternativeGroup == null)
            {
                return false;
            }

            for (int propertyIndex = 0; propertyIndex < PropertyTable.Length; propertyIndex++)
            {
                ContractProperty otherProperty = PropertyTable[propertyIndex];
                if (otherProperty.name == property.name)
                {
                    continue;
                }

                bool sameGroup = otherProperty.alternativeGroup == property.alternativeGroup;
                bool otherRequired = (otherProperty.requiredFor & kindMask) != 0;
                if (sameGroup && otherRequired && material.HasProperty(otherProperty.name))
                {
                    return true;
                }
            }

            return false;
        }

        public static void Validate(Material material, TargetKind kind, string partName, List<ValidationMessage> output)
        {
            if (material == null)
            {
                return;
            }

            List<ContractPropertyStatus> statuses = new List<ContractPropertyStatus>();
            EvaluateProperties(material, kind, statuses);

            HashSet<string> reportedGroups = new HashSet<string>();

            for (int statusIndex = 0; statusIndex < statuses.Count; statusIndex++)
            {
                ContractPropertyStatus status = statuses[statusIndex];
                if (status.state != ContractPropertyState.Missing)
                {
                    continue;
                }

                string group = status.property.alternativeGroup;
                if (group == null)
                {
                    output.Add(new ValidationMessage(
                        ValidationSeverity.Error,
                        ValidationCode.None,
                        material,
                        "Material '" + material.name + "' on part '" + partName + "' declares no " + status.property.name
                            + ", which a " + kind.ToString() + " part needs."));
                    continue;
                }

                if (!reportedGroups.Add(group))
                {
                    continue;
                }

                if (!AllGroupMembersMissing(statuses, group))
                {
                    continue;
                }

                List<string> groupMemberNames = new List<string>();
                for (int statusIndex2 = 0; statusIndex2 < statuses.Count; statusIndex2++)
                {
                    if (statuses[statusIndex2].property.alternativeGroup == group)
                    {
                        groupMemberNames.Add(statuses[statusIndex2].property.name);
                    }
                }

                output.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.None,
                    material,
                    "Material '" + material.name + "' on part '" + partName + "' declares no "
                        + string.Join(" or ", groupMemberNames) + ", which a " + kind.ToString() + " part needs."));
            }

            if (!material.enableInstancing)
            {
                output.Add(new ValidationMessage(
                    ValidationSeverity.Error,
                    ValidationCode.None,
                    material,
                    "Material '" + material.name + "' on part '" + partName + "' has GPU instancing off; Entities Graphics needs it on."));
            }
        }

        private static bool AllGroupMembersMissing(List<ContractPropertyStatus> statuses, string group)
        {
            bool sawGroupMember = false;

            for (int statusIndex = 0; statusIndex < statuses.Count; statusIndex++)
            {
                ContractPropertyStatus status = statuses[statusIndex];
                if (status.property.alternativeGroup != group)
                {
                    continue;
                }

                sawGroupMember = true;
                if (status.state != ContractPropertyState.Missing)
                {
                    return false;
                }
            }

            return sawGroupMember;
        }
    }
}
