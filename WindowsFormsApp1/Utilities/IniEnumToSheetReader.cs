using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsFormsApp1.Utilities
{
    /// <summary>
    /// Reads an INI file where each non-comment line is "EnumNumber=SheetName".
    /// Examples:
    ///   125=FluidCode
    ///   130=PipeCodes
    ///   200=ValveCodes
    /// - Lines starting with ';' or '#' are comments and skipped.
    /// - Whitespace is trimmed around key/value.
    /// - Duplicate keys: last one wins.
    /// </summary>
    public static class IniEnumToSheetReader
    {
        public static Dictionary<int, string> ReadMap(string iniPath)
        {
            if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath))
                throw new FileNotFoundException("INI file not found", iniPath);

            var map = new Dictionary<int, string>();
            var lines = File.ReadAllLines(iniPath);

            for (int i = 0; i < lines.Length; i++)
            {
                var raw = lines[i] ?? string.Empty;
                var line = raw.Trim();

                if (line.Length == 0) continue;                    // blank
                if (line.StartsWith(";") || line.StartsWith("#"))   // comment
                    continue;

                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length != 2)
                    continue; // silently skip malformed lines (or throw if you prefer)

                var keyPart = parts[0].Trim();
                var valPart = parts[1].Trim();

                if (keyPart.Length == 0 || valPart.Length == 0)
                    continue;

                int enumNum;
                if (!int.TryParse(keyPart, out enumNum))
                    continue; // skip non-numeric keys

                map[enumNum] = valPart; // last one wins
            }

            return map;
        }

        /// <summary>
        /// Convenience helper: returns distinct sheet names (case-insensitive) from the map,
        /// preserving first-seen order.
        /// </summary>
        public static List<string> GetDistinctSheets(Dictionary<int, string> enumToSheet)
        {
            var result = new List<string>();
            if (enumToSheet == null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in enumToSheet)
            {
                var name = kv.Value ?? string.Empty;
                if (name.Length == 0) continue;
                if (seen.Add(name)) result.Add(name);
            }
            return result;
        }
    }
}
