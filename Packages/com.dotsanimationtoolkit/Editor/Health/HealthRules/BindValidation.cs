// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Health rules that absorb the old Clip Editor badge: each profile's clip sets bound to its rig, and shared clips still bound by target id.</summary>
    public static class BindValidation
    {
        public static void EvaluateProfileBinds(HealthScanContext context, List<HealthFinding> output)
        {
            List<ValidationMessage> emittedMessages = new List<ValidationMessage>();
            for (int profileIndex = 0; profileIndex < context.profiles.Count; profileIndex++)
            {
                ActorProfileAsset profile = context.profiles[profileIndex];
                if (profile == null || profile.rig == null || profile.clipSets == null)
                {
                    continue;
                }

                List<ValidationMessage> messages = ClipValidation.ValidateBind(
                    profile.rig,
                    profile.clipSets,
                    tagRegistry: context.targetTags,
                    eventKeyRegistry: context.eventKeys);

                for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
                {
                    ValidationMessage message = messages[messageIndex];
                    if (IsCodeReportedByAnotherRule(message.code))
                    {
                        continue;
                    }

                    if (IsDuplicateMessage(emittedMessages, message))
                    {
                        continue;
                    }

                    emittedMessages.Add(message);

                    HealthFinding finding = new HealthFinding();
                    finding.severity = message.severity == ValidationSeverity.Error ? HealthSeverity.Error : HealthSeverity.Warning;
                    finding.code = HealthFinding.ClipSetDoesNotBindToProfileRigCode;
                    finding.title = message.code.ToString() + ": clip set doesn't bind to its profile's rig";
                    finding.detail = message.text;
                    finding.message = "Profile '" + profile.name + "': " + message.text;
                    finding.target = message.assetContext != null ? message.assetContext : profile;
                    finding.relatedAssets.Add(profile);
                    finding.relatedAssets.Add(profile.rig);

                    ClipAsset offendingClip = message.assetContext as ClipAsset;
                    if (offendingClip != null)
                    {
                        finding.actions.Add(HealthFindingAction.Locate("Locate clip", "Selects and pings the clip.", offendingClip));
                    }
                    else if (message.assetContext != null)
                    {
                        finding.actions.Add(HealthFindingAction.Locate("Locate asset", "Selects and pings the asset.", message.assetContext));
                    }

                    finding.actions.Add(HealthFindingAction.Locate("Locate profile", "Selects and pings the actor profile.", profile));

                    output.Add(finding);
                }
            }
        }

        public static void EvaluateSharedClipBindings(HealthScanContext context, List<HealthFinding> output)
        {
            for (int clipIndex = 0; clipIndex < context.clips.Count; clipIndex++)
            {
                ClipAsset clip = context.clips[clipIndex];
                if (clip == null)
                {
                    continue;
                }

                List<ValidationMessage> messages = SharedClipBindingUtility.ValidateSharedClipBinding(clip, context.clipSets);
                for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
                {
                    ValidationMessage message = messages[messageIndex];

                    HealthFinding finding = new HealthFinding();
                    finding.severity = message.severity == ValidationSeverity.Error ? HealthSeverity.Error : HealthSeverity.Warning;
                    finding.code = HealthFinding.SharedClipBindingProblemCode;
                    finding.title = "Shared clip binding problem";
                    finding.detail = message.text;
                    finding.message = message.text;
                    finding.target = clip;
                    finding.actions.Add(HealthFindingAction.Locate("Locate clip", "Selects and pings the clip.", clip));

                    output.Add(finding);
                }
            }
        }

        private static bool IsDuplicateMessage(List<ValidationMessage> emittedMessages, ValidationMessage message)
        {
            for (int emittedIndex = 0; emittedIndex < emittedMessages.Count; emittedIndex++)
            {
                ValidationMessage emittedMessage = emittedMessages[emittedIndex];
                if (emittedMessage.code == message.code
                    && emittedMessage.assetContext == message.assetContext
                    && emittedMessage.text == message.text)
                {
                    return true;
                }
            }

            return false;
        }

        // V08 belongs to H06, V36 to H07 and V41 to H08, so a bind message with one of these codes would show twice.
        public static bool IsCodeReportedByAnotherRule(ValidationCode code)
        {
            return code == ValidationCode.V08 || code == ValidationCode.V36 || code == ValidationCode.V41;
        }
    }
}
