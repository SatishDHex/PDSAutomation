using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using WindowsFormsApp1.Logging.Interfaces;
using WindowsFormsApp1.Models;
using WindowsFormsApp1.Repositories.Interfaces;

namespace WindowsFormsApp1.Parsers
{
    /// <summary>
    /// Scans ToolMap schema for SPMapEnumListDef entries and checks for MapEnumListToEnumList relations.
    /// Pure read-only: no XML modifications.
    /// </summary>
    public static class ToolMapEnumListScanner
    {
        public static List<EnumListScanResult> ScanEnumListsAndRelations(
            IToolMapSchemaRepository toolMapRepo,
            ISpfRepository spfRepo,
            ILogger logger = null)
        {
            if (toolMapRepo == null) throw new ArgumentNullException(nameof(toolMapRepo));
            if (spfRepo == null) throw new ArgumentNullException(nameof(spfRepo));

            var tmDoc = toolMapRepo.Document;
            var spfDoc = spfRepo.Document;

            if (tmDoc == null || tmDoc.Root == null)
                throw new InvalidOperationException("ToolMap document is not loaded.");
            if (spfDoc == null || spfDoc.Root == null)
                throw new InvalidOperationException("SPF document is not loaded.");

            const string relationDefUid = "MapEnumListToEnumList";

            var results = new List<EnumListScanResult>();
            var listDefs = tmDoc.Root.Descendants("SPMapEnumListDef").ToList();
            logger?.Info($"Found {listDefs.Count} SPMapEnumListDef node(s) in ToolMap.");

            foreach (var def in listDefs)
            {
                var io = def.Element("IObject");
                if (io == null)
                {
                    logger?.Warn("SPMapEnumListDef without <IObject> encountered. Skipping.");
                    continue;
                }

                var uid1 = (string)io.Attribute("UID");
                var tmName = (string)io.Attribute("Name");

                if (string.IsNullOrWhiteSpace(uid1))
                {
                    logger?.Warn("SPMapEnumListDef has empty IObject@UID. Skipping.");
                    continue;
                }

                logger?.Info($"Scanning EnumList UID={uid1}, Name='{tmName ?? ""}'.");

                // Parse enum number from UID1 like "PDS3DEnumList_125"
                int? enumNumber = null;
                var m = Regex.Match(uid1, @"^PDS3DEnumList_(\d+)$");
                if (m.Success)
                {
                    int parsed;
                    if (int.TryParse(m.Groups[1].Value, out parsed)) enumNumber = parsed;
                }

                // Find relation in ToolMap
                var relIrel = tmDoc.Root
                    .Descendants("Rel")
                    .Select(r => r.Element("IRel"))
                    .FirstOrDefault(irel =>
                        irel != null &&
                        string.Equals((string)irel.Attribute("UID1"), uid1, StringComparison.Ordinal) &&
                        string.Equals((string)irel.Attribute("DefUID"), relationDefUid, StringComparison.Ordinal));

                var result = new EnumListScanResult
                {
                    Uid1 = uid1,
                    EnumNumber = enumNumber,
                    Name = tmName,
                    RelationExists = relIrel != null
                };

                if (relIrel == null)
                {
                    logger?.Warn($"Relation missing: UID1={uid1}, DefUID={relationDefUid}.");
                    results.Add(result);
                    continue;
                }

                // Relation exists — log explicitly
                var uid2 = (string)relIrel.Attribute("UID2");
                result.RelationUid2 = uid2;
                logger?.Success($"Relation exists: UID1={uid1}, UID2={uid2 ?? "(null)"}, DefUID={relationDefUid}.");

                // SPF check: <EnumListType><IObject UID="uid2" Name="..."/></EnumListType>
                if (string.IsNullOrWhiteSpace(uid2))
                {
                    logger?.Warn($"Relation found but UID2 is empty for UID1={uid1}, DefUID={relationDefUid}.");
                    results.Add(result);
                    continue;
                }

                var spfMatchIObj = spfDoc.Root
                    .Descendants("EnumListType")
                    .Select(e => e.Element("IObject"))
                    .FirstOrDefault(o =>
                        o != null &&
                        string.Equals((string)o.Attribute("UID"), uid2, StringComparison.Ordinal));

                if (spfMatchIObj != null)
                {
                    var spfName = (string)spfMatchIObj.Attribute("Name");
                    result.SpfEnumListExists = true;
                    result.SpfEnumListName = spfName;

                    // Log SPF tag details: UID and Name
                    logger?.Success($"SPF EnumListType found: UID={uid2}, Name='{spfName ?? ""}'.");
                }
                else
                {
                    result.SpfEnumListExists = false;
                    logger?.Warn(string.Format("SPF EnumListType NOT FOUND for UID={0} (from ToolMap relation UID1={1}).", uid2, uid1));
                    logger?.Info(string.Format("Creating SPF EnumListType using UID2='{0}' and Name='{1}'{2}.",
                        uid2 ?? "",
                        tmName ?? "",
                        enumNumber.HasValue ? string.Format(", EnumNumber={0}", enumNumber.Value) : ""));

                    // Use UID2 from the relation, not a generated GUID
                    var created = CreateSpfEnumListType(spfRepo, uid2, tmName, enumNumber, logger);

                    if (created != null)
                    {
                        result.SpfEnumListExists = true;
                        result.SpfEnumListName = (string)created.Element("IObject")?.Attribute("Name");
                    }
                }
                
                results.Add(result);
            }

            logger?.Info("Completed ToolMap EnumList → Relation → SPF EnumListType scan.");
            return results;
        }

