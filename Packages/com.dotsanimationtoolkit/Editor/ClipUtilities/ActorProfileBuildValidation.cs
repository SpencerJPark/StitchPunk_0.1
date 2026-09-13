// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Text;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Fails a player build listing every actor profile animation name error; skippable per
    /// machine through <see cref="FailPlayerBuildsOnNameErrors"/>.
    /// </summary>
    public sealed class ActorProfileBuildValidation : IPreprocessBuildWithReport
    {
        public const string FailPlayerBuildsPrefsKey = "DotsAnimationToolkit.ActorProfiles.FailPlayerBuildsOnNameErrors";

        public static bool FailPlayerBuildsOnNameErrors
        {
            get
            {
                return EditorPrefs.GetBool(FailPlayerBuildsPrefsKey, true);
            }
            set
            {
                EditorPrefs.SetBool(FailPlayerBuildsPrefsKey, value);
            }
        }

        public int callbackOrder
        {
            get
            {
                return 0;
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!FailPlayerBuildsOnNameErrors)
            {
                return;
            }

            List<ValidationMessage> messages = ProfileP2Scan.ScanProject();
            if (messages.Count == 0)
            {
                return;
            }

            // report is never read or dereferenced: verification calls this method with null.
            StringBuilder builder = new StringBuilder();
            builder.Append("Player build stopped: " + messages.Count + " animation name error(s) in actor profiles. Fix each profile, or turn off 'Fail player builds on profile name errors' in Project Settings > DOTS Animation Toolkit > Animation Names.");

            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                ValidationMessage message = messages[messageIndex];
                builder.Append('\n');
                builder.Append(ProfileP2Scan.FormatForConsole(message.assetContext as ActorProfileAsset, message));
            }

            throw new BuildFailedException(builder.ToString());
        }
    }
}
