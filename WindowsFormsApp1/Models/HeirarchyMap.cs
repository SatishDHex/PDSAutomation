using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsFormsApp1.Models
{
    /// <summary>
    /// Holds parent -> set(children) mapping for one sheet.
    /// </summary>
    public sealed class HierarchyMap
    {
        private readonly Dictionary<string, HashSet<string>> _map =
            new Dictionary<string, HashSet<string>>();

        public IReadOnlyDictionary<string, HashSet<string>> Map { get { return _map; } }

        public void Add(string parent, string child)
        {
            if (string.IsNullOrWhiteSpace(parent)) parent = "undefined";
            if (string.IsNullOrWhiteSpace(child)) child = "undefined";

            HashSet<string> set;
            if (!_map.TryGetValue(parent, out set))
            {
                set = new HashSet<string>();
                _map[parent] = set;
            }
            set.Add(child);
        }
    }

    public sealed class MultiLevelHierarchy
    {
        // edges[i] is the parent->children map from level i to level i+1
        // Example: edges[0] = map for Level1 -> Level2
        public List<Dictionary<string, HashSet<string>>> EdgesPerLevel { get; }
            = new List<Dictionary<string, HashSet<string>>>();

        // For compatibility: deepest pair mapping (last two levels)
        public Dictionary<string, HashSet<string>> DeepestPair { get; set; }

        public int LevelCount { get; set; } // total detected levels
    }
}
