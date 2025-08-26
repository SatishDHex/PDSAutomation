using ExcelDataReader;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WindowsFormsApp1.Logging.Interfaces;
using WindowsFormsApp1.Models;

namespace WindowsFormsApp1.Parsers
{
    /// <summary>
    /// Reads an Excel workbook and, for selected sheets, builds parent->children maps
    /// using even columns (B, D, F, ...) that have a header containing "Short Description".
    /// </summary>
    public static class ExcelHierarchyParser
    {
        /// <summary>
        /// Build hierarchies for the given sheets.
        /// </summary>
        /// <param name="workbookPath">Path to .xlsx</param>
        /// <param name="sheetNames">Sheets to process (from INI)</param>
        /// <param name="logger">Optional logger</param>
        /// <returns>Dictionary: sheetName -> HierarchyMap</returns>
        public static Dictionary<string, MultiLevelHierarchy> BuildHierarchiesMultiLevel(
            string workbookPath,
            IEnumerable<string> sheetNames,
            ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
                throw new FileNotFoundException("Workbook not found", workbookPath);
            if (sheetNames == null) throw new ArgumentNullException(nameof(sheetNames));

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            using (var stream = File.Open(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream))
            {
                var ds = reader.AsDataSet(new ExcelDataReader.ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataReader.ExcelDataTableConfiguration { UseHeaderRow = false }
                });

                var wanted = new HashSet<string>(sheetNames, StringComparer.OrdinalIgnoreCase);
                var dict = new Dictionary<string, MultiLevelHierarchy>(StringComparer.OrdinalIgnoreCase);

                foreach (DataTable table in ds.Tables)
                {
                    if (!wanted.Contains(table.TableName)) continue;
                    logger?.Info("Processing sheet: " + table.TableName);

                    var h = ParseOneSheet_MultiLevel(table, logger);
                    dict[table.TableName] = h;

                    int totalParents = h.EdgesPerLevel.Sum(m => m.Count);
                    int totalChildren = h.EdgesPerLevel.Sum(m => m.Values.Sum(s => s.Count));
                    logger?.Success(string.Format(
                        "Built {0}-level hierarchy for '{1}': totalParents={2}, totalChildren={3}.",
                        h.LevelCount, table.TableName, totalParents, totalChildren));
                }

                return dict;
            }
        }

        /// <summary>
        /// Parse one sheet: detect level columns (even columns with "Short Description" in header),
        /// then build parent->children mapping across the last two detected levels.
        /// </summary>
        private static MultiLevelHierarchy ParseOneSheet_MultiLevel(DataTable table, ILogger logger)
        {
            // 1) Find even columns (B,D,F,...) that contain "Short Description" in the top few rows.
            var evenCols = new List<int>();
            for (int c = 2; c <= table.Columns.Count; c += 2) evenCols.Add(c);

            var levelCols = new List<int>();
            var headerRowByCol = new Dictionary<int, int>();

            for (int r = 0; r < Math.Min(5, table.Rows.Count); r++)
            {
                for (int k = 0; k < evenCols.Count; k++)
                {
                    int col1 = evenCols[k];
                    int col0 = col1 - 1;
                    if (col0 >= table.Columns.Count) continue;

                    var cell = table.Rows[r][col0];
                    var txt = (cell == null || cell == DBNull.Value) ? "" : Convert.ToString(cell).Trim();
                    if (!string.IsNullOrEmpty(txt) &&
                        txt.IndexOf("short description", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (!levelCols.Contains(col1))
                        {
                            levelCols.Add(col1);
                            headerRowByCol[col1] = r + 1; // store 1-based
                        }
                    }
                }
            }

            levelCols.Sort();

            var result = new MultiLevelHierarchy
            {
                LevelCount = levelCols.Count
            };

            if (levelCols.Count < 2)
            {
                logger?.Warn(string.Format(
                    "Sheet '{0}': found {1} level column(s); need at least 2. Skipping hierarchy build.",
                    table.TableName, levelCols.Count));
                result.DeepestPair = new Dictionary<string, HashSet<string>>();
                return result;
            }

            // 2) Build edges for every adjacent pair of levels.
            // edges[i] = map from level i (left) to level i+1 (right)
            // NOTE: level index here is positional in levelCols list (0..N-1)
            for (int i = 0; i < levelCols.Count - 1; i++)
            {
                int parentCol1 = levelCols[i];
                int childCol1 = levelCols[i + 1];

                int parentHdrRow = headerRowByCol[parentCol1];
                int childHdrRow = headerRowByCol[childCol1];
                int startDataRow = Math.Max(parentHdrRow, childHdrRow) + 1; // 1-based

                var edgeMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                for (int r = startDataRow; r <= table.Rows.Count; r++)
                {
                    var row = table.Rows[r - 1];

                    string parent = NormalizeKey(GetCellString(row, parentCol1 - 1));
                    string child = NormalizeKey(GetCellString(row, childCol1 - 1));

                    if (string.IsNullOrEmpty(parent) && string.IsNullOrEmpty(child)) continue;

                    if (string.IsNullOrEmpty(parent)) parent = "undefined";
                    if (string.IsNullOrEmpty(child)) child = "undefined";

                    HashSet<string> set;
                    if (!edgeMap.TryGetValue(parent, out set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        edgeMap[parent] = set;
                    }
                    set.Add(child);
                }

                result.EdgesPerLevel.Add(edgeMap);
            }

            // 3) Keep compatibility map for the deepest pair (last two levels).
            result.DeepestPair = result.EdgesPerLevel[result.EdgesPerLevel.Count - 1];

            logger?.Info(string.Format(
                "Sheet '{0}': detected {1} level(s) at columns [{2}]. Built {3} edge map(s).",
                table.TableName,
                levelCols.Count,
                string.Join(", ", levelCols),   // e.g., "2, 4, 6, 8"
                result.EdgesPerLevel.Count));

            return result;
        }

        private static string GetCellString(DataRow row, int colIndex0Based)
        {
            if (colIndex0Based < 0 || colIndex0Based >= row.Table.Columns.Count) return "";
            var val = row[colIndex0Based];
            if (val == null || val == DBNull.Value) return "";
            return Convert.ToString(val).Trim();
        }

        private static string NormalizeKey(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            // Example normalization to align with your sample ('Foam' -> 'foam', 'FS' -> 'fs'):
            s = s.Trim();
            s = Regex.Replace(s, @"\s+", " ");
            return s.ToLowerInvariant();
        }
    }
}
