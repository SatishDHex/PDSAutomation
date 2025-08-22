using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WindowsFormsApp1.Utilities
{
    public static class VersionedFile
    {
        /// <summary>
        /// Returns a new path by appending the next available number using a pattern like "_001".
        /// Examples:
        ///   toolmapschema.xml    -> toolmapschema_001.xml (if not exists)
        ///   toolmapschema_001.xml -> toolmapschema_002.xml (if _001 exists)
        /// Pattern default: "_{0:000}" => _001, _002, ...
        /// </summary>
        public static string GetNextVersionPath(string originalPath, string pattern = "_{0:000}")
        {
            if (string.IsNullOrWhiteSpace(originalPath))
                throw new ArgumentException("originalPath is required", nameof(originalPath));

            var dir = Path.GetDirectoryName(originalPath) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(originalPath) ?? "file";
            var ext = Path.GetExtension(originalPath) ?? "";

            // Detect if baseName already ends with _NNN (keep incrementing from there)
            // e.g., toolmapschema_007 -> base core "toolmapschema" + 7
            var m = Regex.Match(baseName, @"^(?<core>.*?)(?:_(?<n>\d+))$");
            string core = baseName;
            int start = 1;
            if (m.Success)
            {
                core = m.Groups["core"].Value;
                int n;
                if (int.TryParse(m.Groups["n"].Value, out n))
                    start = n + 1;
            }

            // Iterate upward until we find a free filename
            for (int i = start; i < 100000; i++)
            {
                var candidate = Path.Combine(dir, core + string.Format(pattern, i) + ext);
                if (!File.Exists(candidate))
                    return candidate;
            }

            throw new IOException("Could not find an available versioned filename.");
        }
    }
}
