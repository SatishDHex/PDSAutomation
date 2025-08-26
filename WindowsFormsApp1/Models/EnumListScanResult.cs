using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsFormsApp1.Models
{
    public sealed class EnumListScanResult
    {
        public string Uid1 { get; set; }              // e.g., "PDS3DEnumList_125"
        public int? EnumNumber { get; set; }          // 125 (parsed from UID1), may be null
        public string Name { get; set; }              // ToolMap EnumList Name (from IObject@Name)

        public bool RelationExists { get; set; }      // ToolMap relation exists?
        public string RelationUid2 { get; set; }      // The UID2 extracted from ToolMap relation (e.g., "FluidSystems")

        public bool SpfEnumListExists { get; set; }   // SPF <EnumListType> with IObject@UID == UID2
        public string SpfEnumListName { get; set; }   // SPF IObject@Name (if found)
    }
}
