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

        private static readonly ContractProperty[] PropertyTable = new ContractProperty[0];

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
        }

        public static void Validate(Material material, TargetKind kind, string partName, List<ValidationMessage> output)
        {
        }
    }
}
