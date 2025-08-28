using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using WindowsFormsApp1.Logging.Interfaces;
using WindowsFormsApp1.Models;
using WindowsFormsApp1.Repositories.Interfaces;
using WindowsFormsApp1.Utilities;

namespace WindowsFormsApp1.Services
{
    /// <summary>
    /// Ensures SPMapEnumDef (value) nodes and EnumList->Enum relations in ToolMap,
    /// and checks (only logs) for cross-system MapEnumToEnum relations.
    /// </summary>
    public sealed class ToolMapEnumValueService
    {
        private enum OpStatus { Created, Exists }
        private const string DEF_ENUM_LIST__ENUM = "MapEnumListMapEnum";
        private const string DEF_ENUM__ENUM = "MapEnumToEnum";
        private const string DEF_CONTAINS = "Contains";
        private const string SPF_SCHEMA_REV = "03.06.01.24";

        public void EnsureValuesAndRelations(
            IToolMapSchemaRepository toolMapRepo,
            ISpfRepository spfRepo,
            IEnumerable<CodeList> codeLists,
            Dictionary<int, string> enumToSheet,
            Dictionary<string, MultiLevelHierarchy> hierarchiesBySheet,
            ILogger log)
        {
            if (toolMapRepo == null) throw new ArgumentNullException(nameof(toolMapRepo));
            if (spfRepo == null) throw new ArgumentNullException(nameof(spfRepo));
            if (codeLists == null) throw new ArgumentNullException(nameof(codeLists));

            var tmDoc = toolMapRepo.Document;
            var spfDoc = spfRepo.Document;
            if (tmDoc == null || tmDoc.Root == null) throw new InvalidOperationException("ToolMap not loaded.");
            if (spfDoc == null || spfDoc.Root == null) throw new InvalidOperationException("SPF not loaded.");

            foreach (var cl in codeLists)
            {
                var enumUid = "PDS3DEnumList_" + cl.EnumNumber;
                log?.Info($"EnumList {enumUid} '{(cl.Name ?? "")}': processing {cl.Entries.Count} value(s)…");

                // Resolve sheet name for this enum number
                MultiLevelHierarchy sheetHierarchy = null;
                string sheetName = null;
                enumToSheet?.TryGetValue(cl.EnumNumber, out sheetName);
                hierarchiesBySheet?.TryGetValue(sheetName ?? "", out sheetHierarchy);

                foreach (var kv in cl.Entries)
                {
                    var entry = kv.Value;
                    var yy = entry.Number;
                    var shortName = string.IsNullOrWhiteSpace(entry.Short) ? "undefined" : entry.Short;
                    var valueUid = $"{enumUid}_{yy}";

                    // ToolMap value node & parent relation
                    var enumDefStatus = EnsureEnumDef(tmDoc, valueUid, shortName, yy);
                    var parentRelStatus = EnsureParentRelation(tmDoc, enumUid, valueUid);

                    // Cross-system Enum->Enum
                    string spfEnumUid; OpStatus spfEnumStatus;
                    string crossRelStatus;

                    var crossIrel = FindRelIRel(tmDoc, uid1: valueUid, defUid: DEF_ENUM__ENUM);
                    if (crossIrel != null)
                    {
                        spfEnumUid = (string)crossIrel.Attribute("UID2") ?? "(null)";
                        spfEnumStatus = OpStatus.Exists;
                        crossRelStatus = "Exists";
                    }
                    else
                    {
                        // Find/create SPF EnumEnum by NAME ONLY
                        var spfFound = FindSpfEnumEnumByName(spfDoc, shortName, out var matchCount);
                        if (spfFound != null)
                        {
                            spfEnumUid = (string)spfFound.Element("IObject")?.Attribute("UID");
                            spfEnumStatus = OpStatus.Exists;
                        }
                        else
                        {
                            var created = CreateSpfEnumEnum(spfDoc, yy, shortName);
                            spfEnumUid = (string)created.Element("IObject")?.Attribute("UID");
                            spfEnumStatus = OpStatus.Created;
                        }

                        // Create TM cross relation now
                        CreateRelAfterLastRel(tmDoc,
                            relUid: "{" + Guid.NewGuid().ToString().ToUpper() + "}",
                            uid1: valueUid,
                            uid2: spfEnumUid,
                            defUid: DEF_ENUM__ENUM);

                        crossRelStatus = "Created";
                    }

                    // NEW: Try to link EnumEnum to its parent EnumListType in SPF using Excel hierarchy
                    string parentName = null;
                    string parentUid = null;
                    string containsRel = "Skipped";
                    string parentSpfState = "";

                    if (!string.IsNullOrEmpty(sheetName) && sheetHierarchy != null)
                    {
                        parentName = HierarchyLookup.GetImmediateParentFromDeepest(sheetHierarchy, shortName);

                        if (!string.IsNullOrEmpty(parentName))
                        {
                            var parentElt = FindSpfEnumListTypeByName(spfDoc, parentName, out var parentMatches);
                            if (parentElt != null)
                            {
                                parentUid = (string)parentElt.Element("IObject")?.Attribute("UID");

                                // Create Contains if missing
                                if (!ExistsRel(spfDoc, parentUid, spfEnumUid, DEF_CONTAINS))
                                {
                                    CreateContainsRel(spfDoc, parentUid, spfEnumUid);
                                    containsRel = "Created";
                                }
                                else
                                {
                                    containsRel = "Exists";
                                }
                            }
                            else
                            {
                                // Parent not present in SPF → create it, then create Contains
                                var createdParent = CreateSpfEnumListTypeByName(spfDoc, parentName);
                                parentUid = (string)createdParent.Element("IObject")?.Attribute("UID");
                                parentSpfState = "Created";

                                CreateContainsRel(spfDoc, parentUid, spfEnumUid);
                                containsRel = "Created";
                            }
                        }
                        else
                        {
                            containsRel = "NoParent";
                        }
                    }
                    else
                    {
                        containsRel = "NoSheet";
                    }

                    // ONE compact line
                    var line =
                        $"EnumList={enumUid} Entry={yy} Name='{shortName}' " +
                        $"Sheet='{sheetName ?? ""}' ParentName='{parentName ?? ""}' ParentSpf={(string.IsNullOrEmpty(parentSpfState) ? "NA" : parentSpfState)} " +
                        $"EnumDef={(enumDefStatus == OpStatus.Created ? "Created" : "Exists")} " +
                        $"ParentRel={(parentRelStatus == OpStatus.Created ? "Created" : "Exists")} " +
                        $"SpfEnum={(spfEnumStatus == OpStatus.Created ? "Created" : "Exists")} uid2={spfEnumUid} " +
                        $"CrossRel={crossRelStatus} ContainsRel={containsRel}";

                    // severity
                    if (enumDefStatus == OpStatus.Created || parentRelStatus == OpStatus.Created ||
                        spfEnumStatus == OpStatus.Created || crossRelStatus == "Created" || containsRel == "Created")
                        log?.Success(line);
                    else
                        log?.Info(line);
                }
            }
        }

