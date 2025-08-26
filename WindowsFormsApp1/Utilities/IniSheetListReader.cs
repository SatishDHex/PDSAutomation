using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsFormsApp1.Utilities
{
    /// <summary>
    /// Minimal INI reader for this use-case:
    /// - Lines starting with ';' or '#' are comments.
    /// - If a [Sheets] section exists, use its non-empty lines as sheet names (keys or values).
    /// - Otherwise, any non-empty, non-comment, non-section line is taken as a sheet name.
    /// </summary>
    public static class IniSheetListReader
    {
        public static List<string> ReadSheetNames(string iniPath)
        {
            if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath))
                throw new FileNotFoundException("INI file not found", iniPath);

            var lines = File.ReadAllLines(iniPath);
            var results = new List<string>();

            bool inSheets = false;
            foreach (var raw in lines)
            {
                var line = (raw ?? "").Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    inSheets = string.Equals(line, "[Sheets]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inSheets)
                {
                    // Accept "SheetName" or "key=SheetName"
                    var parts = line.Split(new[] { '=' }, 2);
                    var name = parts.Length == 2 ? parts[1].Trim() : line;
                    if (!string.IsNullOrWhiteSpace(name)) results.Add(name);
                }
            }

            // If no [Sheets] section, fall back to all plain lines
            if (results.Count == 0)
            {
                foreach (var raw in lines)
                {
                    var line = (raw ?? "").Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[")) continue; // sections ignored
                    results.Add(line);
                }
            }

            return results;
        }
    }
}
