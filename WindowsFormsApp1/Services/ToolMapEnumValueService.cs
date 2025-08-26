using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using WindowsFormsApp1.Logging.Interfaces;
using WindowsFormsApp1.Models;
using WindowsFormsApp1.Repositories.Interfaces;

namespace WindowsFormsApp1.Services
{
    /// <summary>
    /// Ensures SPMapEnumDef (value) nodes and EnumList->Enum relations in ToolMap,
    /// and checks (only logs) for cross-system MapEnumToEnum relations.
    /// </summary>
    public sealed class ToolMapEnumValueService
    {
        private enum OpStatus { Created, Exists }

        public void EnsureValuesAndRelations(
            IToolMapSchemaRepository toolMapRepo,
            IEnumerable<CodeList> codeLists,
            ILogger log)
        {
            if (toolMapRepo == null) throw new ArgumentNullException(nameof(toolMapRepo));
            if (codeLists == null) throw new ArgumentNullException(nameof(codeLists));

            var doc = toolMapRepo.Document;
            if (doc == null || doc.Root == null)
                throw new InvalidOperationException("ToolMap document not loaded.");

            foreach (var cl in codeLists)
            {
                var enumUid = "PDS3DEnumList_" + cl.EnumNumber;

                // optional: one lightweight log per CodeList
                log?.Info($"EnumList {enumUid} '{(cl.Name ?? "")}': processing {cl.Entries.Count} value(s)…");

                foreach (var kv in cl.Entries)
                {
                    var entry = kv.Value;
                    var yy = entry.Number;
                    var shortName = string.IsNullOrWhiteSpace(entry.Short) ? "undefined" : entry.Short;
                    var valueUid = $"{enumUid}_{yy}";

                    // --- Do work but DON'T log yet ---
                    var enumDefStatus = EnsureEnumDef(doc, valueUid, shortName, yy);
                    var parentRelStatus = EnsureParentRelation(doc, enumUid, valueUid);

                    // Cross-system relation: check only (no creation here)
                    var crossIrel = FindRelIRel(doc, uid1: valueUid, defUid: "MapEnumToEnum");
                    bool crossExists = crossIrel != null;
                    string crossUid2 = crossExists ? ((string)crossIrel.Attribute("UID2") ?? "(null)") : null;

                    // --- Emit ONE compact line for this entry ---
                    // Example:
                    // OK: EnumList=PDS3DEnumList_125 Entry=1 Name='undefined' EnumDef=Created ParentRel=Created CrossRel=OK uid2={GUID}
                    // or
                    // OK: EnumList=PDS3DEnumList_125 Entry=2 Name='HX' EnumDef=Exists ParentRel=Exists CrossRel=MISSING
                    var line =
                        $"EnumList={enumUid} Entry={yy} Name='{shortName}' " +
                        $"EnumDef={(enumDefStatus == OpStatus.Created ? "Created" : "Exists")} " +
                        $"ParentRel={(parentRelStatus == OpStatus.Created ? "Created" : "Exists")} " +
                        $"CrossRel={(crossExists ? "OK uid2=" + crossUid2 : "MISSING")}";

                    // choose Success for created, Info for exists only
                    if (enumDefStatus == OpStatus.Created || parentRelStatus == OpStatus.Created)
                        log?.Success(line);
                    else if (!crossExists)
                        log?.Warn(line); // nothing created but missing cross-rel
                    else
                        log?.Info(line);
                }
            }
        }

        // ---------- Helpers (no logging) ----------

        private static OpStatus EnsureEnumDef(XDocument doc, string valueUid, string name, int mapEnumNumber)
        {
            var existing = FindEnumDef(doc, valueUid);
            if (existing != null)
            {
                // (optional) sync name/number if you want strict parity:
                // UpdateIfDifferent(existing.Element("IObject"), "Name", name);
                // UpdateIfDifferent(existing.Element("IMapEnumDef"), "MapEnumNumber", mapEnumNumber.ToString());
                return OpStatus.Exists;
            }

            CreateEnumDef(doc, valueUid, name, mapEnumNumber);
            return OpStatus.Created;
        }

        private static XElement FindEnumDef(XDocument doc, string valueUid)
        {
            return doc.Root
                      .Elements("SPMapEnumDef")
                      .FirstOrDefault(d =>
                      {
                          var obj = d.Element("IObject");
                          return obj != null && (string)obj.Attribute("UID") == valueUid;
                      });
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
                new XElement("IMapEnumDef",
                    new XAttribute("MapEnumNumber", mapEnumNumber)
                )
            );

            var root = doc.Root;

            // Keep SPMapEnumDef block contiguous; else after EnumListDefs; else at end
            var lastEnumDef = root.Elements("SPMapEnumDef").LastOrDefault();
            if (lastEnumDef != null)
            {
                lastEnumDef.AddAfterSelf(node);
            }
            else
            {
                var lastEnumListDef = root.Elements("SPMapEnumListDef").LastOrDefault();
                if (lastEnumListDef != null) lastEnumListDef.AddAfterSelf(node);
                else root.Add(node);
            }
        }

        private static OpStatus EnsureParentRelation(XDocument doc, string enumUid, string valueUid)
        {
            const string defUid = "MapEnumListMapEnum";

            var exists = doc.Root.Elements("Rel")
                .Select(r => r.Element("IRel"))
                .Any(irel =>
                    irel != null
                    && string.Equals((string)irel.Attribute("UID1"), enumUid, StringComparison.Ordinal)
                    && string.Equals((string)irel.Attribute("UID2"), valueUid, StringComparison.Ordinal)
                    && string.Equals((string)irel.Attribute("DefUID"), defUid, StringComparison.Ordinal));

            if (exists) return OpStatus.Exists;

            var relUid = $"{enumUid}-HasValue-{valueUid.Substring(enumUid.Length + 1)}"; // last piece is YY

            var rel = new XElement("Rel",
                new XElement("IObject", new XAttribute("UID", relUid)),
                new XElement("IRel",
                    new XAttribute("UID1", enumUid),
                    new XAttribute("UID2", valueUid),
                    new XAttribute("DefUID", defUid)
                )
            );

            var root = doc.Root;
            var lastRel = root.Elements("Rel").LastOrDefault();
            if (lastRel != null) lastRel.AddAfterSelf(rel);
            else root.Add(rel);

            return OpStatus.Created;
        }

        private static XElement FindRelIRel(XDocument doc, string uid1, string defUid)
        {
            return doc.Root.Elements("Rel")
                .Select(r => r.Element("IRel"))
                .FirstOrDefault(irel =>
                    irel != null
                    && string.Equals((string)irel.Attribute("UID1"), uid1, StringComparison.Ordinal)
                    && string.Equals((string)irel.Attribute("DefUID"), defUid, StringComparison.Ordinal));
        }

        // If you decide to sync attributes later:
        // private static void UpdateIfDifferent(XElement el, string attr, string value)
        // {
        //     if (el == null) return;
        //     var a = el.Attribute(attr);
        //     if (a == null || !string.Equals(a.Value, value, StringComparison.Ordinal))
        //         el.SetAttributeValue(attr, value);
        // }
    }
}
