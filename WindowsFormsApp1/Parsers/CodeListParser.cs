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
            bool recursive = true, 
            ILogger logger = null,
            IToolMapSchemaRepository toolMapRepo = null,
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

                        
                        if (toolMapRepo != null)
                        {
                            // Check if the EnumList is defined in Toolmap Schema
                            CheckEnumListInToolMapSchema(toolMapRepo, parsed, logger, toolMapParentElementName);

                            // check if the EnumList has a relation to EnumList on the SPF Side
                            CheckEnumListRels(toolMapRepo, parsed.EnumNumber, logger);

                        }
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

        private static void CheckEnumListInToolMapSchema(
            IToolMapSchemaRepository repo,
            CodeList cl,
            ILogger log,
            string parentElementName)
        {

            if (log != null) 
                log.Info("Checking if the enum list def exists in the Tool map schema");
            

            var doc = repo.Document;
            if (doc == null || doc.Root == null)
            {
                if (log != null) log.Error("ToolMap repo has no loaded document.");
                return;
            }

            var parent = (parentElementName == null)
                ? doc.Root
                : doc.Root.Descendants(parentElementName).FirstOrDefault() ?? doc.Root;

            var uid = "PDS3DEnumList_" + cl.EnumNumber;

            var existing = parent
                .Descendants("SPMapEnumListDef")
                .FirstOrDefault(def =>
                {
                    var io = def.Element("IObject");
                    return io != null && (string)io.Attribute("UID") == uid;
                });

            if (existing != null)
            {
                // Optional: warn if the name differs
                var io = existing.Element("IObject");
                var existingName = io == null ? null : (string)io.Attribute("Name");
                var targetName = cl.Name ?? string.Empty;

                if (!string.IsNullOrEmpty(targetName) &&
                    !string.Equals(existingName, targetName, StringComparison.Ordinal))
                {
                    if (log != null)
                        log.Success(string.Format(
                            "Enum List Exists in ToolMap Schema but Name differs for UID={0}. Existing='{1}', Parsed='{2}'",
                            uid, existingName ?? "(null)", targetName));
                }
                else
                {
                    if (log != null) log.Success("  Enum List Exists in ToolMap Schema, UID=" + uid);
                }
                return;
            }

            // Create new node
            var newDef = new XElement("SPMapEnumListDef",
                new XElement("IObject",
                    new XAttribute("UID", uid),
                    new XAttribute("Name", (object)(cl.Name ?? string.Empty))
                ),
                new XElement("IMapObject"),
                new XElement("IMapEnumListDef",
                    new XAttribute("ProcessEnumListCriteria", string.Empty)
                )
            );

            parent.Add(newDef);
            if (log != null)
                log.Success(string.Format("Created ToolMap EnumList: UID={0}, Name='{1}'",
                    uid, cl.Name ?? string.Empty));
        }

        private static bool CheckEnumListRels(
    IToolMapSchemaRepository repo,
    int enumNumber,
    ILogger log)
        {

            if (log != null)
                log.Info("Checking if the enum list has a valid relation with Enum List on the SPF Side");


            var doc = repo.Document;
            if (doc == null || doc.Root == null)
            {
                if (log != null) log.Error("ToolMap repo has no loaded document.");
                return false;
            }

            var uid1 = "PDS3DEnumList_" + enumNumber;
            const string defUid = "MapEnumListToEnumList";

            // Look for: <Rel><IRel UID1="..." DefUID="MapEnumListToEnumList" .../></Rel>
            var found = doc
                .Root
                .Descendants("Rel")
                .Select(rel => rel.Element("IRel"))
                .Where(irel => irel != null)
                .FirstOrDefault(irel =>
                    string.Equals((string)irel.Attribute("UID1"), uid1, StringComparison.Ordinal) &&
                    string.Equals((string)irel.Attribute("DefUID"), defUid, StringComparison.Ordinal));

            if (found != null)
            {
                var uid2 = (string)found.Attribute("UID2") ?? "(null)";
                if (log != null)
                    log.Success(string.Format(
                        "Relation exists: UID1={0}, UID2={1}, DefUID={2}",
                        uid1, uid2, defUid));
                return true;
            }

            if (log != null)
                log.Warn(string.Format(
                    "Relation missing: UID1={0}, DefUID={1}",
                    uid1, defUid));

            return false;
        }
    }
}
