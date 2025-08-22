using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using WindowsFormsApp1.Repositories.Interfaces;
using WindowsFormsApp1.Utilities;

namespace WindowsFormsApp1.Repositories
{
    public sealed class SpfSchemaXmlRepository : ISpfRepository
    {
        private readonly XmlDatabase _db;

        public SpfSchemaXmlRepository(string filePath)
        {
            // If SPF XML uses namespaces, register here:
            XmlNamespaceManager nsMgr = null;
            // Example (uncomment & adjust):
            // var nameTable = new NameTable();
            // nsMgr = new XmlNamespaceManager(nameTable);
            // nsMgr.AddNamespace("spf", "http://smartplant/foundation/namespace");

            _db = new XmlDatabase(filePath, nsMgr);
        }

        public string SaveAsNextVersion(string pattern = "_{0:000}")
        {
            var target = VersionedFile.GetNextVersionPath(FilePath, pattern);
            Save(target);
            return target;
        }

        public XDocument Document { get { return _db.Document; } }
        public string FilePath { get { return _db.FilePath; } }

        public XElement SelectOne(string xpath) { return _db.SelectElement(xpath); }
        public IEnumerable<XElement> SelectMany(string xpath) { return _db.SelectElements(xpath); }

        public void Save(string targetPath = null) { _db.Save(targetPath); }
    }
}
