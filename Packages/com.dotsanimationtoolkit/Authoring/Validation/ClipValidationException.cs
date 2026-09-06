// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Thrown by <see cref="ClipRegistryBuilder.Build"/> when the source assets carry validation
    /// errors. <see cref="Messages"/> carries every finding, including warnings, so a caller can
    /// surface them with their asset context intact.
    /// </summary>
    public sealed class ClipValidationException : Exception
    {
        private readonly ValidationMessage[] messages;

        // Not enforced that validationMessages contains an error: a throwing constructor here would
        // replace the diagnostic the caller was trying to deliver with a less useful one.
        public ClipValidationException(IReadOnlyList<ValidationMessage> validationMessages)
            : base(FormatMessage(validationMessages))
        {
            messages = ToArray(validationMessages);
        }

        /// <summary>Every finding produced for the asset under bake, in the order validation reported them.</summary>
        public IReadOnlyList<ValidationMessage> Messages
        {
            get { return messages; }
        }

        private static ValidationMessage[] ToArray(IReadOnlyList<ValidationMessage> validationMessages)
        {
            if (validationMessages == null)
            {
                return Array.Empty<ValidationMessage>();
            }
            ValidationMessage[] copiedMessages = new ValidationMessage[validationMessages.Count];
            for (int messageIndex = 0; messageIndex < validationMessages.Count; messageIndex++)
            {
                copiedMessages[messageIndex] = validationMessages[messageIndex];
            }
            return copiedMessages;
        }

        private static string FormatMessage(IReadOnlyList<ValidationMessage> validationMessages)
        {
            StringBuilder messageBuilder = new StringBuilder();
            messageBuilder.Append("Clip registry bake failed validation:");
            if (validationMessages != null)
            {
                for (int messageIndex = 0; messageIndex < validationMessages.Count; messageIndex++)
                {
                    ValidationMessage validationMessage = validationMessages[messageIndex];
                    if (!validationMessage.IsError)
                    {
                        continue;
                    }
                    messageBuilder.Append("\n  ");
                    messageBuilder.Append(validationMessage.ToString());
                }
            }
            return messageBuilder.ToString();
        }
    }
}
