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
                    // ----- CROSS-SYSTEM: ensure relation, reconcile name, disambiguate by parent if needed -----
                    string spfEnumUid = null;
                    string spfEnumName = null;
                    string crossRelStatus = "Skipped";
                    string nameEqTag = "OK";
                    string spfPick = "Existing";
                    int candidateCount = 0;
                    string disamb = "None";

                    // Existing relation?
                    var existingRel = FindRelIRel(tmDoc, uid1: valueUid, defUid: DEF_ENUM__ENUM);
                    if (existingRel != null)
                    {
                        spfEnumUid = (string)existingRel.Attribute("UID2") ?? "(null)";

                        // fetch current SPF name (may be null if dangling)
                        var spfByUid = FindSpfEnumEnumByUid(spfDoc, spfEnumUid);
                        spfEnumName = spfByUid != null ? (string)spfByUid.Element("IObject")?.Attribute("Name") : null;

                        // Compare names
                        if (!EqName(shortName, spfEnumName))
                        {
                            // Mark mismatch; we will try to re-link
                            nameEqTag = "Fix";

                            // Candidates with matching Name
                            var candidates = FindSpfEnumEnumsByName(spfDoc, shortName);
                            candidateCount = candidates.Count;

                            XElement chosen = null;

                            if (candidateCount == 0)
                            {
                                // Create a new matching EnumEnum
                                chosen = CreateSpfEnumEnum(spfDoc, yy, shortName);
                                spfPick = "Created";
                                disamb = "None";
                            }
                            else if (candidateCount == 1)
                            {
                                chosen = candidates[0];
                                spfPick = "Existing";
                                disamb = "None";
                            }
                            else
                            {
                                // Many candidates → try to disambiguate via Excel parent
                                string ExcelparentName = null;
                                if (!string.IsNullOrEmpty(sheetName) && sheetHierarchy != null)
                                {
                                    ExcelparentName = HierarchyLookup.GetImmediateParentFromDeepest(sheetHierarchy, shortName);
                                }

                                if (!string.IsNullOrEmpty(ExcelparentName))
                                {
                                    // Pick the candidate whose parent list type name matches
                                    chosen = candidates.FirstOrDefault(c =>
                                    {
                                        var uid = (string)c.Element("IObject")?.Attribute("UID");
                                        var parent = GetSpfParentListTypeName(spfDoc, uid);
                                        return string.Equals(parent ?? "", ExcelparentName ?? "", StringComparison.OrdinalIgnoreCase);
                                    });

                                    disamb = "ByParent";
                                }

                                if (chosen == null)
                                {
                                    // Fall back to first candidate
                                    chosen = candidates[0];
                                    disamb = (string.IsNullOrEmpty(sheetName) ? "First" : "First");
                                }

                                spfPick = "Existing";
                            }

                            // Ensure relation points to chosen
                            spfEnumUid = (string)chosen.Element("IObject")?.Attribute("UID");
                            spfEnumName = (string)chosen.Element("IObject")?.Attribute("Name");
                            crossRelStatus = UpsertCrossRel(tmDoc, valueUid, spfEnumUid); // Updated/Created/Exists
                        }
                        else
                        {
                            crossRelStatus = "Exists";
                            nameEqTag = "OK";
                        }
                    }
                    else
                    {
                        // No relation yet → find by Name; create if missing
                        var candidates = FindSpfEnumEnumsByName(spfDoc, shortName);
                        candidateCount = candidates.Count;

                        XElement chosen = null;

                        if (candidateCount == 0)
                        {
                            chosen = CreateSpfEnumEnum(spfDoc, yy, shortName);
                            spfPick = "Created";
                            disamb = "None";
                        }
                        else if (candidateCount == 1)
                        {
                            chosen = candidates[0];
                            spfPick = "Existing";
                            disamb = "None";
                        }
                        else
                        {
                            // Try to disambiguate via Excel parent
                            string excelparentName = null;
                            if (!string.IsNullOrEmpty(sheetName) && sheetHierarchy != null)
                            {
                                excelparentName = HierarchyLookup.GetImmediateParentFromDeepest(sheetHierarchy, shortName);
                            }

                            if (!string.IsNullOrEmpty(excelparentName))
                            {
                                chosen = candidates.FirstOrDefault(c =>
                                {
                                    var uid = (string)c.Element("IObject")?.Attribute("UID");
                                    var parent = GetSpfParentListTypeName(spfDoc, uid);
                                    return string.Equals(parent ?? "", excelparentName ?? "", StringComparison.OrdinalIgnoreCase);
                                });

                                disamb = "ByParent";
                            }

                            if (chosen == null)
                            {
                                chosen = candidates[0];
                                disamb = (string.IsNullOrEmpty(sheetName) ? "First" : "First");
                            }

                            spfPick = "Existing";
                        }

                        spfEnumUid = (string)chosen.Element("IObject")?.Attribute("UID");
                        spfEnumName = (string)chosen.Element("IObject")?.Attribute("Name");
                        crossRelStatus = UpsertCrossRel(tmDoc, valueUid, spfEnumUid); // Created/Exists
                        nameEqTag = "OK"; // We linked to matching-name target
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

                    var line =
                        $"EnumList={enumUid} Entry={yy} Name='{shortName}' " +
                        $"Sheet='{sheetName ?? ""}' ParentName='{parentName ?? ""}' ParentSpf={(string.IsNullOrEmpty(parentSpfState) ? "NA" : parentSpfState)} " +
                        $"EnumDef={(enumDefStatus == OpStatus.Created ? "Created" : "Exists")} " +
                        $"ParentRel={(parentRelStatus == OpStatus.Created ? "Created" : "Exists")} " +
                        $"SpfPick={spfPick} uid2={spfEnumUid} Candidates={candidateCount} Disamb={disamb} " +
                        $"CrossRel={crossRelStatus} ContainsRel={containsRel} NameEq={nameEqTag}";

                    if (nameEqTag == "Fix" || crossRelStatus == "Created" || crossRelStatus == "Updated" || containsRel == "Created" ||
                        enumDefStatus == OpStatus.Created || parentRelStatus == OpStatus.Created || parentSpfState == "Created")
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

        // Find all SPF EnumEnum elements by IObject@Name (case-insensitive)
        private static List<XElement> FindSpfEnumEnumsByName(XDocument spfDoc, string name)
        {
            var list = spfDoc.Root.Elements("EnumEnum")
                .Where(e =>
                {
                    var obj = e.Element("IObject");
                    if (obj == null) return false;
                    var n = (string)obj.Attribute("Name");
                    return string.Equals(n ?? "", name ?? "", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            return list;
        }

        // Given an EnumEnum UID, find its parent EnumListType's Name via Contains relation
        private static string GetSpfParentListTypeName(XDocument spfDoc, string enumEnumUid)
        {
            if (string.IsNullOrWhiteSpace(enumEnumUid)) return null;

            var parentUid = spfDoc.Root.Elements("Rel")
                .Select(r => r.Element("IRel"))
                .Where(ir => ir != null &&
                             string.Equals((string)ir.Attribute("UID2"), enumEnumUid, StringComparison.Ordinal) &&
                             string.Equals((string)ir.Attribute("DefUID"), "Contains", StringComparison.Ordinal))
                .Select(ir => (string)ir.Attribute("UID1"))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(parentUid)) return null;

            var parent = spfDoc.Root.Elements("EnumListType")
                .FirstOrDefault(e => (string)e.Element("IObject")?.Attribute("UID") == parentUid);

            return parent != null ? (string)parent.Element("IObject")?.Attribute("Name") : null;
        }

        // Upsert TM cross relation MapEnumToEnum (update UID2 if relation exists with different target)
        private static string UpsertCrossRel(XDocument tmDoc, string valueUid, string targetSpfUid)
        {
            var rel = tmDoc.Root.Elements("Rel")
                .FirstOrDefault(r =>
                {
                    var ir = r.Element("IRel");
                    return ir != null &&
                           string.Equals((string)ir.Attribute("UID1"), valueUid, StringComparison.Ordinal) &&
                           string.Equals((string)ir.Attribute("DefUID"), "MapEnumToEnum", StringComparison.Ordinal);
                });

            if (rel != null)
            {
                var ir = rel.Element("IRel");
                var current = (string)ir.Attribute("UID2");
                if (!string.Equals(current, targetSpfUid, StringComparison.Ordinal))
                {
                    ir.SetAttributeValue("UID2", targetSpfUid);
                    return "Updated";
                }
                return "Exists";
            }

            CreateRelAfterLastRel(tmDoc,
                relUid: "{" + Guid.NewGuid().ToString().ToUpper() + "}",
                uid1: valueUid,
                uid2: targetSpfUid,
                defUid: "MapEnumToEnum");
            return "Created";
        }

        /// <summary>
        /// Find SPF <EnumEnum> by UID (IObject@UID == uid).
        /// Returns the element or null if not found.
        /// </summary>
        private static XElement FindSpfEnumEnumByUid(XDocument spfDoc, string uid)
        {
            if (spfDoc == null || spfDoc.Root == null || string.IsNullOrWhiteSpace(uid)) return null;

            return spfDoc.Root.Elements("EnumEnum")
                .FirstOrDefault(e =>
                {
                    var obj = e.Element("IObject");
                    return obj != null && string.Equals((string)obj.Attribute("UID"), uid, StringComparison.Ordinal);
                });
        }

        /// <summary>
        /// Case-insensitive name comparison with trimming and whitespace collapsing.
        /// Returns true if names are equivalent (ignoring case and repeated/leading/trailing whitespace).
        /// </summary>
        private static bool EqName(string a, string b)
        {
            return string.Equals(Norm(a), Norm(b), StringComparison.Ordinal);

            string Norm(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;

                s = s.Trim();

                // Collapse internal whitespace to single spaces and lower-case
                var sb = new StringBuilder(s.Length);
                bool prevWs = false;
                for (int i = 0; i < s.Length; i++)
                {
                    char ch = s[i];
                    if (char.IsWhiteSpace(ch))
                    {
                        if (!prevWs) { sb.Append(' '); prevWs = true; }
                    }
                    else
                    {
                        sb.Append(char.ToLowerInvariant(ch));
                        prevWs = false;
                    }
                }
                return sb.ToString();
            }
        }

        private static string TryGetParentFromHierarchy(
            Dictionary<string, MultiLevelHierarchy> hierarchiesBySheet,
            string sheetName,
            string leafName,
            out string parentName)
        {
            parentName = null;

            if (string.IsNullOrWhiteSpace(sheetName))
                return "NoSheet"; // INI didn’t map this EnumList

            if (hierarchiesBySheet == null || !hierarchiesBySheet.TryGetValue(sheetName, out var h) || h == null)
                return "NoHierarchy"; // sheet not parsed / missing

            // Look for leaf at deepest level
            var deepest = (h.EdgesPerLevel != null && h.EdgesPerLevel.Count > 0)
                ? h.EdgesPerLevel[h.EdgesPerLevel.Count - 1]
                : null;

            if (deepest == null) return "NoHierarchy";

            var normLeaf = (leafName ?? "").Trim().ToLowerInvariant();

            foreach (var kv in deepest) // kv.Key = parent, kv.Value = children set
            {
                if (kv.Value != null && kv.Value.Contains(normLeaf))
                {
                    parentName = kv.Key; // already normalized in builder
                    return "OK";
                }
            }

            return "NoLeaf"; // sheet exists, but leaf not listed
        }

    }
}
