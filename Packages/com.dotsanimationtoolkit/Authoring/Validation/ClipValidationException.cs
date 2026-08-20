// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Thrown by <see cref="ClipRegistryBuilder.Build"/> when the source assets carry validation
    /// errors (architecture sections 3.5, 8 M1). The exception message lists every offending rule
    /// code and its explanation, and <see cref="Messages"/> carries the full findings — including
    /// the warnings that accompanied them — so an inspector or a baker can surface them with their
    /// asset context intact.
    /// </summary>
    public sealed class ClipValidationException : Exception
    {
        private readonly ValidationMessage[] messages;

        /// <summary>
        /// Creates the exception from a complete validation result.
        /// </summary>
        /// <param name="validationMessages">
        /// Every finding produced for the asset under bake, errors and warnings alike. A caller is
        /// expected to raise this only when the set carries at least one error — that is not
        /// enforced here, because an exception constructor that throws would replace the diagnostic
        /// the caller was trying to deliver with a less useful one. A null or empty list yields the
        /// bare header with no findings listed.
        /// </param>
        public ClipValidationException(IReadOnlyList<ValidationMessage> validationMessages)
            : base(FormatMessage(validationMessages))
        {
            messages = ToArray(validationMessages);
        }

        /// <summary>
        /// Every finding produced for the asset under bake, in the order validation reported them.
        /// </summary>
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
