// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Text;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Turns an authored name list into a C# file of <c>public const uint</c> ids — the code path
    /// behind every "Generate … Constants" button in this package. Names exist for authors; Burst
    /// jobs cannot compare managed strings, so consumers need the generated integer instead.
    /// </summary>
    public static class ConstantsGenerator
    {
        /// <summary>The extension offered by every generate dialog, without the dot.</summary>
        public const string GeneratedFileExtension = "cs";

        // The 77 reserved C# keywords. Contextual keywords (var, async, await, yield, partial,
        // dynamic, nameof, where, when, ...) are legal identifiers as-is and are deliberately absent.
        private static readonly HashSet<string> ReservedCSharpKeywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while"
        };

        // -----------------------------------------------------------------------------------
        // Vocabulary constants (target tags, event names).
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Builds the full source text for a project vocabulary: a generated-file header, then one
        /// <c>public const uint</c> per usable row.
        /// </summary>
        /// <param name="reports">Optional accumulator; one line per row whose emitted constant was
        /// not what was authored (sanitized, renamed, escaped, or skipped).</param>
        public static string BuildVocabularyConstantsSource(
            IVocabularyRegistry registry,
            string className,
            string entryNoun,
            string fallbackEntryNamePrefix,
            List<string> reports)
        {
            StringBuilder source = new StringBuilder();
            int entryCount = registry != null ? registry.VocabularyEntryCount : 0;

            AppendVocabularyHeader(source, entryNoun, entryCount);

            source.Append("public static class " + className + "\n");
            source.Append("{\n");

            Dictionary<string, int> usedNameCounts = new Dictionary<string, int>();
            for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
            {
                string authoredName = registry.VocabularyEntryName(entryIndex);
                uint entryId = registry.VocabularyEntryId(entryIndex);

                // A zero id is the interface's documented "this row is unusable" value, and the
                // picker already skips such rows. Emitting one would hand game code a constant that
                // matches nothing, which is worse than its absence: the call site compiles and the
                // comparison silently never fires.
                if (entryId == 0u)
                {
                    AddReport(
                        reports,
                        DescribeRow(entryIndex, authoredName)
                        + " has no id and was skipped. Re-add it in the registry to mint one.");
                    continue;
                }

                string baseIdentifierName = SanitizeIdentifier(authoredName);
                if (string.IsNullOrEmpty(baseIdentifierName))
                {
                    baseIdentifierName = fallbackEntryNamePrefix + (entryIndex + 1).ToString();
                    AddReport(
                        reports,
                        DescribeRow(entryIndex, authoredName)
                        + " has no name that can be written as C#, so it is emitted as '"
                        + baseIdentifierName + "'.");
                }
                else if (baseIdentifierName != authoredName)
                {
                    AddReport(
                        reports,
                        "'" + authoredName + "' is not a legal C# identifier and is emitted as '"
                        + baseIdentifierName + "'.");
                }

                string uniqueIdentifierName = MakeUniqueName(baseIdentifierName, usedNameCounts);
                if (uniqueIdentifierName != baseIdentifierName)
                {
                    AddReport(
                        reports,
                        "'" + authoredName + "' collides with an earlier row once written as C#, so "
                        + "it is emitted as '" + uniqueIdentifierName + "'. Renaming one of them in "
                        + "the registry is the readable fix.");
                }

                string emittedIdentifierName = EscapeReservedKeyword(uniqueIdentifierName);
                if (emittedIdentifierName != uniqueIdentifierName)
                {
                    AddReport(
                        reports,
                        "'" + authoredName + "' is a C# keyword and is emitted as '"
                        + emittedIdentifierName + "'.");
                }

                source.Append(
                    "    /// <summary>" + entryNoun + " '" + EscapeXmlDocText(authoredName)
                    + "'.</summary>\n");
                source.Append(
                    "    public const uint " + emittedIdentifierName + " = 0x"
                    + entryId.ToString("X8") + "u;\n");
            }

            source.Append("}\n");
            return source.ToString();
        }

        private static void AppendVocabularyHeader(StringBuilder source, string entryNoun, int entryCount)
        {
            string vocabularyDescription = entryNoun.ToLowerInvariant() + " registry";

            source.Append("// <auto-generated>\n");
            source.Append("// Generated by the DOTS Animation Toolkit " + vocabularyDescription + ".\n");
            source.Append("// " + entryCount.ToString() + " row(s) at the time of generation.\n");
            source.Append("// Regenerating this file overwrites it; do not hand-edit it.\n");
            source.Append("//\n");
            source.Append("// Why constants and not the names themselves: consumers of these ids run\n");
            source.Append("// inside Burst jobs, and Burst cannot compare managed strings. A uint\n");
            source.Append("// compare against one of the constants below is the only form that is both\n");
            source.Append("// name-shaped in source and legal at run time - the name itself never\n");
            source.Append("// reaches the runtime at all.\n");
            source.Append("//\n");
            source.Append("// Why this file is allowed to break the build: renaming a row in the\n");
            source.Append("// registry renames its constant here, so every use of the old name stops\n");
            source.Append("// compiling - loud, located, and fixed in seconds. The alternative is a\n");
            source.Append("// name that silently repoints to different data, which is the failure this\n");
            source.Append("// whole identity scheme exists to prevent.\n");
            source.Append("// </auto-generated>\n\n");
        }

        private static string DescribeRow(int entryIndex, string authoredName)
        {
            return string.IsNullOrEmpty(authoredName)
                ? "Row " + (entryIndex + 1).ToString()
                : "'" + authoredName + "'";
        }

        private static void AddReport(List<string> reports, string reportLine)
        {
            if (reports != null)
            {
                reports.Add(reportLine);
            }
        }

        // -----------------------------------------------------------------------------------
        // Shared identifier machinery.
        // -----------------------------------------------------------------------------------

        /// <param name="fallbackClassName">Used when the file name sanitizes to nothing.</param>
        public static string ClassNameFromFilePath(string filePath, string fallbackClassName)
        {
            string fileNameWithoutExtension = string.IsNullOrEmpty(filePath)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(filePath);

            string sanitizedClassName = SanitizeIdentifier(fileNameWithoutExtension);
            if (string.IsNullOrEmpty(sanitizedClassName))
            {
                sanitizedClassName = fallbackClassName;
            }
            return EscapeReservedKeyword(sanitizedClassName);
        }

        /// <summary>
        /// Reduces <paramref name="rawName"/> to a legal C# identifier: every character outside
        /// <c>[A-Za-z0-9_]</c> becomes an underscore, and a leading digit is prefixed with one.
        /// </summary>
        /// <returns>A legal identifier, or the empty string when nothing survives.</returns>
        public static string SanitizeIdentifier(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return string.Empty;
            }

            StringBuilder sanitized = new StringBuilder(rawName.Length);
            for (int charIndex = 0; charIndex < rawName.Length; charIndex++)
            {
                char currentCharacter = rawName[charIndex];
                bool isLegalIdentifierCharacter =
                    (currentCharacter >= 'a' && currentCharacter <= 'z')
                    || (currentCharacter >= 'A' && currentCharacter <= 'Z')
                    || (currentCharacter >= '0' && currentCharacter <= '9')
                    || currentCharacter == '_';
                sanitized.Append(isLegalIdentifierCharacter ? currentCharacter : '_');
            }

            if (sanitized.Length == 0)
            {
                return string.Empty;
            }
            if (sanitized[0] >= '0' && sanitized[0] <= '9')
            {
                sanitized.Insert(0, '_');
            }
            return sanitized.ToString();
        }

        /// <summary>
        /// Returns <paramref name="baseName"/> unchanged the first time it is seen; every later call
        /// with the same base gets a <c>_1</c>, <c>_2</c>, ... suffix that has not itself been used.
        /// </summary>
        /// <param name="usedNameCounts">Accumulator shared across one generation pass.</param>
        public static string MakeUniqueName(string baseName, Dictionary<string, int> usedNameCounts)
        {
            if (!usedNameCounts.ContainsKey(baseName))
            {
                usedNameCounts.Add(baseName, 0);
                return baseName;
            }

            int suffix = usedNameCounts[baseName];
            string candidateName;
            do
            {
                suffix++;
                candidateName = baseName + "_" + suffix.ToString();
            }
            while (usedNameCounts.ContainsKey(candidateName));

            usedNameCounts[baseName] = suffix;
            usedNameCounts.Add(candidateName, 0);
            return candidateName;
        }

        /// <summary>Prefixes <paramref name="identifierName"/> with <c>@</c> when it is a reserved word.</summary>
        public static string EscapeReservedKeyword(string identifierName)
        {
            return ReservedCSharpKeywords.Contains(identifierName) ? "@" + identifierName : identifierName;
        }

        // Line breaks are folded to spaces — an embedded newline would otherwise end the comment
        // mid-name and leave the rest as bare, invalid code.
        public static string EscapeXmlDocText(string rawText)
        {
            if (string.IsNullOrEmpty(rawText))
            {
                return string.Empty;
            }
            string singleLineText = rawText.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            return singleLineText.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        // -----------------------------------------------------------------------------------
        // Writing.
        // -----------------------------------------------------------------------------------

        public static void WriteGeneratedFile(string filePath, string generatedSource)
        {
            string containingDirectory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(containingDirectory) && !Directory.Exists(containingDirectory))
            {
                Directory.CreateDirectory(containingDirectory);
            }

            File.WriteAllText(filePath, generatedSource);
            // Deferred: a caller can run inside an Editor.OnDisable raised by DestroyImmediate,
            // and refreshing immediately there means a domain reload under a stack still unwinding.
            EditorApplication.delayCall += AssetDatabase.Refresh;
        }

        /// <summary>
        /// The path to remember for <paramref name="absoluteFilePath"/>: project-relative when it
        /// sits inside the project, so the destination survives being opened on another machine.
        /// </summary>
        public static string ToStorablePath(string absoluteFilePath)
        {
            if (string.IsNullOrEmpty(absoluteFilePath))
            {
                return string.Empty;
            }

            string projectRelativePath = FileUtil.GetProjectRelativePath(
                absoluteFilePath.Replace('\\', '/'));
            return string.IsNullOrEmpty(projectRelativePath) ? absoluteFilePath : projectRelativePath;
        }
    }
}