        /// <summary>
        /// Creates a new SPF <EnumListType> under the SPF document root.
        /// - IObject@UID is set to uid2 (from ToolMap relation).
        /// - IObject@Name is taken from ToolMap name.
        /// - Adds <ISchemaObj/> and <IPropertyType/>.
        /// - If enumNumber is provided, adds <IEnumEnum EnumNumber="N"/>; otherwise omitted.
        /// Returns the created XElement (EnumListType), or null if repo/doc missing.
        /// </summary>
        private static XElement CreateSpfEnumListType(
    ISpfRepository spfRepo,
    string uid2,                // UID from ToolMap relation (used as IObject@UID)
    string toolMapName,
    int? enumNumber,
    ILogger logger)
        {
            var doc = spfRepo.Document;
            if (doc == null || doc.Root == null)
            {
                logger?.Error("SPF repo has no loaded document; cannot create EnumListType.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(uid2))
            {
                logger?.Error("Cannot create SPF EnumListType: UID2 is null/empty.");
                return null;
            }

            var newNode = new XElement("EnumListType",
                new XElement("IObject",
                    new XAttribute("UID", uid2),
                    new XAttribute("Name", (object)(toolMapName ?? string.Empty))
                ),
                new XElement("ISchemaObj"),   // set SchemaRevVer later if needed
                new XElement("IPropertyType")
            );

            if (enumNumber.HasValue)
            {
                newNode.Add(new XElement("IEnumEnum", new XAttribute("EnumNumber", enumNumber.Value)));
            }

            // Insert policy:
            // 1) Find the last existing *direct child* <EnumListType> under root.
            // 2) If found -> insert AFTER it (keeps the EnumListType block contiguous).
            // 3) If none -> insert as the FIRST child under root (starts the block cleanly).
            var root = doc.Root;
            var lastEnumListType = root.Elements("EnumListType").LastOrDefault();

            if (lastEnumListType != null)
            {
                lastEnumListType.AddAfterSelf(newNode);
                logger?.Success(string.Format(
                    "Created SPF EnumListType after existing block: UID={0}, Name='{1}'{2}.",
                    uid2,
                    toolMapName ?? "",
                    enumNumber.HasValue ? string.Format(", EnumNumber={0}", enumNumber.Value) : ""
                ));
            }
            else
            {
                root.AddFirst(newNode);
                logger?.Success(string.Format(
                    "Created first SPF EnumListType at root: UID={0}, Name='{1}'{2}.",
                    uid2,
                    toolMapName ?? "",
                    enumNumber.HasValue ? string.Format(", EnumNumber={0}", enumNumber.Value) : ""
                ));
            }

            return newNode;
        }
    }
}
