using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WindowsFormsApp1.Models;

namespace WindowsFormsApp1.Utilities
{
    public static class HierarchyLookup
    {
        /// Returns the immediate parent name of 'leaf' on the deepest edge map.
        /// If multiple parents match, picks the first; returns null if none.
        public static string GetImmediateParentFromDeepest(MultiLevelHierarchy h, string leaf)
        {
            if (h == null || h.EdgesPerLevel == null || h.EdgesPerLevel.Count == 0) return null;
            var deepest = h.EdgesPerLevel[h.EdgesPerLevel.Count - 1]; // parent -> children
            foreach (var kv in deepest)
            {
                if (kv.Value != null && kv.Value.Contains(Norm(leaf)))
                    return kv.Key;
            }
            return null;

            string Norm(string s) => (s ?? "").Trim().ToLowerInvariant();
        }
    }
}
