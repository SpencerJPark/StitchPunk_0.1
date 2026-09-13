// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Returns only P2 (animation name) validation findings for one profile or the whole project.
    /// A null registry means the project registry.
    /// </summary>
    public static class ProfileP2Scan
    {
        public static List<ValidationMessage> ScanProfile(ActorProfileAsset profile, IVocabularyRegistry animationNames = null)
        {
            List<ValidationMessage> p2Messages = new List<ValidationMessage>();
            if (profile == null)
            {
                return p2Messages;
            }

            // A null registry would make Validate silently skip the membership check, so resolve
            // the project registry here before calling it.
            IVocabularyRegistry resolvedRegistry = animationNames != null ? animationNames : VocabularyRegistryProvider.AnimationNames;
            List<ValidationMessage> allMessages = ActorProfileValidation.Validate(profile, resolvedRegistry);
            for (int messageIndex = 0; messageIndex < allMessages.Count; messageIndex++)
            {
                if (allMessages[messageIndex].code == ValidationCode.P2)
                {
                    p2Messages.Add(allMessages[messageIndex]);
                }
            }

            return p2Messages;
        }

        public static List<ValidationMessage> ScanProject(IVocabularyRegistry animationNames = null)
        {
            IVocabularyRegistry resolvedRegistry = animationNames != null ? animationNames : VocabularyRegistryProvider.AnimationNames;
            List<ValidationMessage> combinedMessages = new List<ValidationMessage>();
            string[] profileGuids = AssetDatabase.FindAssets("t:ActorProfileAsset");
            for (int guidIndex = 0; guidIndex < profileGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(profileGuids[guidIndex]);
                ActorProfileAsset profile = AssetDatabase.LoadAssetAtPath<ActorProfileAsset>(assetPath);
                if (profile == null)
                {
                    continue;
                }

                combinedMessages.AddRange(ScanProfile(profile, resolvedRegistry));
            }

            return combinedMessages;
        }

        public static string FormatForConsole(ActorProfileAsset profile, ValidationMessage message)
        {
            string profileName = profile != null ? profile.name : "(missing profile)";
            string assetPath = profile != null ? AssetDatabase.GetAssetPath(profile) : string.Empty;
            string formattedMessage = "Actor profile '" + profileName + "'";
            if (!string.IsNullOrEmpty(assetPath))
            {
                formattedMessage += " (" + assetPath + ")";
            }

            formattedMessage += ": " + message.text;
            return formattedMessage;
        }
    }
}
