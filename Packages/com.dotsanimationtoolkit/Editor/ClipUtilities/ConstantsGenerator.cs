// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Text;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Turns an authored name list into a C# file of <c>public const</c> ids — the one code path
    /// behind every "Generate … Constants" button in this package (Phase E target-tags spec §4.2.3,
    /// amendment E6 Task 2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why generated constants exist at all.</strong> The owner directive in §4.2.3 is that
    /// a number is never something a person types, reads, or compares — yet the runtime cannot be
    /// handed a name. Event and tag consumers run inside Burst jobs, and Burst cannot compare
    /// managed strings, so a <c>uint</c> compare is the only legal form. A generated constant is the
    /// only shape that is simultaneously name-shaped in source and a bare integer at run time.
    /// </para>
    /// <para>
    /// <strong>Why one class rather than a copy per button.</strong> Every rule below is a rule about
    /// emitting *legal C#* from *arbitrary user text*, and every one of them is a silent generator
    /// bug when it is wrong — a duplicate constant name or an unescaped keyword does not fail here,
    /// it fails in the customer's compiler, in a file they were told not to hand-edit. This started
    /// as private machinery inside <see cref="ClipSetAssetEditor"/>; Task 2 needed the same rules for
    /// two more buttons, and three copies of a rule is three places for it to drift.
    /// </para>
    /// <para>
    /// <strong>Every edge case in an authored name is resolved deterministically, not hopefully.</strong>
    /// A name is reduced to <c>[A-Za-z0-9_]</c> by <see cref="SanitizeIdentifier"/>; one that
    /// sanitizes to nothing falls back to a positional name that cannot collide with itself; one
    /// that starts with a digit gains a leading underscore; two names that sanitize to the same
    /// identifier are disambiguated by <see cref="MakeUniqueName"/> against every name already
    /// emitted, so a suffix can never itself collide; and a name that lands on a reserved word is
    /// escaped with the verbatim prefix <c>@</c>, which is legal regardless of which keyword it is.
    /// </para>
    /// </remarks>
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
        /// Builds the full source text for a project vocabulary: a generated-file header explaining
        /// why the file exists, then one <c>public const uint</c> per usable row.
        /// </summary>
        /// <param name="registry">The vocabulary to emit. Rows are read in authoring order, so the
        /// output is stable across regenerations that did not change the list.</param>
        /// <param name="className">The static class to emit. Already sanitized by the caller — see
        /// <see cref="ClassNameFromFilePath"/>.</param>
        /// <param name="entryNoun">How one row is named in prose, e.g. "Target tag" or "Event".
        /// Sentence-cased; the header lower-cases it where it needs to.</param>
        /// <param name="fallbackEntryNamePrefix">Stem for a row whose name survives sanitizing as
        /// nothing, e.g. "Tag" produces <c>Tag3</c> for the third row.</param>
        /// <param name="reports">
        /// Optional accumulator, one line per row whose emitted constant is not what was authored —
        /// a name that had to be sanitized, renamed to avoid a collision, escaped as a keyword, or
        /// skipped outright. Callers surface these; §4.2.3's promise is that the owner works in
        /// names, so a name that could not survive the trip to C# is exactly the case they must be
        /// told about rather than left to discover by reading the generated file.
        /// </param>
        /// <returns>The complete file text, ready to write.</returns>
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

        /// <summary>
        /// Writes the generated-file banner, including the two explanations a customer opening this
        /// file needs: why it holds numbers when the whole feature is about names, and why it is
        /// allowed to break their build.
        /// </summary>
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

        /// <summary>
        /// Derives the static class name from the file the constants are being written to, so the
        /// type and its file agree without asking twice.
        /// </summary>
        /// <remarks>
        /// A file name is the one place the user has already expressed what they want this called,
        /// and it is the name they will see in the project browser — deriving from it means renaming
        /// the file and regenerating renames the class, rather than producing a file whose name and
        /// type disagree.
        /// </remarks>
        /// <param name="filePath">Absolute or project-relative path of the file to write.</param>
        /// <param name="fallbackClassName">Used when the file name sanitizes to nothing.</param>
        /// <returns>A legal C# identifier.</returns>
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
        /// Reduces <paramref name="rawName"/> to a legal C# identifier's character set. Every
        /// character outside <c>[A-Za-z0-9_]</c> becomes an underscore, and a leading digit is
        /// prefixed with one - both deterministic, both always producing a syntactically legal
        /// identifier (or an empty string when nothing survives, left for the caller to fall back on).
        /// </summary>
        /// <param name="rawName">The authored name, exactly as typed.</param>
        /// <returns>A legal identifier, or the empty string.</returns>
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
        /// with the same base gets a <c>_1</c>, <c>_2</c>, ... suffix, incrementing past any suffix
        /// that a distinct row already produced — including one a row's own authored name happened
        /// to spell — so the returned name is always unused so far.
        /// </summary>
        /// <param name="baseName">The sanitized identifier this row wants.</param>
        /// <param name="usedNameCounts">Accumulator shared across one generation pass.</param>
        /// <returns>An identifier not yet emitted in this pass.</returns>
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

        /// <summary>
        /// Prefixes <paramref name="identifierName"/> with <c>@</c> when it is a reserved word, which
        /// is legal C# regardless of which keyword it is.
        /// </summary>
        /// <param name="identifierName">A sanitized identifier.</param>
        /// <returns>The identifier, verbatim-escaped if it had to be.</returns>
        public static string EscapeReservedKeyword(string identifierName)
        {
            return ReservedCSharpKeywords.Contains(identifierName) ? "@" + identifierName : identifierName;
        }

        /// <summary>
        /// Makes raw authored text safe to sit inside a single <c>///</c> XML doc comment line: line
        /// breaks are folded to spaces (an embedded newline would otherwise end the comment mid-name
        /// and leave the rest as bare, invalid code), and the three XML-significant characters are
        /// entity-escaped.
        /// </summary>
        /// <param name="rawText">The authored text.</param>
        /// <returns>Text safe for one doc-comment line.</returns>
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

        /// <summary>
        /// Writes generated source to <paramref name="filePath"/> and tells Unity it is there.
        /// </summary>
        /// <remarks>
        /// The directory is created when missing because a remembered path outlives the folder it
        /// pointed at — a regenerate that throws <see cref="DirectoryNotFoundException"/> because
        /// someone reorganised a folder is a worse answer than simply recreating it. The write goes
        /// through a plain file API rather than <c>AssetDatabase</c> (the target may legitimately sit
        /// outside the project), so an explicit refresh is what makes Unity notice and compile it.
        /// </remarks>
        /// <param name="filePath">Absolute or project-relative destination.</param>
        /// <param name="generatedSource">The full file text.</param>
        public static void WriteGeneratedFile(string filePath, string generatedSource)
        {
            string containingDirectory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(containingDirectory) && !Directory.Exists(containingDirectory))
            {
                Directory.CreateDirectory(containingDirectory);
            }

            File.WriteAllText(filePath, generatedSource);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// The path to remember for <paramref name="absoluteFilePath"/>: project-relative when it
        /// sits inside the project, so the destination survives being opened on another machine, and
        /// otherwise the absolute path unchanged.
        /// </summary>
        /// <param name="absoluteFilePath">What a save dialog returned.</param>
        /// <returns>The path to store on the registry.</returns>
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