        // ---------- ToolMap value node & relations ----------
        private static OpStatus EnsureEnumDef(XDocument doc, string valueUid, string name, int mapEnumNumber)
        {
            var existing = FindEnumDef(doc, valueUid);
            if (existing != null) return OpStatus.Exists;

            CreateEnumDef(doc, valueUid, name, mapEnumNumber);
            return OpStatus.Created;
        }

        private static XElement FindEnumDef(XDocument doc, string valueUid)
        {
            return doc.Root.Elements("SPMapEnumDef")
                .FirstOrDefault(d => (string)d.Element("IObject")?.Attribute("UID") == valueUid);
        }

        private static void CreateEnumDef(XDocument doc, string valueUid, string name, int mapEnumNumber)
        {
            var node = new XElement("SPMapEnumDef",
                new XElement("IObject",
                    new XAttribute("UID", valueUid),
                    new XAttribute("Name", name),
                    new XAttribute("Description", string.Empty)
                ),
                new XElement("IMapObject"),
                new XElement("IMapEnumDef", new XAttribute("MapEnumNumber", mapEnumNumber))
            );

            var root = doc.Root;
            var lastEnumDef = root.Elements("SPMapEnumDef").LastOrDefault();
            if (lastEnumDef != null) lastEnumDef.AddAfterSelf(node);
            else
            {
                var lastEnumListDef = root.Elements("SPMapEnumListDef").LastOrDefault();
                if (lastEnumListDef != null) lastEnumListDef.AddAfterSelf(node);
                else root.Add(node);
            }
        }

        private static OpStatus EnsureParentRelation(XDocument doc, string enumUid, string valueUid)
        {
            if (ExistsRel(doc, enumUid, valueUid, DEF_ENUM_LIST__ENUM)) return OpStatus.Exists;

            var yy = valueUid.Substring(enumUid.Length + 1); // last part
            var relUid = $"{enumUid}-HasValue-{yy}";
            CreateRelAfterLastRel(doc, relUid, enumUid, valueUid, DEF_ENUM_LIST__ENUM);
            return OpStatus.Created;
        }

        private static bool ExistsRel(XDocument doc, string uid1, string uid2, string defUid)
        {
            return doc.Root.Elements("Rel")
                .Select(r => r.Element("IRel"))
                .Any(irel =>
                    irel != null &&
                    string.Equals((string)irel.Attribute("UID1"), uid1, StringComparison.Ordinal) &&
                    string.Equals((string)irel.Attribute("UID2"), uid2, StringComparison.Ordinal) &&
                    string.Equals((string)irel.Attribute("DefUID"), defUid, StringComparison.Ordinal));
        }

