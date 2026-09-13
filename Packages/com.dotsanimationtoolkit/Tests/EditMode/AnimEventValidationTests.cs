// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers the registry-membership and named-value rules of the shared event-marker rule
    /// table, since those two depend on caller-supplied lookups rather than fixed constants.
    /// </summary>
    public sealed class AnimEventValidationTests
    {
        [Test]
        public void KeyAbsentFromRegistry_IsAnError()
        {
            List<(uint key, int intParam, float floatParam, float windowSeconds)> markers =
                new List<(uint key, int intParam, float floatParam, float windowSeconds)>
                {
                    (20u, 0, 0f, 0f)
                };
            List<ValidationMessage> output = new List<ValidationMessage>();

            AnimEventValidation.ValidateMarkers(
                markers,
                eventKey => false,
                null,
                null,
                "clip 'Walk'",
                output);

            Assert.AreEqual(1, output.Count);
            Assert.AreEqual(ValidationCode.V41, output[0].code);
            Assert.AreEqual(ValidationSeverity.Error, output[0].severity);
        }

        [Test]
        public void IntParamOutsideValueNames_IsAWarning()
        {
            List<ValidationMessage> output = new List<ValidationMessage>();
            IReadOnlyList<string> namedValues = new List<string> { "Light", "Heavy" };

            AnimEventValidation.ValidateMarker(
                0,
                20u,
                3,
                0f,
                eventKey => true,
                eventKey => namedValues,
                null,
                "clip 'Walk'",
                output);

            Assert.AreEqual(1, output.Count);
            Assert.AreEqual(ValidationCode.V42, output[0].code);
            Assert.AreEqual(ValidationSeverity.Warning, output[0].severity);
        }
    }
}
