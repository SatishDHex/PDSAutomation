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
    public sealed class ToolMapEnumListService
    {
        /// <summary>
        /// Ensures an SPMapEnumListDef exists for the given CodeList.
        /// Returns the element and whether it was created (true) or already existed (false).
        /// </summary>
        public Tuple<XElement, bool> EnsureEnumListDef(
            IToolMapSchemaRepository repo,
            CodeList cl,
            ILogger log,
            string parentElementName = null // if ToolMap schema has a wrapper node, pass it; else root is used
        )
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (cl == null) throw new ArgumentNullException(nameof(cl));

            var doc = repo.Document;
            var parent = (parentElementName == null)
                ? doc.Root
                : doc.Root.Descendants(parentElementName).FirstOrDefault() ?? doc.Root;

            var uid = "PDS3DEnumList_" + cl.EnumNumber;

            // Find existing <SPMapEnumListDef> where child <IObject @UID=uid>
            var existing = parent
                .Descendants("SPMapEnumListDef")
                .FirstOrDefault(def =>
                {
                    var io = def.Element("IObject");
                    return io != null && (string)io.Attribute("UID") == uid;
                });

            if (existing != null)
            {
                // Optional: warn if Name mismatches
                var io = existing.Element("IObject");
                var existingName = io == null ? null : (string)io.Attribute("Name");
                var targetName = cl.Name ?? string.Empty;

                if (!string.IsNullOrEmpty(targetName) && !string.Equals(existingName, targetName, StringComparison.Ordinal))
                {
                    log.Warn(string.Format(
                        "ToolMap EnumList exists but Name differs for UID={0}. Existing='{1}', Parsed='{2}'",
                        uid, existingName ?? "(null)", targetName));
                }
                else
                {
                    log.Info("ToolMap EnumList already present for UID=" + uid);
                }

                return Tuple.Create(existing, false);
            }

            // Create new node
            var newDef = new XElement("SPMapEnumListDef",
                new XElement("IObject",
                    new XAttribute("UID", uid),
                    new XAttribute("Name", (object)(cl.Name ?? string.Empty))
                ),
                new XElement("IMapObject"),
                new XElement("IMapEnumListDef",
                    new XAttribute("ProcessEnumListCriteria", string.Empty)
                )
            );

            parent.Add(newDef);
            log.Success(string.Format(
                "Created ToolMap EnumList: UID={0}, Name='{1}'",
                uid, cl.Name ?? string.Empty));

            return Tuple.Create(newDef, true);
        }
    }
}
