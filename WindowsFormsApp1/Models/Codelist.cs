using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsFormsApp1.Models
{
    public sealed class CodeEntry
    {
        public int Number { get; set; }
        public string Short { get; set; }
        public string Long { get; set; }   // optional long description, may be null
    }

    public sealed class CodeList
    {
        /// <summary>Enum list number taken from "CXX" header (e.g., 35).</summary>
        public int EnumNumber { get; set; }

        /// <summary>Optional name inferred from DF='...:CODE0035.ENT'.</summary>
        public string Name { get; set; }

        /// <summary>Entries keyed by numeric code (e.g., 2 → "A").</summary>
        public Dictionary<int, CodeEntry> Entries { get; private set; }

        public CodeList()
        {
            Entries = new Dictionary<int, CodeEntry>();
        }
    }
}
