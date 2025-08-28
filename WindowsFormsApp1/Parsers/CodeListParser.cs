using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using WindowsFormsApp1.Logging;
using WindowsFormsApp1.Logging.Interfaces;
using WindowsFormsApp1.Models;
using WindowsFormsApp1.Repositories;
using WindowsFormsApp1.Repositories.Interfaces;
using WindowsFormsApp1.Services;
using WindowsFormsApp1.Utilities;

namespace WindowsFormsApp1.Parsers
{
    public static class CodeListParser
    {
        // Entry lines example:
        //        1 = ' '
        //        2 = 'A =Approved'
        //        3 = 'NA=Not approved'
        private static readonly Regex EntryRegex =
            new Regex(@"^\s*(?<n>\d+)\s*=\s*'(?<payload>[^']*)'", RegexOptions.Compiled);

        /// <summary>
        /// Parse all *.edt files in a folder.
        /// </summary>
        public static List<CodeList> ParseFolder(
            string folderPath,
            Dictionary<int, string> enumToSheet,
            Dictionary<string, MultiLevelHierarchy> hierarchiesBySheet,
            bool recursive = true, 
            ILogger logger = null,
            IToolMapSchemaRepository toolMapRepo = null,
            ISpfRepository spfRepository = null,
            string toolMapParentElementName = null)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                throw new DirectoryNotFoundException("Codelist folder not found: " + folderPath);

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(folderPath, "*.edt", searchOption);


            var results = new List<CodeList>();
            foreach (var file in files)
            {
                try
                {

                    if (logger != null)
                    {
                        logger.Info("------------------------------------------------------------------------------------------");
                        logger.Info("Processing codelist: " + file);
                    }
                    string skipReason;
                    var parsed = ParseFromFile(file, out skipReason);

                    if (parsed != null)
                    {
                        results.Add(parsed);
                        var name = string.IsNullOrEmpty(parsed.Name) ? "(no name)" : parsed.Name;


                        // Check the codelist values against EnumEnums in toolmapschema
                        var svc = new ToolMapEnumValueService();
                        svc.EnsureValuesAndRelations(
                                toolMapRepo, 
                                spfRepository, 
                                new[] { parsed }, 
                                enumToSheet,
                                hierarchiesBySheet, 
                                logger);

                        if (logger != null)
                            logger.Success(string.Format("Processed: {0} (Enum={1}, Name='{2}', Entries={3})",
                               Path.GetFileName(file), parsed.EnumNumber, name, parsed.Entries.Count));

                    }
                    else
                    {
                        if (logger != null)
                            logger.Warn(string.Format("Skipped: {0} — {1}",
                                Path.GetFileName(file), string.IsNullOrEmpty(skipReason) ? "Unknown reason" : skipReason));
                    }
                }
                catch (Exception ex)
                {
                    if (logger != null) 
                        logger.Error("Failed to parse : " + Path.GetFileName(file) + " — " + ex.Message);
                }
            }
            return results;
        }

        /// <summary>
        /// Parse a single .edt file. Returns null if the enum number cannot be determined from filename.
        /// </summary>
        public static CodeList ParseFromFile(string path, out string skipReason)
        {
            skipReason = null;

            // Extract enum number from filename, e.g., "code999.edt" -> 999
            string fileNameNoExt = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            int enumNumber = 0;

            var match = Regex.Match(fileNameNoExt, @"(\d+)$"); // capture trailing digits
            if (match.Success)
            {
                enumNumber = int.Parse(match.Groups[1].Value);
            }
            else
            {
                // Cannot determine enum number from filename -> skip
                skipReason = "Could not determine enum number from filename";
                return null;
            }

            using (var sr = new StreamReader(path, Encoding.UTF8, true))
            {
                var text = sr.ReadToEnd();
                return Parse(text, path, enumNumber);
            }
        }

        /// <summary>
        /// Parse raw .edt content given an already determined enum number (from filename).
        /// </summary>
        public static CodeList Parse(string text, string filePath, int enumNumber)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Input is empty.", nameof(text));

            var lines = SplitLines(text);

            // Name from the second line (expected to be a comment like: "; 0035, Approval Status (10)")
            string name = null;
            if (lines.Length >= 2)
            {
                string secondLine = lines[1].Trim();
                if (secondLine.StartsWith(";"))
                {
                    // Remove leading ';' and trim
                    string comment = secondLine.TrimStart(';').Trim();
                    // Expect "0035, Approval Status (10)" -> take the part after the comma, before trailing "(...)"
                    int commaIdx = comment.IndexOf(',');
                    if (commaIdx >= 0 && commaIdx + 1 < comment.Length)
                    {
                        string afterComma = comment.Substring(commaIdx + 1).Trim();

                        // Drop trailing "(...)" if present
                        int parenIdx = afterComma.LastIndexOf('(');
                        if (parenIdx > 0)
                            afterComma = afterComma.Substring(0, parenIdx).Trim();

                        if (!string.IsNullOrEmpty(afterComma))
                            name = afterComma;
                    }
                }
            }

            var result = new CodeList
            {
                EnumNumber = enumNumber,
                Name = name
            };

            // Parse entries
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var trimmed = raw.TrimStart();
                if (trimmed.StartsWith(";")) continue; // ignore comments

                var m = EntryRegex.Match(raw);
                if (!m.Success) continue;

                int number = int.Parse(m.Groups["n"].Value);
                string payload = m.Groups["payload"].Value; // inside quotes

                string shortPart = string.Empty;
                string longPart = null;

                int eq = payload.IndexOf('=');
                if (eq >= 0)
                {
                    shortPart = payload.Substring(0, eq).Trim();
                    longPart = payload.Substring(eq + 1).Trim();
                }
                else
                {
                    shortPart = payload.Trim();
                }

                // Normalize single space to empty
                if (shortPart == " ")
                    shortPart = string.Empty;

                var entry = new CodeEntry
                {
                    Number = number,
                    Short = shortPart,
                    Long = string.IsNullOrEmpty(longPart) ? null : longPart
                };

                // If duplicates by number exist, last one wins (change to 'throw' if you want strictness).
                result.Entries[number] = entry;
            }

            return result;
        }

        private static string[] SplitLines(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        
    }
}