        private static XElement FindRelIRel(XDocument doc, string uid1, string defUid)
        {
            return doc.Root.Elements("Rel")
                .Select(r => r.Element("IRel"))
                .FirstOrDefault(irel =>
                    irel != null &&
                    string.Equals((string)irel.Attribute("UID1"), uid1, StringComparison.Ordinal) &&
                    string.Equals((string)irel.Attribute("DefUID"), defUid, StringComparison.Ordinal));
        }

        private static void CreateRelAfterLastRel(XDocument doc, string relUid, string uid1, string uid2, string defUid)
        {
            var rel = new XElement("Rel",
                new XElement("IObject", new XAttribute("UID", relUid)),
                new XElement("IRel",
                    new XAttribute("UID1", uid1),
                    new XAttribute("UID2", uid2),
                    new XAttribute("DefUID", defUid)
                )
            );

            var root = doc.Root;
            var lastRel = root.Elements("Rel").LastOrDefault();
            if (lastRel != null) lastRel.AddAfterSelf(rel);
            else root.Add(rel);
        }

        // ---------- SPF: EnumEnum & EnumListType & Contains ----------
        private static XElement FindSpfEnumEnumByName(XDocument spfDoc, string shortName, out int matchCount)
        {
            var matches = spfDoc.Root.Elements("EnumEnum")
                .Where(e =>
                {
                    var obj = e.Element("IObject");
                    if (obj == null) return false;
                    var name = (string)obj.Attribute("Name");
                    return string.Equals(name ?? "", shortName ?? "", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            matchCount = matches.Count;
            return matches.FirstOrDefault();
        }

        private static XElement CreateSpfEnumEnum(XDocument spfDoc, int yy, string shortName)
        {
            var uid = "{" + Guid.NewGuid().ToString().ToUpper() + "}";

            var node = new XElement("EnumEnum",
                new XElement("IObject",
                    new XAttribute("UID", uid),
                    new XAttribute("Name", shortName ?? string.Empty),
                    new XAttribute("Description", string.Empty)
                ),
                new XElement("ISchemaObj", new XAttribute("SchemaRevVer", SPF_SCHEMA_REV)),
                new XElement("IEnumEnum", new XAttribute("EnumNumber", yy))
            );

            var root = spfDoc.Root;
            var lastEnumEnum = root.Elements("EnumEnum").LastOrDefault();
            if (lastEnumEnum != null) lastEnumEnum.AddAfterSelf(node);
            else
            {
                var lastEnumListType = root.Elements("EnumListType").LastOrDefault();
                if (lastEnumListType != null) lastEnumListType.AddAfterSelf(node);
                else root.Add(node);
            }

            return node;
        }

        private static XElement FindSpfEnumListTypeByName(XDocument spfDoc, string name, out int matchCount)
        {
            var matches = spfDoc.Root.Elements("EnumListType")
                .Where(e =>
                {
                    var obj = e.Element("IObject");
                    if (obj == null) return false;
                    var n = (string)obj.Attribute("Name");
                    return string.Equals(n ?? "", name ?? "", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            matchCount = matches.Count;
            return matches.FirstOrDefault();
        }

        private static void CreateContainsRel(XDocument spfDoc, string parentUid, string childEnumUid)
        {
            var rel = new XElement("Rel",
                new XElement("IObject", new XAttribute("UID", "{" + Guid.NewGuid().ToString().ToUpper() + "}")),
                new XElement("IRel",
                    new XAttribute("UID1", parentUid),
                    new XAttribute("UID2", childEnumUid),
                    new XAttribute("DefUID", DEF_CONTAINS)
                ),
                new XElement("ISchemaObj", new XAttribute("SchemaRevVer", SPF_SCHEMA_REV))
            );

            var root = spfDoc.Root;
            var lastRel = root.Elements("Rel").LastOrDefault();
            if (lastRel != null) lastRel.AddAfterSelf(rel);
            else root.Add(rel);
        }

        private static XElement CreateSpfEnumListTypeByName(XDocument spfDoc, string name)
        {
            var uid = "{" + Guid.NewGuid().ToString().ToUpper() + "}";
            var node = new XElement("EnumListType",
                new XElement("IObject",
                    new XAttribute("UID", uid),
                    new XAttribute("Name", name ?? string.Empty),
                    new XAttribute("Description", string.Empty)
                ),
                new XElement("ISchemaObj", new XAttribute("SchemaRevVer", SPF_SCHEMA_REV)),
                new XElement("IPropertyType"),
                new XElement("IEnumListType")
            // NOTE: We do NOT add <IEnumEnum EnumNumber="..."> here because we don't have a reliable number for a parent list.
            );

            var root = spfDoc.Root;
            var lastListType = root.Elements("EnumListType").LastOrDefault();
            if (lastListType != null) lastListType.AddAfterSelf(node);
            else root.AddFirst(node); // start the EnumListType block at the top if none exist

            return node;
        }
    }
}
